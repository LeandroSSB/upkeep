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

            // GroupBy+Sum+Count traduzidos p/ SQL: agregação roda no banco,
            // materializa apenas uma linha por asset
            var porAsset = await query
                .GroupBy(s => new { s.AssetId, s.Asset!.Nome })
                .Select(g => new CostByAssetResponse(
                    g.Key.AssetId, g.Key.Nome, g.Sum(x => x.Custo), g.Count()))
                .OrderByDescending(r => r.Total)
                .ToListAsync();

            return Results.Ok(new CostReportResponse(porAsset.Sum(r => r.Total), porAsset, from, to));
        });

        return app;
    }
}
