using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Upkeep;
using Upkeep.Api.Auth;
using Upkeep.Api.Common;
using Upkeep.Infrastructure;

namespace Upkeep.Api.Services;

public sealed record CreateServiceRequest(
    Guid? TemplateId,
    DateOnly Data,
    int? Odometro,
    decimal Custo,
    string? Notas);

public sealed record ServiceResponse(
    Guid Id,
    Guid AssetId,
    Guid? TemplateId,
    DateOnly Data,
    int? Odometro,
    decimal Custo,
    string? Notas,
    DateTime CreatedAt)
{
    public static ServiceResponse From(ServiceRecord s) =>
        new(s.Id, s.AssetId, s.TemplateId, s.Data, s.Odometro, s.Custo, s.Notas, s.CreatedAt);
}

public static class ServiceEndpoints
{
    public static IEndpointRouteBuilder MapServiceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/assets/{assetId}/services").WithTags("Services").RequireAuthorization();

        group.MapGet("", async (Guid assetId, UpkeepDbContext db, ClaimsPrincipal user) =>
        {
            var userId = user.GetUserId();
            if (await db.GetOwnedAssetAsync(assetId, userId) is null)
                return Results.NotFound(); // asset alheio não vaza — 404, não lista vazia

            var services = await db.Services
                .Where(s => s.AssetId == assetId)
                .OrderByDescending(s => s.Data)
                .ThenByDescending(s => s.CreatedAt) // desempate determinístico
                .ToListAsync();
            return Results.Ok(services.Select(ServiceResponse.From));
        });

        group.MapPost("", async (Guid assetId, CreateServiceRequest req, UpkeepDbContext db, ClaimsPrincipal user) =>
        {
            var asset = await db.GetOwnedAssetAsync(assetId, user.GetUserId());
            if (asset is null) return Results.NotFound();

            // template informado: deve existir E pertencer ao MESMO asset (asset é o recurso
            // endereçado e existe → 400 Problem, não 404)
            if (req.TemplateId.HasValue &&
                !await db.Templates.AnyAsync(t => t.Id == req.TemplateId.Value && t.AssetId == assetId))
                return Results.Problem(statusCode: 400, title: "Template não pertence ao asset");

            if (req.Odometro.HasValue && asset.Tipo != AssetTipo.Veiculo)
                return Results.ValidationProblem(OdometroSoVeiculo);

            var service = new ServiceRecord
            {
                Id = Guid.NewGuid(),
                AssetId = assetId,
                TemplateId = req.TemplateId,
                Data = req.Data,
                Odometro = req.Odometro,
                Custo = req.Custo,
                Notas = req.Notas,
                CreatedAt = DateTime.UtcNow
            };
            db.Services.Add(service);

            // auto-advance: só para frente, na MESMA transação do insert
            if (req.Odometro.HasValue && req.Odometro.Value > (asset.OdometroAtual ?? 0))
                asset.OdometroAtual = req.Odometro.Value;

            await db.SaveChangesAsync();
            // 201 sem Location: coleção só tem GET lista — não há rota de serviço único
            return Results.Json(ServiceResponse.From(service),
                statusCode: StatusCodes.Status201Created);
        }).AddEndpointFilter<ValidationFilter<CreateServiceRequest>>();

        return app;
    }

    private static readonly Dictionary<string, string[]> OdometroSoVeiculo =
        new() { ["odometro"] = ["Odômetro só é permitido para veículos"] };
}
