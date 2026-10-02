using System.Globalization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Upkeep;
using Upkeep.Api.Assets;
using Upkeep.Api.Auth;
using Upkeep.Infrastructure;

namespace Upkeep.Api.Export;

// DTOs dedicados do export — entidades EF nunca vazam direto (serialização
// camelCase padrão da API). O arquivo é o "backup" do usuário: campos estáveis,
// sem nada de status calculado (isso é view, não dado).

public sealed record ExportUserInfo(Guid Id, string Email);

public sealed record ExportTemplate(
    Guid Id,
    Guid AssetId,
    string Titulo,
    string? Categoria,
    int? IntervaloKm,
    int? IntervaloMeses,
    decimal? CustoEstimado,
    int? BaselineOdometro,
    DateOnly BaselineData);

public sealed record ExportService(
    Guid Id,
    Guid AssetId,
    Guid? TemplateId,
    DateOnly Data,
    int? Odometro,
    decimal Custo,
    string? Notas,
    DateTime CreatedAt);

public sealed record ExportAsset(
    Guid Id,
    string Nome,
    string Tipo,
    int? OdometroAtual,
    string? Notas,
    DateTime CreatedAt,
    List<ExportTemplate> Templates,
    List<ExportService> Services);

public sealed record ExportResponse(
    DateTime ExportadoEm,
    ExportUserInfo User,
    List<ExportAsset> Assets);

public static class ExportEndpoints
{
    public static IEndpointRouteBuilder MapExportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/export").WithTags("Export").RequireAuthorization();

        group.MapGet("", async (
            HttpContext http, UpkeepDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var userId = user.GetUserId();
            // e-mail vem do claim (emitido no token); DB é só fallback defensivo
            var email = user.FindFirstValue(ClaimTypes.Email) ?? user.FindFirstValue("email")
                ?? await db.Users.Where(u => u.Id == userId).Select(u => u.Email).SingleAsync(ct);

            // 3 queries batch: assets do usuário → templates → serviços por assetIds;
            // o grafo aninhado (asset → templates/services) monta em memória, sem Include/N+1.
            var assets = await db.Assets
                .Where(a => a.UserId == userId)
                .OrderBy(a => a.CreatedAt).ThenBy(a => a.Nome).ThenBy(a => a.Id)
                .ToListAsync(ct);

            var assetIds = assets.Select(a => a.Id).ToList();
            List<MaintenanceTemplate> templates = [];
            List<ServiceRecord> services = [];
            if (assetIds.Count > 0) // usuário vazio não bateria no banco à toa
            {
                templates = await db.Templates
                    .Where(t => assetIds.Contains(t.AssetId))
                    .OrderBy(t => t.Titulo).ThenBy(t => t.Id)
                    .ToListAsync(ct);
                services = await db.Services
                    .Where(s => assetIds.Contains(s.AssetId))
                    .OrderBy(s => s.Data).ThenBy(s => s.Id)
                    .ToListAsync(ct);
            }

            var templatesByAsset = templates.ToLookup(t => t.AssetId);
            var servicesByAsset = services.ToLookup(s => s.AssetId);

            var export = new ExportResponse(
                DateTime.UtcNow,
                new ExportUserInfo(userId, email),
                assets.Select(a => new ExportAsset(
                    a.Id, a.Nome, a.Tipo.ToApiString(), a.OdometroAtual, a.Notas, a.CreatedAt,
                    templatesByAsset[a.Id].Select(t => new ExportTemplate(
                        t.Id, t.AssetId, t.Titulo, t.Categoria, t.IntervaloKm, t.IntervaloMeses,
                        t.CustoEstimado, t.BaselineOdometro, t.BaselineData)).ToList(),
                    servicesByAsset[a.Id].Select(s => new ExportService(
                        s.Id, s.AssetId, s.TemplateId, s.Data, s.Odometro, s.Custo, s.Notas,
                        s.CreatedAt)).ToList()))
                .ToList());

            // download com nome datado (data de hoje, UTC) — header setado direto na
            // response antes do Results.Ok escrever o corpo
            http.Response.Headers.ContentDisposition =
                $"attachment; filename=\"upkeep-export-{DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.json\"";

            return Results.Ok(export);
        })
            .WithSummary("Exporta todos os dados do usuário: assets com templates e serviços aninhados");

        return app;
    }
}
