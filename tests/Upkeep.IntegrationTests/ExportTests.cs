using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Upkeep.IntegrationTests;

/// <summary>
/// M6 Task 1: GET /export — JSON completo dos dados do usuário (assets com
/// templates e serviços aninhados), escopado por UserId, servido com
/// content-disposition de download (upkeep-export-yyyy-MM-dd.json, UTC).
/// </summary>
[Collection("ApiTests")]
public class ExportTests(ApiFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static async Task<Guid> CreateAssetAsync(HttpClient client, CancellationToken ct,
        string nome = "Asset", string tipo = "veiculo")
    {
        var unico = nome + " " + Guid.NewGuid().ToString("N")[..8];
        return (await (await client.PostAsJsonAsync("/assets", new { nome = unico, tipo }, ct))
            .Content.ReadFromJsonAsync<AssetResponse>(Json, ct))!.Id;
    }

    private static async Task<Guid> CreateTemplateAsync(HttpClient client, Guid assetId,
        string titulo, CancellationToken ct)
    {
        return (await (await client.PostAsJsonAsync($"/assets/{assetId}/templates",
            new { titulo, intervaloKm = 10_000, baselineData = new DateOnly(2026, 1, 10) }, ct))
            .Content.ReadFromJsonAsync<TemplateResponse>(Json, ct))!.Id;
    }

    private static Task<HttpResponseMessage> PostServiceAsync(HttpClient client, Guid assetId,
        Guid? templateId, DateOnly data, decimal custo, CancellationToken ct) =>
        client.PostAsJsonAsync($"/assets/{assetId}/services", new { templateId, data, custo }, ct);

    [Fact]
    public async Task Export_aninha_templates_e_services_por_asset()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await fixture.CreateAuthenticatedClientAsync("export-full@test.local");

        // 2 assets / 3 templates / 2 serviços
        var carro = await CreateAssetAsync(client, ct, "Corsa 2012");
        var casa = await CreateAssetAsync(client, ct, "Apartamento", "casa");
        var trocaOleo = await CreateTemplateAsync(client, carro, "Troca de óleo", ct);
        var revisao = await CreateTemplateAsync(client, carro, "Revisão geral", ct);
        var pintura = await CreateTemplateAsync(client, casa, "Pintura da fachada", ct);
        await PostServiceAsync(client, carro, trocaOleo, new DateOnly(2026, 3, 15), 500.10m, ct);
        await PostServiceAsync(client, casa, null, new DateOnly(2026, 5, 20), 434.46m, ct);

        var resp = await client.GetAsync("/export", ct);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("application/json", resp.Content.Headers.ContentType?.MediaType);
        // attachment com a data de HOJE (UTC) no nome do arquivo
        var hoje = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var cd = resp.Content.Headers.ContentDisposition;
        Assert.Equal("attachment", cd?.DispositionType);
        Assert.Equal($"upkeep-export-{hoje}.json", cd?.FileName);

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;

        // bloco user + carimbo
        Assert.Equal(userId.ToString(), root.GetProperty("user").GetProperty("id").GetString());
        Assert.Equal("export-full@test.local", root.GetProperty("user").GetProperty("email").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("exportadoEm").GetString()));

        // 2 assets; cada um carrega SOMENTE os próprios templates/serviços
        var assets = root.GetProperty("assets");
        Assert.Equal(2, assets.GetArrayLength());
        var porId = assets.EnumerateArray().ToDictionary(a => a.GetProperty("id").GetString()!);

        var doCarro = porId[carro.ToString()]!;
        Assert.Equal("veiculo", doCarro.GetProperty("tipo").GetString());
        Assert.Equal(2, doCarro.GetProperty("templates").GetArrayLength());
        var idsTemplates = doCarro.GetProperty("templates").EnumerateArray()
            .Select(t => t.GetProperty("id").GetString()).ToHashSet();
        Assert.True(idsTemplates.SetEquals(new[] { trocaOleo.ToString(), revisao.ToString() }),
            "templates do carro = exatamente os 2 criados");
        var svc = Assert.Single(doCarro.GetProperty("services").EnumerateArray());
        Assert.Equal(trocaOleo.ToString(), svc.GetProperty("templateId").GetString());
        Assert.Equal(500.10m, svc.GetProperty("custo").GetDecimal());
        Assert.Equal("2026-03-15", svc.GetProperty("data").GetString());
        Assert.Equal(carro.ToString(), svc.GetProperty("assetId").GetString());

        var daCasa = porId[casa.ToString()]!;
        Assert.Equal("casa", daCasa.GetProperty("tipo").GetString());
        Assert.Single(daCasa.GetProperty("templates").EnumerateArray());
        Assert.Equal(pintura.ToString(),
            daCasa.GetProperty("templates")[0].GetProperty("id").GetString());
        var svcCasa = Assert.Single(daCasa.GetProperty("services").EnumerateArray());
        Assert.Null(svcCasa.GetProperty("templateId").GetString()); // serviço avulso segue null

        // template aninhado carrega os campos dele (inclui o assetId do pai)
        var tpl = doCarro.GetProperty("templates").EnumerateArray().First();
        Assert.Equal(carro.ToString(), tpl.GetProperty("assetId").GetString());
        Assert.Equal(10_000, tpl.GetProperty("intervaloKm").GetInt32());
        Assert.Equal("2026-01-10", tpl.GetProperty("baselineData").GetString());
    }

    [Fact]
    public async Task Export_cross_user_nao_vaza_dados_do_outro()
    {
        var ct = TestContext.Current.CancellationToken;
        var (alice, aliceId) = await fixture.CreateAuthenticatedClientAsync();
        var (bob, _) = await fixture.CreateAuthenticatedClientAsync();
        var daAlice = await CreateAssetAsync(alice, ct, "Da Alice");
        var doBob = await CreateAssetAsync(bob, ct, "Do Bob");
        var tplAlice = await CreateTemplateAsync(alice, daAlice, "Plano Alice", ct);
        var tplBob = await CreateTemplateAsync(bob, doBob, "Plano Bob", ct);
        await PostServiceAsync(alice, daAlice, tplAlice, new DateOnly(2026, 4, 1), 700m, ct);
        await PostServiceAsync(bob, doBob, tplBob, new DateOnly(2026, 4, 2), 111m, ct);

        var resp = await alice.GetAsync("/export", ct);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var raw = await resp.Content.ReadAsStringAsync(ct);

        using var doc = JsonDocument.Parse(raw);
        Assert.Equal(aliceId.ToString(), doc.RootElement.GetProperty("user").GetProperty("id").GetString());
        var assets = doc.RootElement.GetProperty("assets");
        var unico = Assert.Single(assets.EnumerateArray());
        Assert.Equal(daAlice.ToString(), unico.GetProperty("id").GetString());
        Assert.Single(unico.GetProperty("templates").EnumerateArray());
        Assert.Single(unico.GetProperty("services").EnumerateArray());

        // nada do bob em lugar NENHUM do JSON (id do asset, id do template, nome)
        Assert.DoesNotContain(doBob.ToString(), raw);
        Assert.DoesNotContain(tplBob.ToString(), raw);
        Assert.DoesNotContain("Do Bob", raw);
    }

    [Fact]
    public async Task Export_sem_token_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var resp = await fixture.CreateClient().GetAsync("/export", ct);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Export_usuario_vazio_200_com_assets_vazio()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await fixture.CreateAuthenticatedClientAsync("export-vazio@test.local");

        var resp = await client.GetAsync("/export", ct);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        Assert.Equal(0, root.GetProperty("assets").GetArrayLength());
        Assert.Equal(userId.ToString(), root.GetProperty("user").GetProperty("id").GetString());
        Assert.Equal("export-vazio@test.local", root.GetProperty("user").GetProperty("email").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("exportadoEm").GetString()));
    }
}
