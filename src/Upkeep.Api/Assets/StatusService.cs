using Microsoft.EntityFrameworkCore;
using Upkeep;
using Upkeep.Infrastructure;

namespace Upkeep.Api.Assets;

public interface IStatusService
{
    /// <summary>Status agregado de um asset; null se o asset não pertencer ao usuário.</summary>
    Task<AssetStatusDto?> GetAssetStatusAsync(Guid assetId, Guid userId, CancellationToken ct);

    /// <summary>Status por template do asset; vazia se o asset não pertencer ao usuário.</summary>
    Task<IReadOnlyList<TemplateStatusDto>> GetTemplateStatusesAsync(Guid assetId, Guid userId, CancellationToken ct);

    /// <summary>
    /// Status agregado de TODOS os assets do usuário em 3 queries totais
    /// (assets → templates → últimos serviços projetados), sem N×3 do caminho
    /// por asset. Ordem preservada da query de assets.
    /// </summary>
    Task<IReadOnlyList<(Asset Asset, AssetStatusDto Status)>> GetAllAssetStatusesAsync(
        Guid userId, CancellationToken ct);
}

public sealed record AssetStatusDto(string Status, int Overdue, int DueSoon, int Ok);
public sealed record TemplateStatusDto(Guid TemplateId, string Status, int? KmRemaining, DateOnly? DateDue);

/// <summary>Colunas mínimas do último serviço por template (projeção da query batch).</summary>
internal readonly record struct LatestServiceDto(Guid TemplateId, DateOnly Data, int? Odometro, Guid Id);

public sealed class StatusService(UpkeepDbContext db) : IStatusService
{
    private const string LabelOk = "ok";
    private const string LabelDueSoon = "vence_em_breve";
    private const string LabelOverdue = "vencido";

    public async Task<AssetStatusDto?> GetAssetStatusAsync(Guid assetId, Guid userId, CancellationToken ct)
    {
        var asset = await db.GetOwnedAssetAsync(assetId, userId, ct);
        if (asset is null) return null;
        return Aggregate(await EvaluateAsync(asset, ct));
    }

    public async Task<IReadOnlyList<TemplateStatusDto>> GetTemplateStatusesAsync(
        Guid assetId, Guid userId, CancellationToken ct)
    {
        var asset = await db.GetOwnedAssetAsync(assetId, userId, ct);
        if (asset is null) return [];
        return await EvaluateAsync(asset, ct);
    }

    public async Task<IReadOnlyList<(Asset Asset, AssetStatusDto Status)>> GetAllAssetStatusesAsync(
        Guid userId, CancellationToken ct)
    {
        var assets = await db.Assets.Where(a => a.UserId == userId).ToListAsync(ct);
        if (assets.Count == 0) return [];

        var assetIds = assets.Select(a => a.Id).ToList();
        var templates = await db.Templates
            .Where(t => assetIds.Contains(t.AssetId))
            .ToListAsync(ct);
        var latestByTemplate = await GetLatestServicesAsync(templates.Select(t => t.Id).ToList(), ct);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var byAsset = templates.ToLookup(t => t.AssetId);
        var result = new List<(Asset, AssetStatusDto)>(assets.Count);
        foreach (var asset in assets)
            result.Add((asset, Aggregate(EvaluateTemplates(byAsset[asset.Id], latestByTemplate, asset, today))));
        return result;
    }

    /// <summary>Templates + últimos serviços do asset (2 queries); núcleo de avaliação compartilhado.</summary>
    private async Task<IReadOnlyList<TemplateStatusDto>> EvaluateAsync(Asset asset, CancellationToken ct)
    {
        var templates = await db.Templates.Where(t => t.AssetId == asset.Id).ToListAsync(ct);
        if (templates.Count == 0) return [];
        var latestByTemplate = await GetLatestServicesAsync(templates.Select(t => t.Id).ToList(), ct);
        return EvaluateTemplates(templates, latestByTemplate, asset, DateOnly.FromDateTime(DateTime.UtcNow));
    }

    /// <summary>
    /// Último serviço por template em 1 query com PROJEÇÃO (só TemplateId/Data/Odometro/Id —
    /// sem custo/notas/created); redução em memória, Data desc + Id desc (tiebreak determinístico).
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, LatestServiceDto>> GetLatestServicesAsync(
        IReadOnlyCollection<Guid> templateIds, CancellationToken ct)
    {
        if (templateIds.Count == 0) return new Dictionary<Guid, LatestServiceDto>();

        var services = await db.Services
            .Where(s => s.TemplateId != null && templateIds.Contains(s.TemplateId!.Value))
            .Select(s => new LatestServiceDto(s.TemplateId!.Value, s.Data, s.Odometro, s.Id))
            .ToListAsync(ct);

        return services
            .GroupBy(s => s.TemplateId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(s => s.Data).ThenByDescending(s => s.Id).First());
    }

    /// <summary>
    /// Núcleo compartilhado: avalia templates já carregados contra os últimos serviços já
    /// reduzidos (dados pré-carregados — caminho single e caminho batch passam por aqui).
    /// </summary>
    private static IReadOnlyList<TemplateStatusDto> EvaluateTemplates(
        IEnumerable<MaintenanceTemplate> templates,
        IReadOnlyDictionary<Guid, LatestServiceDto> latestByTemplate,
        Asset asset,
        DateOnly today)
    {
        var result = new List<TemplateStatusDto>();
        foreach (var template in templates)
        {
            // nullable struct: sem TryGetValue o default teria Data=0001-01-01 (baseline errado)
            LatestServiceDto? service =
                latestByTemplate.TryGetValue(template.Id, out var s) ? s : null;
            // Serviço sem odômetro → critério km cai de volta ao BaselineOdometro do template (??),
            // mas a Data do serviço ainda reseta o critério tempo.
            var baseline = new DueBaseline(
                service?.Odometro ?? template.BaselineOdometro,
                service?.Data ?? template.BaselineData);
            var r = DueCalculator.Evaluate(template, baseline, asset.OdometroAtual, today);
            result.Add(new TemplateStatusDto(template.Id, ToLabel(r.Status), r.KmRemaining, r.DateDue));
        }
        return result;
    }

    /// <summary>"vencido" se algum vencido, senão "vence_em_breve" se algum, senão "ok"; com contagens por status.</summary>
    internal static AssetStatusDto Aggregate(IReadOnlyList<TemplateStatusDto> statuses)
    {
        var overdue = statuses.Count(s => s.Status == LabelOverdue);
        var dueSoon = statuses.Count(s => s.Status == LabelDueSoon);
        var agregado = overdue > 0 ? LabelOverdue : dueSoon > 0 ? LabelDueSoon : LabelOk;
        return new AssetStatusDto(agregado, overdue, dueSoon, statuses.Count - overdue - dueSoon);
    }

    private static string ToLabel(DueStatus status) => status switch
    {
        DueStatus.Overdue => LabelOverdue,
        DueStatus.DueSoon => LabelDueSoon,
        _ => LabelOk
    };
}
