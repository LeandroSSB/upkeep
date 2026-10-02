using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
        try
        {
            var put = await client.PutAsJsonAsync("/me/ntfy-topic",
                new { ntfyTopic = "upkeep-teste-1" }, ct);
            Assert.Equal(HttpStatusCode.OK, put.StatusCode);
            var body = await put.Content.ReadFromJsonAsync<MeResponse>(ct);
            Assert.Equal("upkeep-teste-1", body!.NtfyTopic);

            // persistiu de verdade: GET reflete o valor salvo
            var me = await client.GetFromJsonAsync<MeResponse>("/me", ct);
            Assert.Equal("upkeep-teste-1", me!.NtfyTopic);
        }
        finally
        {
            // a varredura do ntfy olha TODOS os usuários com tópico: tópico deixado
            // aqui vazaria pendências fictícias nos ReminderTests (padrão ReminderTests)
            await client.PutAsJsonAsync("/me/ntfy-topic",
                new { ntfyTopic = (string?)null }, ct);
        }
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

    // ---- POST /me/ntfy-topic/test (teste instantâneo de push) ----

    [Fact]
    public async Task Test_notification_com_topico_200_e_envia_um_push()
    {
        var ct = TestContext.Current.CancellationToken;
        fixture.CapturedNtfy.Clear();
        var (client, _) = await fixture.CreateAuthenticatedClientAsync("me-push-ok@test.local");
        try
        {
            var set = await client.PutAsJsonAsync("/me/ntfy-topic",
                new { ntfyTopic = "me-push-ok" }, ct);
            set.EnsureSuccessStatusCode();

            var resp = await client.PostAsync("/me/ntfy-topic/test", content: null, ct);

            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var body = await resp.Content.ReadFromJsonAsync<TesteNotificacaoResponse>(ct);
            Assert.True(body!.Enviado);

            // exatamente 1 push, para o tópico SALVO do usuário, com o texto de teste
            var push = Assert.Single(fixture.CapturedNtfy);
            Assert.Equal("me-push-ok", push.Topic);
            using var doc = JsonDocument.Parse(push.Json);
            var root = doc.RootElement;
            Assert.Equal("upkeep: teste ✓", root.GetProperty("title").GetString());
            Assert.Equal("Se você recebeu, os lembretes vão funcionar.",
                root.GetProperty("message").GetString());
            // array de tags INTEIRO — não só [0] (tag extra entraria sem acusar)
            var tags = root.GetProperty("tags").EnumerateArray().Select(t => t.GetString()).ToArray();
            Assert.Equal(new[] { "white_check_mark" }, tags);
            Assert.Equal(3, root.GetProperty("priority").GetInt32());
        }
        finally
        {
            // mesmo padrão dos outros testes: tópico deixado vazaria pushes fictícios
            await client.PutAsJsonAsync("/me/ntfy-topic",
                new { ntfyTopic = (string?)null }, ct);
        }
    }

    [Fact]
    public async Task Test_notification_sem_topico_400()
    {
        var ct = TestContext.Current.CancellationToken;
        fixture.CapturedNtfy.Clear();
        var (client, _) = await fixture.CreateAuthenticatedClientAsync("me-push-sem@test.local");

        var resp = await client.PostAsync("/me/ntfy-topic/test", content: null, ct);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Contains("Configure um tópico", await resp.Content.ReadAsStringAsync(ct));
        Assert.Empty(fixture.CapturedNtfy);
    }

    [Fact]
    public async Task Test_notification_sem_token_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var resp = await fixture.CreateClient().PostAsync("/me/ntfy-topic/test", content: null, ct);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}

public sealed record MeResponse(Guid Id, string Email, string? NtfyTopic);
public sealed record TesteNotificacaoResponse(bool Enviado);
