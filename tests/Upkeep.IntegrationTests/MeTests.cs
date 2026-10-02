using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Upkeep.IntegrationTests;

[Collection("ApiTests")]
public class MeTests(ApiFixture fixture)
{
    [Fact]
    public async Task Get_me_retorna_usuario_atual()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await fixture.CreateAuthenticatedClientAsync("me-get@test.local");

        var resp = await client.GetFromJsonAsync<MeResponse>("/me", ct);

        Assert.NotNull(resp);
        Assert.Equal(userId, resp!.Id);
        Assert.Equal("me-get@test.local", resp.Email);
        Assert.Null(resp.NtfyTopic);
    }

    [Fact]
    public async Task Set_ntfy_topic_persiste()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync("me-set@test.local");

        var put = await client.PutAsJsonAsync("/me/ntfy-topic",
            new { ntfyTopic = "upkeep-teste-1" }, ct);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var body = await put.Content.ReadFromJsonAsync<MeResponse>(ct);
        Assert.Equal("upkeep-teste-1", body!.NtfyTopic);

        // persistiu de verdade: GET reflete o valor salvo
        var me = await client.GetFromJsonAsync<MeResponse>("/me", ct);
        Assert.Equal("upkeep-teste-1", me!.NtfyTopic);
    }

    [Fact]
    public async Task Limpar_ntfy_topic_com_null()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync("me-clear@test.local");

        var set = await client.PutAsJsonAsync("/me/ntfy-topic",
            new { ntfyTopic = "topico-para-limpar" }, ct);
        set.EnsureSuccessStatusCode();

        var put = await client.PutAsJsonAsync("/me/ntfy-topic",
            new { ntfyTopic = (string?)null }, ct);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var body = await put.Content.ReadFromJsonAsync<MeResponse>(ct);
        Assert.Null(body!.NtfyTopic);

        var me = await client.GetFromJsonAsync<MeResponse>("/me", ct);
        Assert.Null(me!.NtfyTopic);
    }

    [Fact]
    public async Task Topico_invalido_400()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync("me-invalid@test.local");

        foreach (var invalido in new[] { "tem espaço", "tópico#com#símbolo", new string('a', 65) })
        {
            var resp = await client.PutAsJsonAsync("/me/ntfy-topic",
                new { ntfyTopic = invalido }, ct);
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            Assert.Contains("errors", await resp.Content.ReadAsStringAsync(ct));
        }
    }

    [Fact]
    public async Task Me_sem_token_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var resp = await fixture.CreateClient().GetAsync("/me", ct);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}

public sealed record MeResponse(Guid Id, string Email, string? NtfyTopic);
