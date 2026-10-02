using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Upkeep.IntegrationTests;

[Collection("ApiTests")]
public class AuthTests(ApiFixture fixture)
{
    [Fact]
    public async Task Register_retorna_201_com_tokens_e_usuario()
    {
        var resp = await fixture.CreateClient().PostAsJsonAsync("/auth/register",
            new { email = "reg@test.local", password = "SenhaForte!123" });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotEmpty(body!.AccessToken);
        Assert.NotEmpty(body.RefreshToken);
        Assert.Equal("reg@test.local", body.User.Email);
    }

    [Fact]
    public async Task Register_email_duplicado_409()
    {
        var req = new { email = "dup@test.local", password = "SenhaForte!123" };
        await fixture.CreateClient().PostAsJsonAsync("/auth/register", req);
        var resp = await fixture.CreateClient().PostAsJsonAsync("/auth/register", req);
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task Register_senha_curta_400_com_errors()
    {
        var resp = await fixture.CreateClient().PostAsJsonAsync("/auth/register",
            new { email = "x@test.local", password = "123" });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var txt = await resp.Content.ReadAsStringAsync();
        Assert.Contains("errors", txt);
    }

    [Fact]
    public async Task Login_ok_e_credenciais_erradas_401()
    {
        await fixture.CreateClient().PostAsJsonAsync("/auth/register",
            new { email = "login@test.local", password = "SenhaForte!123" });
        var ok = await fixture.CreateClient().PostAsJsonAsync("/auth/login",
            new { email = "login@test.local", password = "SenhaForte!123" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        // senha errada e usuário inexistente → mesma resposta 401 (não vaza qual falhou)
        var bad = await fixture.CreateClient().PostAsJsonAsync("/auth/login",
            new { email = "login@test.local", password = "errada!1234" });
        Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);
        var unknown = await fixture.CreateClient().PostAsJsonAsync("/auth/login",
            new { email = "nao-existe@test.local", password = "qualquer!1234" });
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
    }

    [Fact]
    public async Task Refresh_rotaciona_e_invalida_antigo()
    {
        var reg = await fixture.CreateClient().PostAsJsonAsync("/auth/register",
            new { email = "rot@test.local", password = "SenhaForte!123" });
        var auth = await reg.Content.ReadFromJsonAsync<AuthResponse>();

        var r1 = await fixture.CreateClient().PostAsJsonAsync("/auth/refresh",
            new { refreshToken = auth!.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        var novo = await r1.Content.ReadFromJsonAsync<RefreshResponse>();
        Assert.NotEqual(auth.RefreshToken, novo!.RefreshToken);

        // token antigo não pode funcionar de novo (revogado)
        var reuse = await fixture.CreateClient().PostAsJsonAsync("/auth/refresh",
            new { refreshToken = auth.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);

        // reuso detectado → o novo também foi revogado em cascata
        var cascade = await fixture.CreateClient().PostAsJsonAsync("/auth/refresh",
            new { refreshToken = novo.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, cascade.StatusCode);
    }
}

public sealed record RefreshResponse(string AccessToken, string RefreshToken);
