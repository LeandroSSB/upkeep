using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Testcontainers.PostgreSql;
using Upkeep.Infrastructure;
using Xunit;

namespace Upkeep.IntegrationTests;

public sealed class ApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:18-alpine")
        .Build();

    /// <summary>
    /// POSTs ntfy capturados pelo handler abaixo — (tópico do JSON, JSON crú).
    /// Tests limpam no início (Clear) e a suíte nunca sai pra rede real.
    /// </summary>
    public List<(string Topic, string Json)> CapturedNtfy { get; } = [];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        // Overrides must go via UseSetting (host configuration): WebApplicationFactory's
        // DeferredHostBuilder passes host settings to the minimal-API entry point as
        // command-line args, so they are visible to builder.Configuration in Program.cs.
        // ConfigureAppConfiguration is replayed only at Build time — too late for the
        // eager GetConnectionString("Upkeep") read.
        builder.UseSetting("ConnectionStrings:Upkeep", _db.GetConnectionString());
        builder.UseSetting("Jwt:Key", "chave-de-teste-bem-longa-com-256-bits-minimo!!");
        builder.UseSetting("Jwt:Issuer", "upkeep-tests");
        builder.UseSetting("Jwt:Audience", "upkeep-tests");
        builder.UseSetting("Jwt:AccessMinutes", "15");
        builder.UseSetting("Jwt:RefreshDays", "7");
        // Suíte compartilha um host: dezenas de register/login no /auth esbarrariam no
        // limite padrão (10/min) e virariam 429 espúrios — limite alto nos testes.
        builder.UseSetting("RateLimit:PermitLimit", "1000");
        builder.UseSetting("webroot", "");
        // Ntfy:Enabled fica false (default) — o worker diário NÃO é registrado nos tests.
        // Client "ntfy" troca o handler por um capturador: DueReminderService roda
        // de verdade, mas o POST nunca sai da memória do teste.
        builder.ConfigureServices(s => s.Configure<HttpClientFactoryOptions>("ntfy", o =>
            o.HttpMessageHandlerBuilderActions.Add(b =>
                b.PrimaryHandler = new CapturingNtfyHandler(CapturedNtfy))));
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

/// <summary>
/// Handler do client "ntfy": grava (topic, JSON crú) na lista do fixture e responde 200
/// vazio. Lê o corpo ANTES de responder (o conteúdo só é bufferizado uma vez).
/// </summary>
internal sealed class CapturingNtfyHandler(List<(string Topic, string Json)> sink) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var json = await request.Content!.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        sink.Add((doc.RootElement.GetProperty("topic").GetString() ?? "", json));
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
    }
}
