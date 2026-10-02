using Microsoft.AspNetCore.StaticFiles;
using Scalar.AspNetCore;
using Upkeep.Api.Assets;
using Upkeep.Api.Auth;
using Upkeep.Api.Export;
using Upkeep.Api.Me;
using Upkeep.Api.Middleware;
using Upkeep.Api.Reports;
using Upkeep.Api.Services;
using Upkeep.Api.Templates;
using Upkeep.Infrastructure;

namespace Upkeep.Api.Extensions;

public static class WebApplicationExtensions
{
    /// <summary>
    /// Pipeline HTTP completo (middleware na ordem original + health + endpoints + SPA
    /// fallback). Comportamento idêntico ao Program.cs monolítico original.
    /// </summary>
    public static WebApplication MapApiEndpoints(this WebApplication app)
    {
        app.UseMiddleware<GlobalExceptionMiddleware>();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();

        // SPA: estáticos do wwwroot (preenchido no build Docker pelo stage node) e
        // fallback p/ as rotas do router. Sem wwwroot (tests/dev) é no-op: endpoints da
        // API e /health continuam intactos — o fallback só pega paths não-mapeados.
        // .webmanifest não está no mapa padrão de content types (viraria
        // application/octet-stream e o Chrome recusaria o manifest do PWA).
        var contentTypes = new FileExtensionContentTypeProvider();
        contentTypes.Mappings[".webmanifest"] = "application/manifest+json";
        app.UseDefaultFiles();
        app.UseStaticFiles(new StaticFileOptions { ContentTypeProvider = contentTypes });

        app.MapOpenApi();
        if (!app.Environment.IsProduction())
            app.MapScalarApiReference(); // /scalar — docs interativas fora de produção

        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
        app.MapGet("/health/ready", async (UpkeepDbContext db, HttpContext context) =>
        {
            // CanConnect devolve false em falha "limpa", mas LANÇA em outras (ex.: provider
            // fora do ar) — sem o catch o middleware viraria 500; health check espera 503.
            // CT do request: cliente desiste → não segura o slot do pool de conexões.
            bool ok;
            try { ok = await db.Database.CanConnectAsync(context.RequestAborted); }
            catch { ok = false; }
            return ok
                ? Results.Ok(new { status = "ready" })
                : Results.Problem(statusCode: 503, title: "Database unavailable");
        });

        app.MapAuthEndpoints();
        app.MapMeEndpoints();
        app.MapAssetEndpoints();
        app.MapTemplateEndpoints();
        app.MapServiceEndpoints();
        app.MapReportEndpoints();
        app.MapExportEndpoints();

        // /api/* desconhecido deve ser 404 JSON, nunca o fallback do SPA. O request pode
        // chegar aqui por dois caminhos:
        // - direto no container: path COM o prefixo (/api/...);
        // - produção via nginx: o location /api/ faz o STRIP do prefixo (proxy_pass com
        //   barra), mas o nginx preserva o URI público em X-Original-URI (header zerado
        //   no location / — cliente não consegue se auto-marcar).
        // O 404 só se aplica quando o routing NÃO casou endpoint real — ou seja, o que casou
        // foi o fallback do SPA (marcado abaixo). Endpoint válido segue o pipeline normal:
        // o MapWhen roda depois do UseRouting implícito, então GetEndpoint() já está populado.
        app.MapWhen(
            c => (c.Request.Path.StartsWithSegments("/api")
                    || c.Request.Headers["X-Original-URI"].ToString().StartsWith("/api/"))
                && c.GetEndpoint()?.Metadata.OfType<SpaFallbackMarker>().Any() == true,
            b => b.Run(async ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync("""{"title":"Not Found","status":404}""");
            }));

        // Fallback SPA por último: index.html p/ rotas não-mapeadas ({*path:nonfile}
        // ignora paths com extensão — assets inexistentes seguem 404, não HTML).
        var spaFallback = app.MapFallbackToFile("index.html");
        spaFallback.Add(b => b.Metadata.Add(new SpaFallbackMarker()));

        return app;
    }
}

/// <summary>
/// Metadata que identifica o endpoint de fallback do SPA: o guard /api acima só
/// devolve 404 quando foi ESTE endpoint que o routing casou (nenhum endpoint real
/// correspondeu ao path).
/// </summary>
internal sealed class SpaFallbackMarker { }
