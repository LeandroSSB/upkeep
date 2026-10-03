using System.Globalization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Upkeep;
using Upkeep.Api.Auth;
using Upkeep.Infrastructure;

namespace Upkeep.Api.Reports;

public sealed record CostByAssetResponse(Guid AssetId, string Nome, decimal Total, int Quantidade);

/// <summary>Mês "yyyy-MM" (régua cronológica, zeros incluídos) — só com groupBy=month.</summary>
public sealed record CostByMonthResponse(string Mes, decimal Total, int Quantidade);

/// <summary>
/// Custo por km de um VEÍCULO (Task M8-2) — só quando assetId é um veículo do
/// usuário. "Km rodados" = odometroAtual − primeiro odômetro CONHECIDO, onde o
/// primeiro conhecido é o mínimo entre baselines de template e odômetros de
/// serviço (valores &gt; 0). A base é HISTÓRICA: ignora from/to (o veículo não
/// nasceu no início do filtro) — só o total segue as mesmas regras do relatório.
/// </summary>
public sealed record CustoPorKmResponse(decimal Total, int KmRodados, decimal? PorKm);

public sealed record CostReportResponse(
    decimal Total,
    List<CostByAssetResponse> PorAsset,
    DateOnly? De,
    DateOnly? Ate,
    List<CostByMonthResponse>? PorMes = null,
    CustoPorKmResponse? CustoPorKm = null);

public static class ReportEndpoints
{
    public static IEndpointRouteBuilder MapReportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/reports").WithTags("Reports").RequireAuthorization();

        group.MapGet("/costs", async (
            Guid? assetId, DateOnly? from, DateOnly? to, string? groupBy,
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

            // groupBy=month SOMA a visão mensal (porAsset segue no payload);
            // ausente/valor outro → porMes null (compat com clientes antigos).
            List<CostByMonthResponse>? porMes = null;
            if (groupBy == "month")
            {
                // Agregação server-side por ano/mês de Data — mesmo trap EF10 do
                // porAsset: projeção anônima member-init (record posicional não
                // re-bind pós-GroupBy). Sem OrderBy no SQL: a ordem vem da régua.
                var monthRows = await query
                    .GroupBy(s => new { s.Data.Year, s.Data.Month })
                    .Select(g => new { g.Key.Year, g.Key.Month, Total = g.Sum(x => x.Custo), Qtd = g.Count() })
                    .ToListAsync();

                var byMes = monthRows.ToDictionary(
                    r => $"{r.Year:D4}-{r.Month:D2}", r => (r.Total, r.Qtd));

                // Régua de 12 meses até o mês atual (UTC), clampada à INTERSEÇÃO com
                // o período from/to: from estreito corta o início, to corta o fim —
                // mas nenhum dos dois estica a janela default. Interseção vazia
                // (período todo fora da janela) → régua vazia.
                var hoje = DateTime.UtcNow;
                var fim = new DateOnly(hoje.Year, hoje.Month, 1);
                var inicio = fim.AddMonths(-11);
                if (from.HasValue)
                {
                    var doPeriodo = new DateOnly(from.Value.Year, from.Value.Month, 1);
                    if (doPeriodo > inicio) inicio = doPeriodo;
                }
                if (to.HasValue)
                {
                    var doPeriodo = new DateOnly(to.Value.Year, to.Value.Month, 1);
                    if (doPeriodo < fim) fim = doPeriodo;
                }

                porMes = [];
                for (var m = inicio; m <= fim; m = m.AddMonths(1))
                {
                    var chave = m.ToString("yyyy-MM", CultureInfo.InvariantCulture);
                    var (total, qtd) = byMes.GetValueOrDefault(chave); // mês sem dado → 0
                    porMes.Add(new CostByMonthResponse(chave, total, qtd));
                }
            }

            var totalFiltrado = porAsset.Sum(r => r.Total);

            // custoPorKm (Task M8-2): apenas quando assetId aponta um VEÍCULO do
            // próprio usuário (asset alheio é invisível → null, igual ao restante
            // da resposta). Definição de "km rodados": odometroAtual − primeiro
            // odômetro CONHECIDO (min entre baselines de template e odômetros de
            // serviço, valores > 0). A base é HISTÓRICA e ignora from/to — o
            // veículo não nasceu no início do filtro; só o TOTAL segue os filtros
            // (mesma agregação da query acima). Sem primeiro conhecido (ou atual
            // null) → kmRodados 0; kmRodados <= 0 → porKm null (não dividiu).
            CustoPorKmResponse? custoPorKm = null;
            if (assetId.HasValue)
            {
                var asset = await db.GetOwnedAssetAsync(assetId.Value, userId);
                if (asset is { Tipo: AssetTipo.Veiculo })
                {
                    // MinAsync com seletor nullable: agregado SQL MIN — vazio → null
                    var minBaseline = await db.Templates
                        .Where(t => t.AssetId == asset.Id && t.BaselineOdometro > 0)
                        .MinAsync(t => t.BaselineOdometro);
                    var minServico = await db.Services
                        .Where(s => s.AssetId == asset.Id && s.Odometro > 0)
                        .MinAsync(s => s.Odometro);

                    var conhecidos = new[] { minBaseline, minServico }
                        .Where(v => v.HasValue).Select(v => v!.Value).ToList();
                    var kmRodados = asset.OdometroAtual.HasValue && conhecidos.Count > 0
                        ? asset.OdometroAtual.Value - conhecidos.Min()
                        : 0;

                    custoPorKm = new CustoPorKmResponse(
                        totalFiltrado,
                        kmRodados,
                        kmRodados > 0 ? Math.Round(totalFiltrado / kmRodados, 3) : null);
                }
            }

            return Results.Ok(new CostReportResponse(totalFiltrado, porAsset, from, to, porMes, custoPorKm));
        });

        return app;
    }
}
