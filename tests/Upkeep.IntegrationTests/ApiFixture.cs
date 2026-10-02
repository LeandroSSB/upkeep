using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Upkeep.Infrastructure;
using Xunit;

namespace Upkeep.IntegrationTests;

public sealed class ApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:18-alpine")
        .Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Upkeep"] = _db.GetConnectionString(),
            ["Jwt:Key"] = "chave-de-teste-bem-longa-com-256-bits-minimo!!",
            ["Jwt:Issuer"] = "upkeep-tests",
            ["Jwt:Audience"] = "upkeep-tests",
            ["AccessTokens:Minutes"] = "15",
            ["RefreshTokens:Days"] = "7"
        }));
        builder.UseSetting("webroot", "");
    }

    public async ValueTask InitializeAsync()
    {
        await _db.StartAsync();
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<UpkeepDbContext>().Database.MigrateAsync();
    }

    public new async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _db.DisposeAsync();
    }
}

[CollectionDefinition("ApiTests")]
public sealed class ApiTestCollection : ICollectionFixture<ApiFixture> { }

public static class ApiFixtureExtensions
{
    public static async Task<(HttpClient Client, Guid UserId)> CreateAuthenticatedClientAsync(
        this ApiFixture fixture, string? email = null)
    {
        email ??= $"user-{Guid.NewGuid():N}@test.local";
        var resp = await fixture.CreateClient().PostAsJsonAsync("/auth/register",
            new { email, password = "SenhaForte!123" });
        resp.EnsureSuccessStatusCode();
        var auth = await resp.Content.ReadFromJsonAsync<AuthResponse>();
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", auth!.AccessToken);
        return (client, auth.User.Id);
    }
}

public sealed record AuthResponse(string AccessToken, string RefreshToken, AuthUser User);
public sealed record AuthUser(Guid Id, string Email);
