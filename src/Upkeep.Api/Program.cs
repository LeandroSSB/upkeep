using Serilog;
using Upkeep.Api.Middleware;
using Upkeep.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration));

builder.Services.AddUpkeepDbContext(
    builder.Configuration.GetConnectionString("Upkeep")
    ?? throw new InvalidOperationException("ConnectionStrings:Upkeep ausente"));

var app = builder.Build();

app.UseMiddleware<GlobalExceptionMiddleware>();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/health/ready", async (UpkeepDbContext db) =>
    await db.Database.CanConnectAsync()
        ? Results.Ok(new { status = "ready" })
        : Results.Problem(statusCode: 503, title: "Database unavailable"));

app.Run();

public partial class Program { }
