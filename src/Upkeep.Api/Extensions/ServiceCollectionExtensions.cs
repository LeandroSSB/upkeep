using System.Text;
using System.Threading.RateLimiting;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Serilog;
using Upkeep.Api.Assets;
using Upkeep.Api.Auth;
using Upkeep.Api.Notifications;
using Upkeep.Infrastructure;

namespace Upkeep.Api.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Todo o registro de serviços (serilog, db, jwt, auth, ntfy, openapi, rate limit) —
    /// o pipeline HTTP (middleware/rotas) fica no WebApplicationExtensions. Comportamento
    /// idêntico ao Program.cs monolítico original.
    /// </summary>
    public static WebApplicationBuilder AddApiServices(this WebApplicationBuilder builder)
    {
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
        // Binding ÚNICA via IOptions<NtfyOptions> — DueReminderService e o worker leem a
        // mesma configuração (sem singleton duplicado divergindo da seção bindada).
        builder.Services.Configure<NtfyOptions>(builder.Configuration.GetSection("Ntfy"));
        // 30s: durante outage do ntfy.sh cada POST pararia até 100s (default) — varredura
        // sequencial por usuário ficaria N×100s; timeout curto + isolamento por usuário.
        builder.Services.AddHttpClient("ntfy", c => c.Timeout = TimeSpan.FromSeconds(30));
        builder.Services.AddScoped<INotificationService, DueReminderService>();
        if (builder.Configuration.GetValue<bool>("Ntfy:Enabled"))
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

        return builder;
    }
}
