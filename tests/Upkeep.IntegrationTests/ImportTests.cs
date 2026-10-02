using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Upkeep.IntegrationTests;

/// <summary>
/// M7 Task 1: POST /import — restore ADITIVO de um arquivo de export (M6): ids novos,
/// UserId do importador, relações remapeadas (templateId → novo template do mesmo asset),
/// tudo num SaveChanges (atômico). O round-trip export→import é o contrato vivo.
/// </summary>
[Collection("ApiTests")]
public class ImportTests(ApiFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static Task<HttpResponseMessage> PostImportAsync(HttpClient client, string json,
        CancellationToken ct) =>
        client.PostAsync("/import", new StringContent(json, Encoding.UTF8, "application/json"), ct);

    private static async Task<Guid> CreateAssetAsync(HttpClient client, CancellationToken ct,
        string nome, string tipo = "veiculo", int? odometroAtual = null)
    {
        var unico = nome + " " + Guid.NewGuid().ToString("N")[..8];
        return (await (await client.PostAsJsonAsync("/assets",
            new { nome = unico, tipo, odometroAtual }, ct))
            .Content.ReadFromJsonAsync<AssetResponse>(Json, ct))!.Id;
    }

    private static async Task<Guid> CreateTemplateAsync(HttpClient client, Guid assetId,
        string titulo, CancellationToken ct, int? intervaloKm = null, int? intervaloMeses = null)
    {
        return (await (await client.PostAsJsonAsync($"/assets/{assetId}/templates",
            new { titulo, intervaloKm, intervaloMeses, baselineData = new DateOnly(2026, 1, 10) }, ct))
            .Content.ReadFromJsonAsync<TemplateResponse>(Json, ct))!.Id;
    }

    [Fact]
    public async Task Import_round_trip_do_export_dobra_dados_e_preserva_relacoes()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync("import-roundtrip@test.local");

        // mesma cena do ExportTests: 2 assets / 3 templates / 2 serviços
        var corsa = await CreateAssetAsync(client, ct, "Corsa 2012", odometroAtual: 50_000);
        var casa = await CreateAssetAsync(client, ct, "Apartamento", "casa");
        var trocaOleo = await CreateTemplateAsync(client, corsa, "Troca de óleo", ct, intervaloMeses: 3);
        await CreateTemplateAsync(client, corsa, "Revisão geral", ct, intervaloKm: 10_000);
        await CreateTemplateAsync(client, casa, "Pintura da fachada", ct, intervaloMeses: 12);
        await client.PostAsJsonAsync($"/assets/{corsa}/services",
            new { templateId = trocaOleo, data = new DateOnly(2026, 3, 15), odometro = 60_000, custo = 500.10m }, ct);
        await client.PostAsJsonAsync($"/assets/{casa}/services",
            new { templateId = (Guid?)null, data = new DateOnly(2026, 5, 20), custo = 434.46m }, ct);

        // exporta → importa o JSON CRU (round-trip real, sem retoque)
        var exportRaw = await (await client.GetAsync("/export", ct)).Content.ReadAsStringAsync(ct);
        var resp = await PostImportAsync(client, exportRaw, ct);

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ImportResultResponse>(Json, ct);
        Assert.Equal(new Importados(2, 3, 2), body!.Importados);

        // contagens DOBRAM: 4 assets / 6 templates / 4 serviços (2º export conta tudo)
        var pos = JsonDocument.Parse(await (await client.GetAsync("/export", ct))
            .Content.ReadAsStringAsync(ct));
        var assetsPos = pos.RootElement.GetProperty("assets");
        Assert.Equal(4, assetsPos.GetArrayLength());
        Assert.Equal(6, assetsPos.EnumerateArray().Sum(a => a.GetProperty("templates").GetArrayLength()));
        Assert.Equal(4, assetsPos.EnumerateArray().Sum(a => a.GetProperty("services").GetArrayLength()));

        // cópia do corsa: mesmo nome, id NOVO, dados copiados (odômetro avançou p/ 60.000 no original)
        var copiaCorsa = assetsPos.EnumerateArray()
            .Single(a => a.GetProperty("nome").GetString()!.StartsWith("Corsa 2012")
                && a.GetProperty("id").GetString() != corsa.ToString());
        Assert.Equal(60_000, copiaCorsa.GetProperty("odometroAtual").GetInt32());

        // relações remapeadas: service da cópia aponta pra template da PRÓPRIA cópia
        var idsTemplatesCopia = copiaCorsa.GetProperty("templates").EnumerateArray()
            .Select(t => t.GetProperty("id").GetString()).ToHashSet();
        Assert.Equal(2, idsTemplatesCopia.Count);
        Assert.All(idsTemplatesCopia, id => Assert.NotEqual(trocaOleo.ToString(), id));
        var svcCopia = Assert.Single(copiaCorsa.GetProperty("services").EnumerateArray());
        Assert.Contains(svcCopia.GetProperty("templateId").GetString(), idsTemplatesCopia);

        // serviço avulso da cópia da casa continua avulso
        var copiaCasa = assetsPos.EnumerateArray()
            .Single(a => a.GetProperty("nome").GetString()!.StartsWith("Apartamento")
                && a.GetProperty("id").GetString() != casa.ToString());
        Assert.Null(Assert.Single(copiaCasa.GetProperty("services").EnumerateArray())
            .GetProperty("templateId").GetString());

        // status volta a computar: template com baseline/serviço antigos → cópia vencida de novo
        var lista = await (await client.GetAsync("/assets", ct))
            .Content.ReadFromJsonAsync<List<AssetResponse>>(Json, ct);
        var corsas = lista!.Where(a => a.Nome.StartsWith("Corsa 2012")).ToList();
        Assert.Equal(2, corsas.Count);
        Assert.All(corsas, a => Assert.True(a.Overdue >= 1, "original e cópia com template vencido"));
    }

    [Theory]
    [InlineData("{}")] // assets ausente
    [InlineData("""{"user":{"id":"x","email":"y"}}""")] // assets ausente (só metadados)
    [InlineData("""{"assets":"x"}""")] // assets não é array
    [InlineData("""{"assets":null}""")] // assets null não é array
    [InlineData("isto nem json é")] // corpo não parseia
    public async Task Import_arquivo_invalido_400_e_nada_persistido(string payload)
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync();

        var resp = await PostImportAsync(client, payload, ct);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var problem = await resp.Content.ReadFromJsonAsync<ProblemBody>(Json, ct);
        Assert.Equal("Arquivo inválido", problem!.Title);
        // atomicidade na validação: nenhum asset criado
        var lista = await (await client.GetAsync("/assets", ct))
            .Content.ReadFromJsonAsync<List<AssetResponse>>(Json, ct);
        Assert.Empty(lista!);
    }

    [Theory]
    [InlineData("""{"assets":[{"nome":"X","tipo":"barco"}]}""", "Asset 0")]
    [InlineData("""{"assets":[{"nome":"X"}]}""", "Asset 0")] // tipo ausente
    [InlineData("""{"assets":[{"nome":"X","tipo":"casa","services":[{"data":"2026-01-10","custo":-5}]}]}""", "Asset 0")]
    [InlineData("""{"assets":[{"nome":"X","tipo":"casa","services":[{"data":"ontem","custo":10}]}]}""", "Asset 0")]
    [InlineData("""{"assets":[{"nome":"Ok","tipo":"casa"},{"nome":"X","tipo":"barco"}]}""", "Asset 1")]
    public async Task Import_valida_essenciais_por_asset_com_indice(string payload, string fragmento)
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync();

        var resp = await PostImportAsync(client, payload, ct);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var problem = await resp.Content.ReadFromJsonAsync<ProblemBody>(Json, ct);
        Assert.StartsWith(fragmento, problem!.Title); // "Asset {i}: {motivo}"
        // nada importado quando qualquer asset viola o essencial
        var lista = await (await client.GetAsync("/assets", ct))
            .Content.ReadFromJsonAsync<List<AssetResponse>>(Json, ct);
        Assert.Empty(lista!);
    }

    [Fact]
    public async Task Import_acima_de_200_assets_400()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync();
        var payload = JsonSerializer.Serialize(new
        {
            assets = Enumerable.Range(1, 201).Select(i => new { nome = $"Excedente {i}", tipo = "veiculo" })
        }, Json);

        var resp = await PostImportAsync(client, payload, ct);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var problem = await resp.Content.ReadFromJsonAsync<ProblemBody>(Json, ct);
        Assert.Contains("200", problem!.Title);
        var lista = await (await client.GetAsync("/assets", ct))
            .Content.ReadFromJsonAsync<List<AssetResponse>>(Json, ct);
        Assert.Empty(lista!);
    }

    [Fact]
    public async Task Import_sem_token_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var resp = await PostImportAsync(fixture.CreateClient(), """{"assets":[]}""", ct);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Import_export_de_outro_user_atribui_tudo_ao_importador()
    {
        var ct = TestContext.Current.CancellationToken;
        var (alice, _) = await fixture.CreateAuthenticatedClientAsync("import-alice@test.local");
        var (bob, _) = await fixture.CreateAuthenticatedClientAsync("import-bob@test.local");

        var daAlice = await CreateAssetAsync(alice, ct, "Segredo da Alice");
        var tplAlice = await CreateTemplateAsync(alice, daAlice, "Plano da Alice", ct, intervaloMeses: 6);
        await alice.PostAsJsonAsync($"/assets/{daAlice}/services",
            new { templateId = tplAlice, data = new DateOnly(2026, 4, 1), custo = 700m }, ct);

        // bob importa o EXPORT da alice: dados passam a ser DO BOB (ids novos)
        var exportAlice = await (await alice.GetAsync("/export", ct)).Content.ReadAsStringAsync(ct);
        var resp = await PostImportAsync(bob, exportAlice, ct);

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        Assert.Equal(new Importados(1, 1, 1),
            (await resp.Content.ReadFromJsonAsync<ImportResultResponse>(Json, ct))!.Importados);

        var doBob = Assert.Single((await (await bob.GetAsync("/assets", ct))
            .Content.ReadFromJsonAsync<List<AssetResponse>>(Json, ct))!)!;
        Assert.StartsWith("Segredo da Alice", doBob.Nome);
        Assert.NotEqual(daAlice, doBob.Id); // id novo, cópia

        // relação remapeada DENTRO da conta do bob
        var tplBob = Assert.Single((await (await bob.GetAsync($"/assets/{doBob.Id}/templates", ct))
            .Content.ReadFromJsonAsync<List<TemplateResponse>>(Json, ct))!)!;
        var svcBob = Assert.Single((await (await bob.GetAsync($"/assets/{doBob.Id}/services", ct))
            .Content.ReadFromJsonAsync<List<ServiceResponse>>(Json, ct))!)!;
        Assert.Equal(tplBob.Id, svcBob.TemplateId);
        Assert.NotEqual(tplAlice, tplBob.Id);

        // alice intocada: continua com 1 asset (isolamento entre contas)
        Assert.Single((await (await alice.GetAsync("/assets", ct))
            .Content.ReadFromJsonAsync<List<AssetResponse>>(Json, ct))!);
    }
}

public sealed record Importados(int Assets, int Templates, int Services);
public sealed record ImportResultResponse(Importados Importados);
