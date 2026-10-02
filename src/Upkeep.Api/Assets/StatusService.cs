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
}

public sealed record AssetStatusDto(string Status, int Overdue, int DueSoon, int Ok);
public sealed record TemplateStatusDto(Guid TemplateId, string Status, int? KmRemaining, DateOnly? DateDue);

public sealed class StatusService(UpkeepDbContext db) : IStatusService
{
    private const string LabelOk = "ok";
    private const string LabelDueSoon = "vence_em_breve";
    private const string LabelOverdue = "vencido";

    public async Task<AssetStatusDto?> GetAssetStatusAsync(Guid assetId, Guid userId, CancellationToken ct)
    {
        var asset = await db.Assets.FirstOrDefaultAsync(a => a.Id == assetId && a.UserId == userId, ct);
        if (asset is null) return null;
        return Aggregate(await EvaluateAsync(asset, ct));
    }

    public async Task<IReadOnlyList<TemplateStatusDto>> GetTemplateStatusesAsync(
        Guid assetId, Guid userId, CancellationToken ct)
    {
        var asset = await db.Assets.FirstOrDefaultAsync(a => a.Id == assetId && a.UserId == userId, ct);
        if (asset is null) return [];
        return await EvaluateAsync(asset, ct);
    }

    /// <summary>Templates + serviços em 2 queries; último serviço por template reduzido em memória (sem N+1 por template).</summary>
    private async Task<IReadOnlyList<TemplateStatusDto>> EvaluateAsync(Asset asset, CancellationToken ct)
    {
        var templates = await db.Templates.Where(t => t.AssetId == asset.Id).ToListAsync(ct);
        if (templates.Count == 0) return [];

        var templateIds = templates.Select(t => t.Id).ToList();
        var services = await db.Services
            .Where(s => s.TemplateId != null && templateIds.Contains(s.TemplateId!.Value))
            .ToListAsync(ct);

        // último serviço por template: Data desc, Id desc (tiebreak determinístico)
        var latestByTemplate = services
            .GroupBy(s => s.TemplateId!.Value)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(s => s.Data).ThenByDescending(s => s.Id).First());

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var result = new List<TemplateStatusDto>(templates.Count);
        foreach (var template in templates)
        {
            var service = latestByTemplate.GetValueOrDefault(template.Id);
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
