using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Upkeep.IntegrationTests;

/// <summary>
/// Relatório de custos (Task 11): GET /reports/costs — agregação por asset
/// (GroupBy traduzido p/ SQL), filtros opcionais assetId/from/to (datas
/// inclusivas), sempre escopado ao usuário; assetId alheio → vazio (filtro).
/// Task M6-2: ?groupBy=month soma à resposta a régua mensal de 12 meses
/// (clampada ao período from/to, meses vazios com zero) — sem o parâmetro,
/// porMes vem null (compat com clientes antigos).
/// Task M8-2: assetId de um VEÍCULO do usuário soma custoPorKm { total,
/// kmRodados, porKm } — total segue from/to; kmRodados = odometroAtual −
/// min(primeiro odômetro conhecido: baselines de template + odômetros de
/// serviço > 0, base histórica que IGNORA datas); kmRodados ≤ 0 → porKm null.
/// </summary>
[Collection("ApiTests")]
public class ReportTests(ApiFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static async Task<Guid> CreateAssetAsync(HttpClient client, CancellationToken ct,
        string nome = "Asset", string tipo = "veiculo", int? odometroAtual = null)
    {
        var unico = nome + " " + Guid.NewGuid().ToString("N")[..8];
        return (await (await client.PostAsJsonAsync("/assets",
            new { nome = unico, tipo, odometroAtual }, ct))
            .Content.ReadFromJsonAsync<AssetResponse>(Json, ct))!.Id;
    }

    private static Task<HttpResponseMessage> PostServiceAsync(HttpClient client, Guid assetId,
        DateOnly data, decimal custo, CancellationToken ct, int? odometro = null) =>
        client.PostAsJsonAsync($"/assets/{assetId}/services", new { data, custo, odometro }, ct);

    /// <summary>Template de km com baseline — intervalo_km é obrigatório quando há baseline.</summary>
    private static Task<HttpResponseMessage> PostTemplateAsync(HttpClient client, Guid assetId,
        int baselineOdometro, CancellationToken ct) =>
        client.PostAsJsonAsync($"/assets/{assetId}/templates",
            new { titulo = "Revisão", intervaloKm = 10000, baselineOdometro, baselineData = new DateOnly(2026, 1, 1) }, ct);

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

    // ---- groupBy=month: régua mensal de 12 meses (Task M6-2) ----

    /// <summary>Primeiro dia do mês a N meses do atual (UTC) — dia 1 nunca é futuro.</summary>
    private static DateOnly Mes(int offset) =>
        new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1).AddMonths(offset);

    private static string Chave(DateOnly mes) =>
        mes.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    [Fact]
    public async Task GroupBy_month_regua_de_12_com_valores_nos_meses_e_zeros()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync();
        var asset = await CreateAssetAsync(client, ct);

        // 3 meses distintos dentro da janela default (últimos 12)
        await PostServiceAsync(client, asset, Mes(-9), 10m, ct);
        await PostServiceAsync(client, asset, Mes(-4), 200m, ct);
        await PostServiceAsync(client, asset, Mes(0), 100.50m, ct);
        await PostServiceAsync(client, asset, Mes(0), 50m, ct);

        var rel = await GetReportAsync(client, ct, "groupBy=month");

        Assert.NotNull(rel.PorMes);
        Assert.Equal(12, rel.PorMes.Count);
        Assert.Equal(Chave(Mes(-11)), rel.PorMes[0].Mes); // ordem cronológica
        Assert.Equal(Chave(Mes(0)), rel.PorMes[^1].Mes);  // termina no mês atual

        var porChave = rel.PorMes.ToDictionary(m => m.Mes);
        Assert.Equal(10m, porChave[Chave(Mes(-9))].Total);
        Assert.Equal(1, porChave[Chave(Mes(-9))].Quantidade);
        Assert.Equal(200m, porChave[Chave(Mes(-4))].Total);
        Assert.Equal(150.50m, porChave[Chave(Mes(0))].Total); // 100,50 + 50
        Assert.Equal(2, porChave[Chave(Mes(0))].Quantidade);

        var comDado = new HashSet<string> { Chave(Mes(-9)), Chave(Mes(-4)), Chave(Mes(0)) };
        Assert.All(rel.PorMes.Where(m => !comDado.Contains(m.Mes)), m =>
        {
            Assert.Equal(0m, m.Total); // mês vazio entra na régua com zero
            Assert.Equal(0, m.Quantidade);
        });

        Assert.Equal(rel.Total, rel.PorMes.Sum(m => m.Total)); // régua soma = total
        Assert.Single(rel.PorAsset); // porAsset segue presente (groupBy soma visão)
    }

    [Fact]
    public async Task GroupBy_month_from_to_clampa_a_regua_ao_periodo()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync();
        var asset = await CreateAssetAsync(client, ct);

        await PostServiceAsync(client, asset, Mes(-2), 30m, ct);
        await PostServiceAsync(client, asset, Mes(0), 70m, ct);
        await PostServiceAsync(client, asset, Mes(-5), 999m, ct); // fora da janela

        var hoje = DateOnly.FromDateTime(DateTime.UtcNow);
        var rel = await GetReportAsync(client, ct,
            $"from={Mes(-2):O}&to={hoje:O}&groupBy=month");

        Assert.Equal(3, rel.PorMes!.Count); // régua clampada: -2, -1 e atual
        Assert.Equal(Chave(Mes(-2)), rel.PorMes[0].Mes);
        Assert.Equal(30m, rel.PorMes[0].Total);
        Assert.Equal(0m, rel.PorMes[1].Total); // mês vazio intermediário
        Assert.Equal(Chave(Mes(0)), rel.PorMes[2].Mes);
        Assert.Equal(70m, rel.PorMes[2].Total);
        Assert.Equal(100m, rel.Total); // 999 ficou fora (from) e fora da régua

        // from antigo NÃO estica a régua: interseção mantém a janela default de 12
        var amplo = await GetReportAsync(client, ct, "from=2020-01-01&groupBy=month");
        Assert.Equal(12, amplo.PorMes!.Count);
        Assert.Equal(Chave(Mes(-11)), amplo.PorMes[0].Mes);
        Assert.Equal(999m, amplo.PorMes.ToDictionary(m => m.Mes)[Chave(Mes(-5))].Total);
        Assert.Equal(amplo.Total, amplo.PorMes.Sum(m => m.Total));
    }

    [Fact]
    public async Task GroupBy_month_respeita_filtro_de_assetId()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync();
        var a = await CreateAssetAsync(client, ct, "Asset A");
        var b = await CreateAssetAsync(client, ct, "Asset B");

        await PostServiceAsync(client, a, Mes(-1), 400m, ct);
        await PostServiceAsync(client, b, Mes(-1), 25m, ct);
        await PostServiceAsync(client, b, Mes(-1), 75m, ct);

        var rel = await GetReportAsync(client, ct, $"assetId={b}&groupBy=month");

        var comDado = rel.PorMes!.Where(m => m.Total > 0).ToList();
        Assert.Single(comDado); // só o mês com serviço de B conta
        Assert.Equal(Chave(Mes(-1)), comDado[0].Mes);
        Assert.Equal(100m, comDado[0].Total); // 25 + 75 — nada do asset A
        Assert.Equal(2, comDado[0].Quantidade);
        Assert.Equal(100m, rel.Total);
        Assert.Single(rel.PorAsset); // só B
    }

    [Fact]
    public async Task Sem_groupBy_porMes_null_e_porAsset_como_antes()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync();
        var asset = await CreateAssetAsync(client, ct);
        await PostServiceAsync(client, asset, Mes(-3), 123m, ct);

        var rel = await GetReportAsync(client, ct); // sem groupBy na query

        Assert.Null(rel.PorMes); // compat: cliente antigo não recebe o campo
        Assert.Single(rel.PorAsset);
        Assert.Equal(asset, rel.PorAsset[0].AssetId);
        Assert.Equal(123m, rel.Total);
    }

    // ---- custoPorKm: insight de veículo (Task M8-2) ----

    [Fact]
    public async Task CustoPorKm_veiculo_total_segue_filtros_km_e_base_historica()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync();
        var carro = await CreateAssetAsync(client, ct, "Corsa 2012");

        await PostTemplateAsync(client, carro, 8000, ct);
        // DENTRO da janela: odômetro 12000, custo 600 — auto-advance → atual 12000
        await PostServiceAsync(client, carro, new DateOnly(2026, 7, 10), 600m, ct, odometro: 12000);
        // FORA da janela: odômetro 5000, custo 400 (o MENOR conhecido — prova que a
        // base do kmRodados é histórica e ignora from/to)
        await PostServiceAsync(client, carro, new DateOnly(2026, 1, 15), 400m, ct, odometro: 5000);
        await client.PostAsJsonAsync($"/assets/{carro}/odometer", new { odometer = 20000 }, ct);

        var filtrado = await GetReportAsync(client, ct,
            $"assetId={carro}&from=2026-06-01&to=2026-09-30");

        Assert.NotNull(filtrado.CustoPorKm);
        Assert.Equal(600m, filtrado.CustoPorKm!.Total);    // só o serviço dentro da janela
        Assert.Equal(15000, filtrado.CustoPorKm.KmRodados); // 20000 − min(5000 fora da janela, 8000, 12000)
        Assert.Equal(0.04m, filtrado.CustoPorKm.PorKm);     // 600/15000

        var aberto = await GetReportAsync(client, ct, $"assetId={carro}");
        Assert.Equal(1000m, aberto.CustoPorKm!.Total);      // sem filtro: 600 + 400
        Assert.Equal(15000, aberto.CustoPorKm.KmRodados);
        Assert.Equal(0.067m, aberto.CustoPorKm.PorKm);      // 1000/15000 = 0,0666… → round 3
    }

    [Fact]
    public async Task CustoPorKm_baseline_de_template_e_candidato_e_servico_sem_odometro_nao()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync();
        var carro = await CreateAssetAsync(client, ct, "Corsa 2012", odometroAtual: 20000);

        // baseline 5000 é o ÚNICO primeiro conhecido: serviço não traz odômetro
        await PostTemplateAsync(client, carro, 5000, ct);
        await PostServiceAsync(client, carro, new DateOnly(2026, 3, 10), 300m, ct); // odômetro null

        var rel = await GetReportAsync(client, ct, $"assetId={carro}");

        Assert.NotNull(rel.CustoPorKm);
        Assert.Equal(300m, rel.CustoPorKm!.Total);
        Assert.Equal(15000, rel.CustoPorKm.KmRodados); // 20000 − baseline 5000
        Assert.Equal(0.02m, rel.CustoPorKm.PorKm);      // 300/15000
    }

    [Fact]
    public async Task CustoPorKm_sem_odometro_inicial_ou_atual_km_zero_porKm_null()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync();

        // atual conhecido, mas nenhum baseline/serviço com odômetro > 0
        var semInicial = await CreateAssetAsync(client, ct, "Corsa 2012", odometroAtual: 20000);
        await PostServiceAsync(client, semInicial, new DateOnly(2026, 3, 10), 250m, ct);
        var rel = await GetReportAsync(client, ct, $"assetId={semInicial}");
        Assert.NotNull(rel.CustoPorKm);
        Assert.Equal(250m, rel.CustoPorKm!.Total);
        Assert.Equal(0, rel.CustoPorKm.KmRodados);
        Assert.Null(rel.CustoPorKm.PorKm);

        // odômetro atual nunca registrado (baseline existe, mas atual é null)
        var semAtual = await CreateAssetAsync(client, ct, "Kombi");
        await PostTemplateAsync(client, semAtual, 5000, ct);
        await PostServiceAsync(client, semAtual, new DateOnly(2026, 3, 11), 100m, ct);
        var relB = await GetReportAsync(client, ct, $"assetId={semAtual}");
        Assert.Equal(100m, relB.CustoPorKm!.Total);
        Assert.Equal(0, relB.CustoPorKm.KmRodados);
        Assert.Null(relB.CustoPorKm.PorKm);

        // km zero: primeiro conhecido == atual (não andou desde a base)
        var parado = await CreateAssetAsync(client, ct, "Fusca");
        await PostServiceAsync(client, parado, new DateOnly(2026, 3, 12), 90m, ct, odometro: 5000);
        var relC = await GetReportAsync(client, ct, $"assetId={parado}");
        Assert.Equal(90m, relC.CustoPorKm!.Total);
        Assert.Equal(0, relC.CustoPorKm.KmRodados); // 5000 − 5000
        Assert.Null(relC.CustoPorKm.PorKm);
    }

    [Fact]
    public async Task CustoPorKm_asset_nao_veiculo_null()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync();
        var casa = await CreateAssetAsync(client, ct, "Apartamento", tipo: "casa");
        await PostServiceAsync(client, casa, new DateOnly(2026, 4, 1), 434.46m, ct);

        var rel = await GetReportAsync(client, ct, $"assetId={casa}");

        Assert.Equal(434.46m, rel.Total); // relatório em si segue normal
        Assert.Null(rel.CustoPorKm);
    }

    [Fact]
    public async Task CustoPorKm_sem_assetId_null()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync();
        var carro = await CreateAssetAsync(client, ct, "Corsa 2012", odometroAtual: 20000);
        await PostTemplateAsync(client, carro, 5000, ct);
        await PostServiceAsync(client, carro, new DateOnly(2026, 3, 10), 300m, ct, odometro: 12000);

        var rel = await GetReportAsync(client, ct); // sem assetId

        Assert.Single(rel.PorAsset);
        Assert.Null(rel.CustoPorKm);
    }

    [Fact]
    public async Task CustoPorKm_assetId_alheio_null_e_resposta_vazia()
    {
        var ct = TestContext.Current.CancellationToken;
        var (alice, _) = await fixture.CreateAuthenticatedClientAsync();
        var (bob, _) = await fixture.CreateAuthenticatedClientAsync();
        var doBob = await CreateAssetAsync(bob, ct, "Corsa do Bob", odometroAtual: 20000);
        await PostTemplateAsync(bob, doBob, 5000, ct);
        await PostServiceAsync(bob, doBob, new DateOnly(2026, 3, 10), 300m, ct, odometro: 12000);

        // asset de bob visto pela alice: invisível → custoPorKm null + vazio como hoje
        var rel = await GetReportAsync(alice, ct, $"assetId={doBob}");

        Assert.Equal(0m, rel.Total);
        Assert.Empty(rel.PorAsset);
        Assert.Null(rel.CustoPorKm);
    }
}

public sealed record CostReportResponse(
    decimal Total,
    List<CostByAssetResponse> PorAsset,
    DateOnly? De,
    DateOnly? Ate,
    List<CostByMonthResponse>? PorMes = null,
    CustoPorKmResponse? CustoPorKm = null);

public sealed record CustoPorKmResponse(
    decimal Total,
    int KmRodados,
    decimal? PorKm);

public sealed record CostByMonthResponse(
    string Mes,
    decimal Total,
    int Quantidade);

public sealed record CostByAssetResponse(
    Guid AssetId,
    string Nome,
    decimal Total,
    int Quantidade);
