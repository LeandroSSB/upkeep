using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Upkeep.IntegrationTests;

/// <summary>
/// Status agregado (Task 10): DueCalculator integrado ao GET /assets
/// (statusAgregado + contagens overdue/dueSoon/ok) e ao GET /assets/{id}/templates
/// (status/kmRemaining/dateDue reais em vez de placeholders nulos).
/// </summary>
[Collection("ApiTests")]
public class StatusTests(ApiFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static async Task<Guid> CreateAssetAsync(HttpClient client, CancellationToken ct,
        string tipo = "veiculo", int? odometroAtual = null)
    {
        var nome = "Asset " + Guid.NewGuid().ToString("N")[..8];
        return (await (await client.PostAsJsonAsync("/assets", new { nome, tipo, odometroAtual }, ct))
            .Content.ReadFromJsonAsync<AssetResponse>(Json, ct))!.Id;
    }

    private static async Task<Guid> CreateTemplateAsync(HttpClient client, Guid assetId,
        CancellationToken ct, int? intervaloKm = 10_000, int? intervaloMeses = 12,
        int? baselineOdometro = 50_000, DateOnly? baselineData = null)
    {
        var data = baselineData ?? DateOnly.FromDateTime(DateTime.UtcNow);
        return (await (await client.PostAsJsonAsync($"/assets/{assetId}/templates",
            new { titulo = "Troca de óleo", intervaloKm, intervaloMeses,
                  baselineOdometro, baselineData = data }, ct))
            .Content.ReadFromJsonAsync<TemplateResponse>(Json, ct))!.Id;
    }

    private static Task<HttpResponseMessage> PostOdometerAsync(HttpClient client, Guid assetId,
        int odometer, CancellationToken ct) =>
        client.PostAsJsonAsync($"/assets/{assetId}/odometer", new { odometer }, ct);

    private static async Task<AssetResponse> GetAssetAsync(HttpClient client, Guid assetId,
        CancellationToken ct) =>
        (await (await client.GetAsync("/assets", ct))
            .Content.ReadFromJsonAsync<List<AssetResponse>>(Json, ct))!.Single(a => a.Id == assetId);

    private static async Task<TemplateResponse> GetTemplateAsync(HttpClient client, Guid assetId,
        Guid templateId, CancellationToken ct) =>
        (await (await client.GetAsync($"/assets/{assetId}/templates", ct))
            .Content.ReadFromJsonAsync<List<TemplateResponse>>(Json, ct))!.Single(t => t.Id == templateId);

    [Fact]
    public async Task Template_ok_com_kmRemaining_dateDue_preenchidos_e_agregado_ok()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync();
        var hoje = DateOnly.FromDateTime(DateTime.UtcNow);
        var assetId = await CreateAssetAsync(client, ct, odometroAtual: 50_000);
        var templateId = await CreateTemplateAsync(client, assetId, ct,
            intervaloKm: 10_000, intervaloMeses: 12, baselineOdometro: 50_000, baselineData: hoje);

        var t = await GetTemplateAsync(client, assetId, templateId, ct);
        Assert.Equal("ok", t.Status);
        Assert.Equal(10_000, t.KmRemaining); // 50.000 + 10.000 − 50.000
        Assert.Equal(hoje.AddMonths(12), t.DateDue);

        var a = await GetAssetAsync(client, assetId, ct);
        Assert.Equal("ok", a.StatusAgregado);
        Assert.Equal(0, a.Overdue);
        Assert.Equal(0, a.DueSoon);
        Assert.Equal(1, a.Ok);
    }

    [Fact]
    public async Task Odometro_alem_do_limite_torna_template_e_agregado_vencidos()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync();
        var hoje = DateOnly.FromDateTime(DateTime.UtcNow);
        var assetId = await CreateAssetAsync(client, ct, odometroAtual: 50_000);
        var templateId = await CreateTemplateAsync(client, assetId, ct,
            intervaloKm: 10_000, intervaloMeses: 12, baselineOdometro: 50_000, baselineData: hoje);

        var odo = await PostOdometerAsync(client, assetId, 61_000, ct);
        Assert.Equal(HttpStatusCode.OK, odo.StatusCode);

        var t = await GetTemplateAsync(client, assetId, templateId, ct);
        Assert.Equal("vencido", t.Status);
        Assert.Equal(-1_000, t.KmRemaining); // 60.000 − 61.000

        var a = await GetAssetAsync(client, assetId, ct);
        Assert.Equal("vencido", a.StatusAgregado);
        Assert.Equal(1, a.Overdue);
        Assert.Equal(0, a.DueSoon);
        Assert.Equal(0, a.Ok);
    }

    [Fact]
    public async Task Servico_lancado_reseta_baseline_e_status_volta_a_ok()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync();
        var hoje = DateOnly.FromDateTime(DateTime.UtcNow);
        var assetId = await CreateAssetAsync(client, ct, odometroAtual: 50_000);
        var templateId = await CreateTemplateAsync(client, assetId, ct,
            intervaloKm: 10_000, intervaloMeses: 12, baselineOdometro: 50_000, baselineData: hoje);
        await PostOdometerAsync(client, assetId, 61_000, ct);
        Assert.Equal("vencido", (await GetTemplateAsync(client, assetId, templateId, ct)).Status);

        // serviço do template com odômetro: baseline passa a ser (65.000, hoje)
        var servico = await client.PostAsJsonAsync($"/assets/{assetId}/services",
            new { templateId, data = hoje, odometro = 65_000, custo = 320m }, ct);
        Assert.Equal(HttpStatusCode.Created, servico.StatusCode); // auto-advance p/ 65.000

        var t = await GetTemplateAsync(client, assetId, templateId, ct);
        Assert.Equal("ok", t.Status);
        Assert.Equal(10_000, t.KmRemaining); // 65.000 + 10.000 − 65.000 (novo baseline)
        Assert.Equal(hoje.AddMonths(12), t.DateDue); // tempo também conta do serviço

        var a = await GetAssetAsync(client, assetId, ct);
        Assert.Equal("ok", a.StatusAgregado);
        Assert.Equal(0, a.Overdue);
        Assert.Equal(0, a.DueSoon);
        Assert.Equal(1, a.Ok);
    }

    [Fact]
    public async Task Template_so_tempo_com_baseline_antigo_fica_vencido()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync();
        var hoje = DateOnly.FromDateTime(DateTime.UtcNow);
        var assetId = await CreateAssetAsync(client, ct, tipo: "casa");
        var baseline = hoje.AddYears(-3); // vencia 1 ano atrás (24 meses)
        var templateId = await CreateTemplateAsync(client, assetId, ct,
            intervaloKm: null, intervaloMeses: 24, baselineOdometro: null, baselineData: baseline);

        var t = await GetTemplateAsync(client, assetId, templateId, ct);
        Assert.Equal("vencido", t.Status);
        Assert.Null(t.KmRemaining); // sem critério km
        Assert.Equal(baseline.AddMonths(24), t.DateDue);

        var a = await GetAssetAsync(client, assetId, ct);
        Assert.Equal("vencido", a.StatusAgregado);
        Assert.Equal(1, a.Overdue);
        Assert.Equal(0, a.DueSoon);
        Assert.Equal(0, a.Ok);
    }

    [Fact]
    public async Task Limiar_faltando_1500km_de_10000_vence_em_breve()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync();
        var hoje = DateOnly.FromDateTime(DateTime.UtcNow);
        var assetId = await CreateAssetAsync(client, ct, odometroAtual: 50_000);
        var templateId = await CreateTemplateAsync(client, assetId, ct,
            intervaloKm: 10_000, intervaloMeses: 12, baselineOdometro: 50_000, baselineData: hoje);
        await PostOdometerAsync(client, assetId, 58_500, ct);

        var t = await GetTemplateAsync(client, assetId, templateId, ct);
        Assert.Equal("vence_em_breve", t.Status); // 1.500 ≤ 20% de 10.000
        Assert.Equal(1_500, t.KmRemaining);
        Assert.Equal(hoje.AddMonths(12), t.DateDue);

        var a = await GetAssetAsync(client, assetId, ct);
        Assert.Equal("vence_em_breve", a.StatusAgregado);
        Assert.Equal(0, a.Overdue);
        Assert.Equal(1, a.DueSoon);
        Assert.Equal(0, a.Ok);
    }

    [Fact]
    public async Task Agregado_misto_vencido_por_km_e_vence_em_breve_por_data()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync();
        var hoje = DateOnly.FromDateTime(DateTime.UtcNow);
        var assetId = await CreateAssetAsync(client, ct, odometroAtual: 61_000);

        // A: vencido por KM (50.000 + 10.000 − 61.000 = −1.000), sem critério tempo
        await CreateTemplateAsync(client, assetId, ct,
            intervaloKm: 10_000, intervaloMeses: null, baselineOdometro: 50_000, baselineData: hoje);
        // B: vence em breve por DATA (due em ~20 dias ≤ 30), sem critério km
        await CreateTemplateAsync(client, assetId, ct,
            intervaloKm: null, intervaloMeses: 12, baselineOdometro: null,
            baselineData: hoje.AddDays(20).AddMonths(-12));

        var a = await GetAssetAsync(client, assetId, ct);
        Assert.Equal("vencido", a.StatusAgregado); // pior status manda no agregado
        Assert.Equal(1, a.Overdue);
        Assert.Equal(1, a.DueSoon);
        Assert.Equal(0, a.Ok);
    }

    [Fact]
    public async Task Asset_sem_templates_agregado_ok_com_contagens_zero()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync();
        var assetId = await CreateAssetAsync(client, ct, tipo: "casa");

        var resp = await client.GetAsync($"/assets/{assetId}/templates", ct);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Empty((await resp.Content.ReadFromJsonAsync<List<TemplateResponse>>(Json, ct))!);

        var a = await GetAssetAsync(client, assetId, ct);
        Assert.Equal("ok", a.StatusAgregado);
        Assert.Equal(0, a.Overdue);
        Assert.Equal(0, a.DueSoon);
        Assert.Equal(0, a.Ok);
    }

    [Fact]
    public async Task Cross_user_get_templates_404_e_lista_nao_expoe_asset_alheio()
    {
        var ct = TestContext.Current.CancellationToken;
        var (alice, _) = await fixture.CreateAuthenticatedClientAsync();
        var (bob, _) = await fixture.CreateAuthenticatedClientAsync();
        var assetId = await CreateAssetAsync(alice, ct, odometroAtual: 50_000);
        var templateId = await CreateTemplateAsync(alice, assetId, ct);

        // asset alheio não vaza — 404, não lista vazia
        var get = await bob.GetAsync($"/assets/{assetId}/templates", ct);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);

        // status de asset alheio nunca aparece na lista do intruso
        var listaBob = await (await bob.GetAsync("/assets", ct))
            .Content.ReadFromJsonAsync<List<AssetResponse>>(Json, ct);
        Assert.DoesNotContain(assetId, listaBob!.Select(x => x.Id));

        // e a dona segue vendo o status normal
        var a = await GetAssetAsync(alice, assetId, ct);
        Assert.Equal("ok", a.StatusAgregado);
        Assert.Equal(1, a.Ok);
        Assert.Equal("ok", (await GetTemplateAsync(alice, assetId, templateId, ct)).Status);
    }

    [Fact]
    public async Task Servico_sem_odometro_reseta_tempo_mas_km_mantem_baseline_do_template()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync();
        var hoje = DateOnly.FromDateTime(DateTime.UtcNow);
        var assetId = await CreateAssetAsync(client, ct, odometroAtual: 52_000);
        // baseline 6 meses atrás: antes do serviço, vence (tempo) em hoje+6m
        var templateId = await CreateTemplateAsync(client, assetId, ct,
            intervaloKm: 10_000, intervaloMeses: 12, baselineOdometro: 50_000,
            baselineData: hoje.AddMonths(-6));

        var antes = await GetTemplateAsync(client, assetId, templateId, ct);
        Assert.Equal("ok", antes.Status);
        Assert.Equal(8_000, antes.KmRemaining);
        Assert.Equal(hoje.AddMonths(6), antes.DateDue);

        // serviço SEM odômetro: só a data vira baseline; km cai de volta ao baseline do template
        var servico = await client.PostAsJsonAsync($"/assets/{assetId}/services",
            new { templateId, data = hoje, custo = 100m }, ct);
        Assert.Equal(HttpStatusCode.Created, servico.StatusCode);

        var t = await GetTemplateAsync(client, assetId, templateId, ct);
        Assert.Equal("ok", t.Status);
        Assert.Equal(8_000, t.KmRemaining); // inalterado: fallback pro BaselineOdometro do template
        Assert.Equal(hoje.AddMonths(12), t.DateDue); // tempo resetado pela data do serviço
    }
}
