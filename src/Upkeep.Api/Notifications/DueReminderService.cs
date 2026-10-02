using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Upkeep;
using Upkeep.Infrastructure;

namespace Upkeep.Api.Notifications;

/// <summary>Envio de lembretes de manutenções vencidas/a vencer via ntfy.</summary>
public interface INotificationService
{
    /// <summary>
    /// Varre todos os usuários com tópico configurado e envia 1 push por usuário com
    /// pendências. Retorna o nº de envios bem-sucedidos (falhas individuais só logam).
    /// </summary>
    Task<int> SendDueRemindersAsync(CancellationToken ct);
}

/// <summary>Config da seção "Ntfy" (Server/Enabled/CheckHourUtc).</summary>
public sealed class NtfyOptions
{
    public string Server { get; init; } = "https://ntfy.sh";
    public bool Enabled { get; init; }
    public int CheckHourUtc { get; init; } = 11;
}

/// <summary>
/// Varredura multi-usuário: usuários com NtfyTopic → seus assets → templates → último
/// serviço por template (mesma resolução de baseline do StatusService: Data desc, Id desc)
/// → DueCalculator → 1 POST ntfy por usuário com qualquer Overdue/DueSoon.
/// </summary>
public sealed class DueReminderService(
    UpkeepDbContext db,
    IHttpClientFactory httpFactory,
    ILogger<DueReminderService> logger,
    IOptions<NtfyOptions> ntfy) : INotificationService
{
    public async Task<int> SendDueRemindersAsync(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Só usuários com tópico recebem push; sem candidatos, sai cedo.
        var topics = await db.Users
            .Where(u => u.NtfyTopic != null)
            .ToDictionaryAsync(u => u.Id, u => u.NtfyTopic!, ct);
        if (topics.Count == 0) return 0;
        var userIds = topics.Keys.ToList();

        var assets = await db.Assets.Where(a => userIds.Contains(a.UserId)).ToListAsync(ct);
        var assetIds = assets.Select(a => a.Id).ToList();
        var templates = await db.Templates.Where(t => assetIds.Contains(t.AssetId)).ToListAsync(ct);

        // Último serviço por template em 1 query; redução em memória (sem N+1) — idem StatusService.
        var templateIds = templates.Select(t => t.Id).ToList();
        var services = await db.Services
            .Where(s => s.TemplateId != null && templateIds.Contains(s.TemplateId!.Value))
            .ToListAsync(ct);
        var latestByTemplate = services
            .GroupBy(s => s.TemplateId!.Value)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(s => s.Data).ThenByDescending(s => s.Id).First());

        // Avalia cada template e agrupa as pendências por usuário (dono do asset).
        var assetById = assets.ToDictionary(a => a.Id);
        var dueByUser = new Dictionary<Guid, List<DueItem>>();
        foreach (var template in templates)
        {
            var asset = assetById[template.AssetId];
            var service = latestByTemplate.GetValueOrDefault(template.Id);
            // Serviço sem odômetro → critério km cai no BaselineOdometro do template (??),
            // mas a Data do serviço ainda reseta o critério tempo.
            var baseline = new DueBaseline(
                service?.Odometro ?? template.BaselineOdometro,
                service?.Data ?? template.BaselineData);
            var r = DueCalculator.Evaluate(template, baseline, asset.OdometroAtual, today);
            if (r.Status == DueStatus.Ok) continue;

            if (!dueByUser.TryGetValue(asset.UserId, out var items))
                dueByUser[asset.UserId] = items = [];
            items.Add(new DueItem(asset.Nome, template.Titulo, r.Status, r.KmRemaining, r.DateDue));
        }

        var http = httpFactory.CreateClient("ntfy");
        var sent = 0;
        foreach (var (userId, items) in dueByUser)
        {
            var (title, body) = ReminderText.Format(items);
            try
            {
                var resp = await http.PostAsJsonAsync(ntfy.Value.Server, new
                {
                    topic = topics[userId],
                    title,
                    message = body,
                    tags = new[] { "wrench" },
                    priority = items.Any(i => i.Status == DueStatus.Overdue) ? 4 : 3
                }, ct);
                resp.EnsureSuccessStatusCode();
                sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // Falha num usuário não pode derrubar a varredura dos demais.
                logger.LogWarning(ex, "Falha ao notificar usuário {UserId} no ntfy", userId);
            }
        }
        return sent;
    }
}
