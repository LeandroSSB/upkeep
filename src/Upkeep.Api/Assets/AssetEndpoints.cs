using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Upkeep;
using Upkeep.Api.Auth;
using Upkeep.Api.Common;
using Upkeep.Infrastructure;

namespace Upkeep.Api.Assets;

public static class AssetEndpoints
{
    public static IEndpointRouteBuilder MapAssetEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/assets").WithTags("Assets").RequireAuthorization();

        group.MapGet("", async (UpkeepDbContext db, ClaimsPrincipal user) =>
        {
            var userId = user.GetUserId();
            var assets = await db.Assets.Where(a => a.UserId == userId).ToListAsync();
            return Results.Ok(assets.Select(AssetResponse.From));
        });

        group.MapPost("", async (CreateAssetRequest req, UpkeepDbContext db, ClaimsPrincipal user) =>
        {
            if (!AssetTipoApi.TryParse(req.Tipo, out var tipo))
                return Results.ValidationProblem(TipoInvalido);
            if (req.OdometroAtual.HasValue && tipo != AssetTipo.Veiculo)
                return Results.ValidationProblem(OdometroSoVeiculo("odometroAtual"));

            var asset = new Asset
            {
                Id = Guid.NewGuid(),
                UserId = user.GetUserId(),
                Nome = req.Nome.Trim(),
                Tipo = tipo,
                OdometroAtual = req.OdometroAtual,
                Notas = req.Notas,
                CreatedAt = DateTime.UtcNow
            };
            db.Assets.Add(asset);
            await db.SaveChangesAsync();
            return Results.Created($"/assets/{asset.Id}", AssetResponse.From(asset));
        }).AddEndpointFilter<ValidationFilter<CreateAssetRequest>>();

        group.MapPut("/{id}", async (Guid id, UpdateAssetRequest req, UpkeepDbContext db, ClaimsPrincipal user) =>
        {
            var asset = await db.Assets.FirstOrDefaultAsync(a => a.Id == id && a.UserId == user.GetUserId());
            if (asset is null) return Results.NotFound(); // não vaza existência de asset alheio

            asset.Nome = req.Nome.Trim();
            asset.Notas = req.Notas;
            await db.SaveChangesAsync();
            return Results.Ok(AssetResponse.From(asset));
        }).AddEndpointFilter<ValidationFilter<UpdateAssetRequest>>();

        group.MapDelete("/{id}", async (Guid id, UpkeepDbContext db, ClaimsPrincipal user) =>
        {
            var asset = await db.Assets.FirstOrDefaultAsync(a => a.Id == id && a.UserId == user.GetUserId());
            if (asset is null) return Results.NotFound();

            db.Assets.Remove(asset); // cascade em templates/services (by design)
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        group.MapPost("/{id}/odometer", async (Guid id, UpdateOdometerRequest req, UpkeepDbContext db, ClaimsPrincipal user) =>
        {
            var asset = await db.Assets.FirstOrDefaultAsync(a => a.Id == id && a.UserId == user.GetUserId());
            if (asset is null) return Results.NotFound();
            if (asset.Tipo != AssetTipo.Veiculo)
                return Results.ValidationProblem(OdometroSoVeiculo("odometer"));
            if (req.Odometer < (asset.OdometroAtual ?? 0)) // odômetro nunca volta (decisão de plano)
                return Results.ValidationProblem(OdometroNaoVolta);

            asset.OdometroAtual = req.Odometer;
            await db.SaveChangesAsync();
            return Results.Ok(AssetResponse.From(asset));
        }).AddEndpointFilter<ValidationFilter<UpdateOdometerRequest>>();

        return app;
    }

    private static readonly Dictionary<string, string[]> TipoInvalido =
        new() { ["tipo"] = ["Tipo deve ser 'veiculo', 'casa' ou 'aparelho'"] };
    private static readonly Dictionary<string, string[]> OdometroNaoVolta =
        new() { ["odometer"] = ["Odômetro não pode voltar (novo valor menor que o atual)"] };

    private static Dictionary<string, string[]> OdometroSoVeiculo(string campo) =>
        new() { [campo] = ["Odômetro só é permitido para veículos"] };
}
