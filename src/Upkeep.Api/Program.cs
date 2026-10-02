using System.Text;
using System.Threading.RateLimiting;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Scalar.AspNetCore;
using Serilog;
using Upkeep.Api.Assets;
using Upkeep.Api.Auth;
using Upkeep.Api.Me;
using Upkeep.Api.Middleware;
using Upkeep.Api.Notifications;
using Upkeep.Api.Reports;
using Upkeep.Api.Services;
using Upkeep.Api.Templates;
using Upkeep.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration));

builder.Services.AddUpkeepDbContext(
    builder.Configuration.GetConnectionString("Upkeep")
    ?? throw new InvalidOperationException("ConnectionStrings:Upkeep ausente"));

var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>()
    ?? throw new InvalidOperationException("Jwt ausente");
// Falha rápido no boot (não no primeiro login): appsettings vem com Key vazia por padrão —
// sem este guard a API subia e assinava tokens com chave vazia/curta.
if (string.IsNullOrWhiteSpace(jwt.Key) || jwt.Key.Length < 32)
    throw new InvalidOperationException("Jwt:Key ausente/curta demais (mínimo 32 chars; configure a env Jwt__Key)");
builder.Services.AddSingleton(jwt);
builder.Services.AddSingleton<ITokenService, TokenService>();
builder.Services.AddSingleton<IPasswordHasher, UpkeepPasswordHasher>();
builder.Services.AddScoped<IStatusService, StatusService>();
builder.Services.AddValidatorsFromAssemblyContaining<RegisterRequestValidator>();

// ntfy: lembretes diários de manutenções vencidas. O worker diário só sobe com
// Ntfy:Enabled=true (padrão false — tests/dev não varrem nada nem saem pra rede).
builder.Services.Configure<NtfyOptions>(builder.Configuration.GetSection("Ntfy"));
var ntfy = builder.Configuration.GetSection("Ntfy").Get<NtfyOptions>() ?? new NtfyOptions();
builder.Services.AddSingleton(ntfy); // instância bindada p/ DueReminderService
// 30s: durante outage do ntfy.sh cada POST pararia até 100s (default) — varredura
// sequencial por usuário ficaria N×100s; timeout curto + isolamento por usuário.
builder.Services.AddHttpClient("ntfy", c => c.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddScoped<INotificationService, DueReminderService>();
if (ntfy.Enabled)
    builder.Services.AddHostedService<NtfyReminderWorker>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        // MapInboundClaims=false: claims chegam com nomes literais ("sub") — sem isso o handler
        // remapeia para URIs ( ClaimTypes.*) e GetUserId não encontra o claim (achado Task 5).
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new()
        {
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            ValidateIssuerSigningKey = true,
            ValidateIssuer = true,
            ValidateAudience = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "sub",
            RoleClaimType = "role"
        };
    });
builder.Services.AddAuthorization();

// OpenAPI nativo com security scheme Bearer — o "Authorize" do Scalar (/scalar) funciona.
// AddBearerAuthentication() (built-in) só chegou depois do pacote 10.0.12: transformers
// abaixo são o equivalente documentado — scheme no components + requirement nos
// endpoints autenticados (metadata IAuthorizeData).
builder.Services.AddOpenApi(o =>
{
    o.AddDocumentTransformer((doc, ctx, ct) =>
    {
        doc.Components ??= new OpenApiComponents();
        doc.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        doc.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "accessToken retornado por /auth/register ou /auth/login"
        };
        return Task.CompletedTask;
    });
    o.AddOperationTransformer((operation, context, ct) =>
    {
        if (context.Description.ActionDescriptor.EndpointMetadata.OfType<IAuthorizeData>().Any())
        {
            operation.Security ??= new List<OpenApiSecurityRequirement>();
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", context.Document)] = new List<string>()
            });
        }
        return Task.CompletedTask;
    });
});

// Rate limiting /auth: fixed window global por instância. Limite via config para que
// a suíte de integração (mesmo host, dezenas de register/login) não esbarre no 429.
var permitLimit = int.TryParse(builder.Configuration["RateLimit:PermitLimit"], out var configured)
    && configured > 0 ? configured : 10;
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddFixedWindowLimiter("auth", w =>
    {
        w.PermitLimit = permitLimit;
        w.Window = TimeSpan.FromMinutes(1);
        w.QueueLimit = 0;
    });
});

var app = builder.Build();

// Migrations automáticas no boot (deploy Docker: ApplyMigrations=true). Guard
// necessário: a suíte de integração (ApiFixture, env Testing) migra sozinha e
// NÃO seta a flag.
if (app.Configuration.GetValue<bool>("ApplyMigrations"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<UpkeepDbContext>().Database.MigrateAsync();
}

app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// SPA: estáticos do wwwroot (preenchido no build Docker pelo stage node) e
// fallback p/ as rotas do router. Sem wwwroot (tests/dev) é no-op: endpoints da
// API e /health continuam intactos — o fallback só pega paths não-mapeados.
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapOpenApi();
if (!app.Environment.IsProduction())
    app.MapScalarApiReference(); // /scalar — docs interativas fora de produção

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/health/ready", async (UpkeepDbContext db) =>
    await db.Database.CanConnectAsync()
        ? Results.Ok(new { status = "ready" })
        : Results.Problem(statusCode: 503, title: "Database unavailable"));

app.MapAuthEndpoints();
app.MapMeEndpoints();
app.MapAssetEndpoints();
app.MapTemplateEndpoints();
app.MapServiceEndpoints();
app.MapReportEndpoints();

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

app.Run();

public partial class Program { }

/// <summary>
/// Metadata que identifica o endpoint de fallback do SPA: o guard /api acima só
/// devolve 404 quando foi ESTE endpoint que o routing casou (nenhum endpoint real
/// correspondeu ao path).
/// </summary>
internal sealed class SpaFallbackMarker { }
