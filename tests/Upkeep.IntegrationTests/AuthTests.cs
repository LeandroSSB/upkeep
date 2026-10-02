using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Upkeep.Infrastructure;
using Xunit;

namespace Upkeep.IntegrationTests;

[Collection("ApiTests")]
public class AuthTests(ApiFixture fixture)
{
    [Fact]
    public async Task Register_retorna_201_com_tokens_e_usuario()
    {
        var ct = TestContext.Current.CancellationToken;
        var resp = await fixture.CreateClient().PostAsJsonAsync("/auth/register",
            new { email = "reg@test.local", password = "SenhaForte!123" }, ct);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<AuthResponse>(ct);
        Assert.NotEmpty(body!.AccessToken);
        Assert.NotEmpty(body.RefreshToken);
        Assert.Equal("reg@test.local", body.User.Email);
    }

    [Fact]
    public async Task Register_email_duplicado_409()
    {
        var ct = TestContext.Current.CancellationToken;
        var req = new { email = "dup@test.local", password = "SenhaForte!123" };
        await fixture.CreateClient().PostAsJsonAsync("/auth/register", req, ct);
        var resp = await fixture.CreateClient().PostAsJsonAsync("/auth/register", req, ct);
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task Register_senha_curta_400_com_errors()
    {
        var ct = TestContext.Current.CancellationToken;
        var resp = await fixture.CreateClient().PostAsJsonAsync("/auth/register",
            new { email = "x@test.local", password = "123" }, ct);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var txt = await resp.Content.ReadAsStringAsync(ct);
        Assert.Contains("errors", txt);
    }

    [Fact]
    public async Task Login_ok_e_credenciais_erradas_401()
    {
        var ct = TestContext.Current.CancellationToken;
        await fixture.CreateClient().PostAsJsonAsync("/auth/register",
            new { email = "login@test.local", password = "SenhaForte!123" }, ct);
        var ok = await fixture.CreateClient().PostAsJsonAsync("/auth/login",
            new { email = "login@test.local", password = "SenhaForte!123" }, ct);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        // senha errada e usuário inexistente → mesma resposta 401 (não vaza qual falhou)
        var bad = await fixture.CreateClient().PostAsJsonAsync("/auth/login",
            new { email = "login@test.local", password = "errada!1234" }, ct);
        Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);
        var unknown = await fixture.CreateClient().PostAsJsonAsync("/auth/login",
            new { email = "nao-existe@test.local", password = "qualquer!1234" }, ct);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
    }

    [Fact]
    public async Task Refresh_corpo_malformado_400_nunca_500()
    {
        var ct = TestContext.Current.CancellationToken;
        // {} ou {"refreshToken": null}: sem validator no endpoint, o corpo chegava ao
        // handler e HashToken(null) estourava 500
        var vazio = await fixture.CreateClient().PostAsJsonAsync("/auth/refresh", new { }, ct);
        Assert.Equal(HttpStatusCode.BadRequest, vazio.StatusCode);

        var nulo = await fixture.CreateClient().PostAsJsonAsync("/auth/refresh",
            new { refreshToken = (string?)null }, ct);
        Assert.Equal(HttpStatusCode.BadRequest, nulo.StatusCode);
        Assert.Contains("errors", await nulo.Content.ReadAsStringAsync(ct));
    }

    [Fact]
    public async Task Refresh_rotaciona_e_invalida_antigo()
    {
        var ct = TestContext.Current.CancellationToken;
        var reg = await fixture.CreateClient().PostAsJsonAsync("/auth/register",
            new { email = "rot@test.local", password = "SenhaForte!123" }, ct);
        var auth = await reg.Content.ReadFromJsonAsync<AuthResponse>(ct);

        var r1 = await fixture.CreateClient().PostAsJsonAsync("/auth/refresh",
            new { refreshToken = auth!.RefreshToken }, ct);
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        var novo = await r1.Content.ReadFromJsonAsync<RefreshResponse>(ct);
        Assert.NotEqual(auth.RefreshToken, novo!.RefreshToken);

        // token antigo não pode funcionar de novo (revogado)
        var reuse = await fixture.CreateClient().PostAsJsonAsync("/auth/refresh",
            new { refreshToken = auth.RefreshToken }, ct);
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);

        // reuso detectado → o novo também foi revogado em cascata
        var cascade = await fixture.CreateClient().PostAsJsonAsync("/auth/refresh",
            new { refreshToken = novo.RefreshToken }, ct);
        Assert.Equal(HttpStatusCode.Unauthorized, cascade.StatusCode);
    }
    [Fact]
    public async Task Login_purga_refresh_tokens_expirados_e_antigos()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = fixture.CreateClient();
        var email = "purge@test.local";

        // 3 refresh tokens: register + login + login — os 2 primeiros serão alvo do
        // purge (expirado / revogado há 40d) e o 3º permanece válido
        var reg = await client.PostAsJsonAsync("/auth/register",
            new { email, password = "SenhaForte!123" }, ct);
        reg.EnsureSuccessStatusCode();
        var auth = await reg.Content.ReadFromJsonAsync<AuthResponse>(ct);
        var userId = auth!.User.Id;
        await client.PostAsJsonAsync("/auth/login", new { email, password = "SenhaForte!123" }, ct);
        var prePurge = await client.PostAsJsonAsync("/auth/login",
            new { email, password = "SenhaForte!123" }, ct);
        prePurge.EnsureSuccessStatusCode();

        Guid expiradoId, revogadoId;
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<UpkeepDbContext>();
            var tokens = await db.RefreshTokens.Where(t => t.UserId == userId)
                .OrderBy(t => t.CreatedAt).ToListAsync(ct);
            Assert.True(tokens.Count >= 3, $"esperados 3 tokens, achei {tokens.Count}");
            expiradoId = tokens[0].Id;
            revogadoId = tokens[1].Id;
            tokens[0].ExpiresAt = DateTime.UtcNow.AddDays(-1);
            tokens[1].RevokedAt = DateTime.UtcNow.AddDays(-40);
            await db.SaveChangesAsync(ct);
        }

        var resp = await client.PostAsJsonAsync("/auth/login",
            new { email, password = "SenhaForte!123" }, ct);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<UpkeepDbContext>();
            var restantes = await db.RefreshTokens.Where(t => t.UserId == userId).ToListAsync(ct);
            // os 2 alvo do purge sumiram (sem o purge permaneceriam no banco)
            Assert.DoesNotContain(restantes, t => t.Id == expiradoId);
            Assert.DoesNotContain(restantes, t => t.Id == revogadoId);
            // nenhum token expirado ou revogado há mais de 30d sobreviveu para esse user
            var cutoff = DateTime.UtcNow.AddDays(-30);
            Assert.DoesNotContain(restantes, t => t.ExpiresAt <= DateTime.UtcNow);
            Assert.DoesNotContain(restantes, t => t.RevokedAt is not null && t.RevokedAt <= cutoff);
            // sobraram válidos: o emitido antes do purge + o novo do último login
            Assert.True(restantes.Count >= 2, $"esperados >=2 tokens válidos, achei {restantes.Count}");
        }

        // o refresh token emitido pelo último login funciona (purge não deletou o recém-criado)
        var finalAuth = await resp.Content.ReadFromJsonAsync<AuthResponse>(ct);
        var refresh = await client.PostAsJsonAsync("/auth/refresh",
            new { refreshToken = finalAuth!.RefreshToken }, ct);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
    }
}

public sealed record RefreshResponse(string AccessToken, string RefreshToken);
