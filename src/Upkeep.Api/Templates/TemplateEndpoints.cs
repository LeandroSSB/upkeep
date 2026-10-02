using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Upkeep;
using Upkeep.Api.Auth;
using Upkeep.Api.Common;
using Upkeep.Infrastructure;

namespace Upkeep.Api.Templates;

public sealed record CreateTemplateRequest(
    string Titulo,
    string? Categoria,
    int? IntervaloKm,
    int? IntervaloMeses,
    decimal? CustoEstimado,
    int? BaselineOdometro,
    DateOnly? BaselineData) : ITemplateRequest;

public sealed record UpdateTemplateRequest(
    string Titulo,
    string? Categoria,
    int? IntervaloKm,
    int? IntervaloMeses,
    decimal? CustoEstimado,
    int? BaselineOdometro,
    DateOnly? BaselineData) : ITemplateRequest;

/// <summary>Resposta de template. status/kmRemaining/dateDue entram na Task 10 — por ora sempre null (chaves presentes).</summary>
public sealed record TemplateResponse(
    Guid Id,
    Guid AssetId,
    string Titulo,
    string? Categoria,
    int? IntervaloKm,
    int? IntervaloMeses,
    decimal? CustoEstimado,
    int? BaselineOdometro,
    DateOnly BaselineData,
    object? Status,
    object? KmRemaining,
    object? DateDue)
{
    public static TemplateResponse From(MaintenanceTemplate t) =>
        new(t.Id, t.AssetId, t.Titulo, t.Categoria, t.IntervaloKm, t.IntervaloMeses,
            t.CustoEstimado, t.BaselineOdometro, t.BaselineData,
            Status: null, KmRemaining: null, DateDue: null);
}

public static class TemplateEndpoints
{
    public static IEndpointRouteBuilder MapTemplateEndpoints(this IEndpointRouteBuilder app)
    {
        var nested = app.MapGroup("/assets/{assetId}/templates").WithTags("Templates").RequireAuthorization();
        var direct = app.MapGroup("/templates").WithTags("Templates").RequireAuthorization();

        nested.MapGet("", async (Guid assetId, UpkeepDbContext db, ClaimsPrincipal user) =>
        {
            var userId = user.GetUserId();
            if (!await db.Assets.AnyAsync(a => a.Id == assetId && a.UserId == userId))
                return Results.NotFound(); // asset alheio não vaza — 404, não lista vazia

            var templates = await db.Templates.Where(t => t.AssetId == assetId).ToListAsync();
            return Results.Ok(templates.Select(TemplateResponse.From));
        });

        nested.MapPost("", async (Guid assetId, CreateTemplateRequest req, UpkeepDbContext db, ClaimsPrincipal user) =>
        {
            var userId = user.GetUserId();
            if (!await db.Assets.AnyAsync(a => a.Id == assetId && a.UserId == userId))
                return Results.NotFound();

            var template = new MaintenanceTemplate
            {
                Id = Guid.NewGuid(),
                AssetId = assetId,
                Titulo = req.Titulo.Trim(),
                Categoria = req.Categoria,
                IntervaloKm = req.IntervaloKm,
                IntervaloMeses = req.IntervaloMeses,
                CustoEstimado = req.CustoEstimado,
                BaselineOdometro = req.BaselineOdometro,
                BaselineData = req.BaselineData ?? DateOnly.FromDateTime(DateTime.UtcNow)
            };
            db.Templates.Add(template);
            await db.SaveChangesAsync();
            return Results.Created($"/assets/{assetId}/templates/{template.Id}", TemplateResponse.From(template));
        }).AddEndpointFilter<ValidationFilter<CreateTemplateRequest>>();

        direct.MapPut("/{id}", async (Guid id, UpdateTemplateRequest req, UpkeepDbContext db, ClaimsPrincipal user) =>
        {
            // join implícito via nav Template.Asset (mapping da Task 3) — não vaza template alheio
            var template = await db.Templates
                .FirstOrDefaultAsync(t => t.Id == id && t.Asset!.UserId == user.GetUserId());
            if (template is null) return Results.NotFound();

            template.Titulo = req.Titulo.Trim();
            template.Categoria = req.Categoria;
            template.IntervaloKm = req.IntervaloKm;
            template.IntervaloMeses = req.IntervaloMeses;
            template.CustoEstimado = req.CustoEstimado;
            template.BaselineOdometro = req.BaselineOdometro;
            template.BaselineData = req.BaselineData ?? DateOnly.FromDateTime(DateTime.UtcNow);
            await db.SaveChangesAsync();
            return Results.Ok(TemplateResponse.From(template));
        }).AddEndpointFilter<ValidationFilter<UpdateTemplateRequest>>();

        direct.MapDelete("/{id}", async (Guid id, UpkeepDbContext db, ClaimsPrincipal user) =>
        {
            var template = await db.Templates
                .FirstOrDefaultAsync(t => t.Id == id && t.Asset!.UserId == user.GetUserId());
            if (template is null) return Results.NotFound();

            db.Templates.Remove(template);
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException) // FK Restrict no banco: há service_records vinculados
            {
                return Results.Conflict(new { title = "Template possui serviços vinculados" });
            }
            return Results.NoContent();
        });

        return app;
    }
}
