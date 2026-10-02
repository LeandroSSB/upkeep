using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Upkeep.Api.Notifications;
using Xunit;

namespace Upkeep.IntegrationTests;

/// <summary>
/// ntfy v1.1: varredura multi-usuário do DueReminderService — 1 POST por usuário com
/// tópico E pendências; sem tópico ou sem pendências não notifica. O HTTP sai pelo
/// CapturingNtfyHandler da fixture (sem rede). Como a varredura olha TODOS os usuários
/// com tópico e a suíte compartilha um banco, cada teste limpa seu tópico no finally.
/// </summary>
[Collection("ApiTests")]
public class ReminderTests(ApiFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// User (+ tópico opcional) com asset veiculo e template só-km (intervalo 10.000,
    /// baseline 50.000): overdue → odômetro 60.000 (kmRemaining 0); duesoon → 58.500
    /// (faltam 1.500 ≤ 20% do intervalo).
    /// </summary>
    private static async Task<(HttpClient Client, Guid UserId)> CreateUserWithDueTemplateAsync(
        ApiFixture f, string? topic, bool overdue = true)
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await f.CreateAuthenticatedClientAsync();
        if (topic is not null)
        {
            var put = await client.PutAsJsonAsync("/me/ntfy-topic", new { ntfyTopic = topic }, ct);
            put.EnsureSuccessStatusCode();
        }

        var assetId = (await (await client.PostAsJsonAsync("/assets",
                new
                {
                    nome = "Asset " + Guid.NewGuid().ToString("N")[..8],
                    tipo = "veiculo",
                    odometroAtual = overdue ? 60_000 : 58_500
                }, ct))
            .Content.ReadFromJsonAsync<AssetResponse>(Json, ct))!.Id;
        var template = await client.PostAsJsonAsync($"/assets/{assetId}/templates",
            new { titulo = "Troca de óleo", intervaloKm = 10_000, baselineOdometro = 50_000 }, ct);
        template.EnsureSuccessStatusCode();
        return (client, userId);
    }

    private static Task ClearTopicAsync(HttpClient client) =>
        client.PutAsJsonAsync("/me/ntfy-topic", new { ntfyTopic = (string?)null },
            TestContext.Current.CancellationToken);

    private static async Task<int> SendDueRemindersAsync(ApiFixture f, CancellationToken ct)
    {
        using var scope = f.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<INotificationService>();
        return await svc.SendDueRemindersAsync(ct);
    }

    [Fact]
    public async Task Usuario_com_topico_e_template_vencido_recebe_um_push()
    {
        var ct = TestContext.Current.CancellationToken;
        fixture.CapturedNtfy.Clear();
        var (client, _) = await CreateUserWithDueTemplateAsync(fixture, "rt1-vencido");
        try
        {
            var n = await SendDueRemindersAsync(fixture, ct);

            Assert.Equal(1, n);
            var entry = Assert.Single(fixture.CapturedNtfy);
            Assert.Equal("rt1-vencido", entry.Topic);
            Assert.Contains("vencido", entry.Json);
            Assert.Contains("rt1-vencido", entry.Json);
        }
        finally
        {
            await ClearTopicAsync(client);
        }
    }

    [Fact]
    public async Task Usuario_sem_topico_com_vencido_nao_recebe()
    {
        var ct = TestContext.Current.CancellationToken;
        fixture.CapturedNtfy.Clear();
        await CreateUserWithDueTemplateAsync(fixture, topic: null);

        var n = await SendDueRemindersAsync(fixture, ct);

        Assert.Equal(0, n);
        Assert.Empty(fixture.CapturedNtfy);
    }

    [Fact]
    public async Task Dois_usuarios_vencido_e_duesoon_geram_dois_pushes_com_prioridades()
    {
        var ct = TestContext.Current.CancellationToken;
        fixture.CapturedNtfy.Clear();
        var (clientA, _) = await CreateUserWithDueTemplateAsync(fixture, "rt3-vencido", overdue: true);
        var (clientB, _) = await CreateUserWithDueTemplateAsync(fixture, "rt3-breve", overdue: false);
        try
        {
            var n = await SendDueRemindersAsync(fixture, ct);

            Assert.Equal(2, n);
            Assert.Equal(2, fixture.CapturedNtfy.Count);
            var vencido = fixture.CapturedNtfy.Single(e => e.Topic == "rt3-vencido");
            Assert.Contains("\"priority\":4", vencido.Json);
            Assert.Contains("vencido", vencido.Json);
            var breve = fixture.CapturedNtfy.Single(e => e.Topic == "rt3-breve");
            Assert.Contains("\"priority\":3", breve.Json);
            Assert.Contains("vence_em_breve", breve.Json);
        }
        finally
        {
            await ClearTopicAsync(clientA);
            await ClearTopicAsync(clientB);
        }
    }

    [Fact]
    public async Task Usuario_com_topico_mas_template_ok_nao_recebe()
    {
        var ct = TestContext.Current.CancellationToken;
        fixture.CapturedNtfy.Clear();
        var (client, _) = await fixture.CreateAuthenticatedClientAsync();
        var set = await client.PutAsJsonAsync("/me/ntfy-topic", new { ntfyTopic = "rt4-ok" }, ct);
        set.EnsureSuccessStatusCode();
        try
        {
            // 52.000: faltam 8.000 km (> 20% de 10.000) → ok, nada a notificar
            var assetId = (await (await client.PostAsJsonAsync("/assets",
                    new
                    {
                        nome = "Asset " + Guid.NewGuid().ToString("N")[..8],
                        tipo = "veiculo",
                        odometroAtual = 52_000
                    }, ct))
                .Content.ReadFromJsonAsync<AssetResponse>(Json, ct))!.Id;
            var template = await client.PostAsJsonAsync($"/assets/{assetId}/templates",
                new { titulo = "Troca de óleo", intervaloKm = 10_000, baselineOdometro = 50_000 }, ct);
            template.EnsureSuccessStatusCode();

            var n = await SendDueRemindersAsync(fixture, ct);

            Assert.Equal(0, n);
            Assert.Empty(fixture.CapturedNtfy);
        }
        finally
        {
            await ClearTopicAsync(client);
        }
    }
}
