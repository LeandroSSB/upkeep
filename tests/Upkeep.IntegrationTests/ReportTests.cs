using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Upkeep.IntegrationTests;

/// <summary>
/// Relatório de custos (Task 11): GET /reports/costs — agregação por asset
/// (GroupBy traduzido p/ SQL), filtros opcionais assetId/from/to (datas
/// inclusivas), sempre escopado ao usuário; assetId alheio → vazio (filtro).
/// </summary>
[Collection("ApiTests")]
public class ReportTests(ApiFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static async Task<Guid> CreateAssetAsync(HttpClient client, CancellationToken ct,
        string nome = "Asset")
    {
        var unico = nome + " " + Guid.NewGuid().ToString("N")[..8];
        return (await (await client.PostAsJsonAsync("/assets",
            new { nome = unico, tipo = "veiculo" }, ct))
            .Content.ReadFromJsonAsync<AssetResponse>(Json, ct))!.Id;
    }

    private static Task<HttpResponseMessage> PostServiceAsync(HttpClient client, Guid assetId,
        DateOnly data, decimal custo, CancellationToken ct) =>
        client.PostAsJsonAsync($"/assets/{assetId}/services", new { data, custo }, ct);

    private static async Task<CostReportResponse> GetReportAsync(HttpClient client,
        CancellationToken ct, string? query = null)
    {
        var resp = await client.GetAsync("/reports/costs" + (query is null ? "" : "?" + query), ct);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<CostReportResponse>(Json, ct))!;
    }

    [Fact]
    public async Task Sem_filtro_agrupa_por_asset_ordenha_por_total_desc()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync();
        var carro = await CreateAssetAsync(client, ct, "Corsa 2012");
        var casa = await CreateAssetAsync(client, ct, "Apartamento");

        await PostServiceAsync(client, carro, new DateOnly(2026, 1, 15), 500.10m, ct);
        await PostServiceAsync(client, carro, new DateOnly(2026, 8, 2), 300.00m, ct);
        await PostServiceAsync(client, casa, new DateOnly(2026, 5, 20), 434.46m, ct);

        var rel = await GetReportAsync(client, ct);

        Assert.Equal(1234.56m, rel.Total); // 500,10 + 300 + 434,46
        Assert.Null(rel.De);
        Assert.Null(rel.Ate);
        Assert.Equal(2, rel.PorAsset.Count);
        // carro (800,10) > casa (434,46) — ordenação por total desc
        Assert.Equal(carro, rel.PorAsset[0].AssetId);
        Assert.StartsWith("Corsa 2012", rel.PorAsset[0].Nome);
        Assert.Equal(800.10m, rel.PorAsset[0].Total);
        Assert.Equal(2, rel.PorAsset[0].Quantidade);
        Assert.Equal(casa, rel.PorAsset[1].AssetId);
        Assert.Equal(434.46m, rel.PorAsset[1].Total);
        Assert.Equal(1, rel.PorAsset[1].Quantidade);
    }

    [Fact]
    public async Task Filtro_por_assetId_retorna_somente_o_asset()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync();
        var a = await CreateAssetAsync(client, ct, "Asset A");
        var b = await CreateAssetAsync(client, ct, "Asset B");

        await PostServiceAsync(client, a, new DateOnly(2026, 2, 10), 100m, ct);
        await PostServiceAsync(client, b, new DateOnly(2026, 2, 11), 250m, ct);
        await PostServiceAsync(client, b, new DateOnly(2026, 2, 12), 50m, ct);

        var rel = await GetReportAsync(client, ct, $"assetId={b}");

        Assert.Single(rel.PorAsset);
        Assert.Equal(b, rel.PorAsset[0].AssetId);
        Assert.Equal(300m, rel.PorAsset[0].Total);
        Assert.Equal(2, rel.PorAsset[0].Quantidade);
        Assert.Equal(300m, rel.Total);
    }

    [Fact]
    public async Task Filtro_por_periodo_bounds_inclusivos()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync();
        var asset = await CreateAssetAsync(client, ct);

        await PostServiceAsync(client, asset, new DateOnly(2026, 1, 31), 10m, ct); // antes
        await PostServiceAsync(client, asset, new DateOnly(2026, 2, 1), 20m, ct);  // borda from
        await PostServiceAsync(client, asset, new DateOnly(2026, 3, 15), 30m, ct); // meio
        await PostServiceAsync(client, asset, new DateOnly(2026, 3, 31), 40m, ct); // borda to
        await PostServiceAsync(client, asset, new DateOnly(2026, 4, 1), 50m, ct);  // depois

        var rel = await GetReportAsync(client, ct,
            $"assetId={asset}&from=2026-02-01&to=2026-03-31");

        Assert.Equal(new DateOnly(2026, 2, 1), rel.De); // ecoa o filtro efetivo
        Assert.Equal(new DateOnly(2026, 3, 31), rel.Ate);
        Assert.Equal(90m, rel.Total); // 20 + 30 + 40 — ambas as bordas contam
        Assert.Single(rel.PorAsset);
        Assert.Equal(3, rel.PorAsset[0].Quantidade);
    }

    [Fact]
    public async Task Filtro_somente_from_ou_somente_to()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync();
        var asset = await CreateAssetAsync(client, ct);

        await PostServiceAsync(client, asset, new DateOnly(2026, 1, 10), 10m, ct);
        await PostServiceAsync(client, asset, new DateOnly(2026, 6, 10), 20m, ct);

        var soFrom = await GetReportAsync(client, ct, "from=2026-03-01");
        Assert.Equal(20m, soFrom.Total);
        Assert.Equal(new DateOnly(2026, 3, 1), soFrom.De);
        Assert.Null(soFrom.Ate);

        var soTo = await GetReportAsync(client, ct, "to=2026-03-01");
        Assert.Equal(10m, soTo.Total);
        Assert.Null(soTo.De);
        Assert.Equal(new DateOnly(2026, 3, 1), soTo.Ate);
    }

    [Fact]
    public async Task Cross_user_assetId_alheio_vazio_e_servicos_nao_vazam()
    {
        var ct = TestContext.Current.CancellationToken;
        var (alice, _) = await fixture.CreateAuthenticatedClientAsync();
        var (bob, _) = await fixture.CreateAuthenticatedClientAsync();
        var daAlice = await CreateAssetAsync(alice, ct, "Da Alice");
        var doBob = await CreateAssetAsync(bob, ct, "Do Bob");

        await PostServiceAsync(alice, daAlice, new DateOnly(2026, 4, 1), 700m, ct);
        await PostServiceAsync(bob, doBob, new DateOnly(2026, 4, 2), 111m, ct);

        // assetId alheio → resultado vazio (filtro, não 404)
        var alheio = await GetReportAsync(alice, ct, $"assetId={doBob}");
        Assert.Equal(0m, alheio.Total);
        Assert.Empty(alheio.PorAsset);
        Assert.Null(alheio.De);

        // sem filtro: bob NÃO vê o serviço da alice (e vice-versa)
        var relBob = await GetReportAsync(bob, ct);
        Assert.Single(relBob.PorAsset);
        Assert.Equal(doBob, relBob.PorAsset[0].AssetId);
        Assert.Equal(111m, relBob.Total);

        var relAlice = await GetReportAsync(alice, ct);
        Assert.Single(relAlice.PorAsset);
        Assert.Equal(daAlice, relAlice.PorAsset[0].AssetId);
        Assert.Equal(700m, relAlice.Total);
    }

    [Fact]
    public async Task From_maior_que_to_retorna_200_total_zero_e_vazio()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync();
        var asset = await CreateAssetAsync(client, ct);

        await PostServiceAsync(client, asset, new DateOnly(2026, 3, 10), 100m, ct);
        await PostServiceAsync(client, asset, new DateOnly(2026, 6, 10), 200m, ct);

        // from > to: janela impossível é filtro que casa nada — 200, não erro
        var rel = await GetReportAsync(client, ct, "from=2027-01-01&to=2026-01-01");

        Assert.Equal(0m, rel.Total);
        Assert.Empty(rel.PorAsset);
    }

    [Fact]
    public async Task Sem_token_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var anon = fixture.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anon.GetAsync("/reports/costs", ct)).StatusCode);
    }
}

public sealed record CostReportResponse(
    decimal Total,
    List<CostByAssetResponse> PorAsset,
    DateOnly? De,
    DateOnly? Ate);

public sealed record CostByAssetResponse(
    Guid AssetId,
    string Nome,
    decimal Total,
    int Quantidade);
