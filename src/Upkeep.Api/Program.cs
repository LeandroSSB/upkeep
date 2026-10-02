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
using Upkeep.Api.Middleware;
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
builder.Services.AddSingleton(jwt);
builder.Services.AddSingleton<ITokenService, TokenService>();
builder.Services.AddSingleton<IPasswordHasher, UpkeepPasswordHasher>();
builder.Services.AddScoped<IStatusService, StatusService>();
builder.Services.AddValidatorsFromAssemblyContaining<RegisterRequestValidator>();

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

app.MapOpenApi();
if (!app.Environment.IsProduction())
    app.MapScalarApiReference(); // /scalar — docs interativas fora de produção

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/health/ready", async (UpkeepDbContext db) =>
    await db.Database.CanConnectAsync()
        ? Results.Ok(new { status = "ready" })
        : Results.Problem(statusCode: 503, title: "Database unavailable"));

app.MapAuthEndpoints();
app.MapAssetEndpoints();
app.MapTemplateEndpoints();
app.MapServiceEndpoints();
app.MapReportEndpoints();

app.Run();

public partial class Program { }
