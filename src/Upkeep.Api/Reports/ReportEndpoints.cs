using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Upkeep.Api.Auth;
using Upkeep.Infrastructure;

namespace Upkeep.Api.Reports;

public sealed record CostByAssetResponse(Guid AssetId, string Nome, decimal Total, int Quantidade);

public sealed record CostReportResponse(
    decimal Total,
    List<CostByAssetResponse> PorAsset,
    DateOnly? De,
    DateOnly? Ate);

public static class ReportEndpoints
{
    public static IEndpointRouteBuilder MapReportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/reports").WithTags("Reports").RequireAuthorization();

        group.MapGet("/costs", async (
            Guid? assetId, DateOnly? from, DateOnly? to,
            UpkeepDbContext db, ClaimsPrincipal user) =>
        {
            var userId = user.GetUserId();

            // escopo obrigatório: sempre os assets do próprio usuário;
            // assetId alheio simplesmente não casa → lista vazia (é filtro, não 404)
            var query = db.Services.Where(s => s.Asset!.UserId == userId);
            if (assetId.HasValue) query = query.Where(s => s.AssetId == assetId.Value);
            if (from.HasValue) query = query.Where(s => s.Data >= from.Value); // bounds inclusivos
            if (to.HasValue) query = query.Where(s => s.Data <= to.Value);

            // GroupBy+Sum+Count+OrderBy traduzidos p/ SQL: agregação roda no banco,
            // materializa apenas uma linha por asset. Atenção: o OrderBy deve recair
            // sobre a projeção anônima (member-init) — ordenar por membro de um record
            // projetado via construtor posicional não re-bind ao agregado e o EF
            // rejeita a tradução (IQueryable vira in-translate-able → 500).
            var rows = await query
                .GroupBy(s => new { s.AssetId, s.Asset!.Nome })
                .Select(g => new { g.Key.AssetId, g.Key.Nome, Total = g.Sum(x => x.Custo), Qtd = g.Count() })
                .OrderByDescending(r => r.Total)
                .ToListAsync();

            var porAsset = rows
                .Select(r => new CostByAssetResponse(r.AssetId, r.Nome, r.Total, r.Qtd))
                .ToList();

            return Results.Ok(new CostReportResponse(porAsset.Sum(r => r.Total), porAsset, from, to));
        });

        return app;
    }
}
