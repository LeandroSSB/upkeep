using Microsoft.EntityFrameworkCore;
using Upkeep.Api.Extensions;
using Upkeep.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.AddApiServices();

var app = builder.Build();

// Migrations automáticas no boot (deploy Docker: ApplyMigrations=true). Guard
// necessário: a suíte de integração (ApiFixture, env Testing) migra sozinha e
// NÃO seta a flag.
if (app.Configuration.GetValue<bool>("ApplyMigrations"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<UpkeepDbContext>().Database.MigrateAsync();
}

app.MapApiEndpoints();
app.Run();

public partial class Program { }
