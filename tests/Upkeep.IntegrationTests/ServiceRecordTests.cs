using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Upkeep.IntegrationTests;

[Collection("ApiTests")]
public class ServiceRecordTests(ApiFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static async Task<Guid> CreateAssetAsync(HttpClient client, CancellationToken ct,
        string tipo = "veiculo", int? odometroAtual = null)
    {
        var nome = "Asset " + Guid.NewGuid().ToString("N")[..8];
        return (await (await client.PostAsJsonAsync("/assets",
            new { nome, tipo, odometroAtual }, ct))
            .Content.ReadFromJsonAsync<AssetResponse>(Json, ct))!.Id;
    }

    private static async Task<Guid> CreateTemplateAsync(HttpClient client, Guid assetId,
        CancellationToken ct) =>
        (await (await client.PostAsJsonAsync($"/assets/{assetId}/templates",
            new { titulo = "Troca de óleo", intervaloKm = 10_000 }, ct))
            .Content.ReadFromJsonAsync<TemplateResponse>(Json, ct))!.Id;

    private static async Task<HttpResponseMessage> PostServiceAsync(HttpClient client, Guid assetId,
        object payload, CancellationToken ct) =>
        await client.PostAsJsonAsync($"/assets/{assetId}/services", payload, ct);

    [Fact]
    public async Task Lancar_servico_201_com_todos_os_campos()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync();
        var assetId = await CreateAssetAsync(client, ct, odometroAtual: 50_000);
        var templateId = await CreateTemplateAsync(client, assetId, ct);

        var resp = await PostServiceAsync(client, assetId,
            new { templateId, data = new DateOnly(2026, 9, 20), odometro = 51_000,
                  custo = 320.50m, notas = "Óleo 5W30 sintético" }, ct);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        Assert.StartsWith($"/assets/{assetId}/services/", resp.Headers.Location?.ToString());

        var body = await resp.Content.ReadFromJsonAsync<ServiceResponse>(Json, ct);
        Assert.NotNull(body);
        Assert.NotEqual(Guid.Empty, body.Id);
        Assert.Equal(assetId, body.AssetId);
        Assert.Equal(templateId, body.TemplateId);
        Assert.Equal(new DateOnly(2026, 9, 20), body.Data);
        Assert.Equal(51_000, body.Odometro);
        Assert.Equal(320.50m, body.Custo);
        Assert.Equal("Óleo 5W30 sintético", body.Notas);
        Assert.True(body.CreatedAt <= DateTime.UtcNow.AddMinutes(1));

        // sem template e sem odometro também é válido (serviço avulso em asset não-veiculo)
        var casaId = await CreateAssetAsync(client, ct, tipo: "casa");
        var avulso = await PostServiceAsync(client, casaId,
            new { data = new DateOnly(2026, 9, 21), custo = 0m }, ct);
        Assert.Equal(HttpStatusCode.Created, avulso.StatusCode);
        var corpo = await avulso.Content.ReadFromJsonAsync<ServiceResponse>(Json, ct);
        Assert.Null(corpo!.TemplateId);
        Assert.Null(corpo.Odometro);
    }

    [Fact]
    public async Task Historico_200_ordenado_data_desc_e_so_do_asset()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync();
        var carro = await CreateAssetAsync(client, ct);
        var outro = await CreateAssetAsync(client, ct);

        await PostServiceAsync(client, carro, new { data = new DateOnly(2026, 1, 10), custo = 100m }, ct);
        await PostServiceAsync(client, carro, new { data = new DateOnly(2026, 3, 5), custo = 200m }, ct);
        await PostServiceAsync(client, carro, new { data = new DateOnly(2026, 2, 15), custo = 150m }, ct);
        // mesmo dia: desempate pelo CreatedAt desc (segundo lançamento criado depois vem primeiro)
        await PostServiceAsync(client, carro, new { data = new DateOnly(2026, 3, 5), custo = 210m }, ct);
        await PostServiceAsync(client, outro, new { data = new DateOnly(2026, 12, 31), custo = 999m }, ct);

        var resp = await client.GetAsync($"/assets/{carro}/services", ct);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var list = await resp.Content.ReadFromJsonAsync<List<ServiceResponse>>(Json, ct);
        Assert.NotNull(list);
        Assert.Equal(4, list!.Count); // serviço do outro asset não vaza
        Assert.All(list, s => Assert.Equal(carro, s.AssetId));
        Assert.Equal(
            [new DateOnly(2026, 3, 5), new DateOnly(2026, 3, 5),
             new DateOnly(2026, 2, 15), new DateOnly(2026, 1, 10)],
            list.Select(s => s.Data).ToArray());
        // desempate: 210 (criado depois) antes de 200 no mesmo dia
        Assert.Equal(210m, list[0].Custo);
        Assert.Equal(200m, list[1].Custo);
    }

    [Fact]
    public async Task Template_inexistente_ou_de_outro_asset_400_problem()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync();
        var asset1 = await CreateAssetAsync(client, ct);
        var asset2 = await CreateAssetAsync(client, ct);
        var templateDoAsset2 = await CreateTemplateAsync(client, asset2, ct);

        // template de outro asset → 400 (asset endereçado existe; não é 404)
        var outro = await PostServiceAsync(client, asset1,
            new { templateId = templateDoAsset2, data = DateOnly.FromDateTime(DateTime.UtcNow), custo = 50m }, ct);
        Assert.Equal(HttpStatusCode.BadRequest, outro.StatusCode);
        Assert.Contains("Template não pertence ao asset", await outro.Content.ReadAsStringAsync(ct));

        // template inexistente → 400 (deve existir E pertencer ao asset)
        var fantasma = await PostServiceAsync(client, asset1,
            new { templateId = Guid.NewGuid(), data = DateOnly.FromDateTime(DateTime.UtcNow), custo = 50m }, ct);
        Assert.Equal(HttpStatusCode.BadRequest, fantasma.StatusCode);
        Assert.Contains("Template não pertence ao asset", await fantasma.Content.ReadAsStringAsync(ct));
    }

    [Fact]
    public async Task Validacoes_400_data_futura_custo_negativo_notas_longas_odometro_negativo()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync();
        var assetId = await CreateAssetAsync(client, ct);

        var hoje = DateOnly.FromDateTime(DateTime.UtcNow);
        var futura = await PostServiceAsync(client, assetId,
            new { data = hoje.AddDays(2), custo = 100m }, ct);
        Assert.Equal(HttpStatusCode.BadRequest, futura.StatusCode);

        var custoNeg = await PostServiceAsync(client, assetId,
            new { data = hoje, custo = -0.01m }, ct);
        Assert.Equal(HttpStatusCode.BadRequest, custoNeg.StatusCode);

        var notasLongas = await PostServiceAsync(client, assetId,
            new { data = hoje, custo = 100m, notas = new string('n', 2001) }, ct);
        Assert.Equal(HttpStatusCode.BadRequest, notasLongas.StatusCode);

        var odoNeg = await PostServiceAsync(client, assetId,
            new { data = hoje, custo = 100m, odometro = -10 }, ct);
        Assert.Equal(HttpStatusCode.BadRequest, odoNeg.StatusCode);
    }

    [Fact]
    public async Task Servico_com_odometro_avanca_odometro_do_asset_sem_voltar()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync();
        var assetId = await CreateAssetAsync(client, ct, odometroAtual: 50_000);

        // avança: 52_300 > 50_000 → asset atualiza na mesma transação
        var avanca = await PostServiceAsync(client, assetId,
            new { data = DateOnly.FromDateTime(DateTime.UtcNow), custo = 300m, odometro = 52_300 }, ct);
        Assert.Equal(HttpStatusCode.Created, avanca.StatusCode);

        var assets = await (await client.GetAsync("/assets", ct))
            .Content.ReadFromJsonAsync<List<AssetResponse>>(Json, ct);
        Assert.Equal(52_300, assets!.Single(a => a.Id == assetId).OdometroAtual);

        // menor que o atual: serviço é registrado (201), mas odômetro do asset não volta
        var volta = await PostServiceAsync(client, assetId,
            new { data = DateOnly.FromDateTime(DateTime.UtcNow), custo = 100m, odometro = 49_000 }, ct);
        Assert.Equal(HttpStatusCode.Created, volta.StatusCode);

        assets = await (await client.GetAsync("/assets", ct))
            .Content.ReadFromJsonAsync<List<AssetResponse>>(Json, ct);
        Assert.Equal(52_300, assets!.Single(a => a.Id == assetId).OdometroAtual);
    }

    [Fact]
    public async Task Odometro_em_asset_nao_veiculo_400()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync();
        var casaId = await CreateAssetAsync(client, ct, tipo: "casa");

        var resp = await PostServiceAsync(client, casaId,
            new { data = DateOnly.FromDateTime(DateTime.UtcNow), custo = 80m, odometro = 100 }, ct);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var raw = await resp.Content.ReadAsStringAsync(ct);
        Assert.Contains("veícul", raw); // mensagem específica de odômetro/veículo
    }

    [Fact]
    public async Task Cross_user_404_e_sem_token_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var (alice, _) = await fixture.CreateAuthenticatedClientAsync();
        var (bob, _) = await fixture.CreateAuthenticatedClientAsync();
        var assetId = await CreateAssetAsync(alice, ct);

        var post = await PostServiceAsync(bob, assetId,
            new { data = DateOnly.FromDateTime(DateTime.UtcNow), custo = 1m }, ct);
        Assert.Equal(HttpStatusCode.NotFound, post.StatusCode);

        var get = await bob.GetAsync($"/assets/{assetId}/services", ct);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);

        var anon = fixture.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anon.GetAsync($"/assets/{assetId}/services", ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anon.PostAsJsonAsync($"/assets/{assetId}/services",
                new { data = DateOnly.FromDateTime(DateTime.UtcNow), custo = 1m }, ct)).StatusCode);

        // nada foi lançado por intrusos
        var historico = await (await alice.GetAsync($"/assets/{assetId}/services", ct))
            .Content.ReadFromJsonAsync<List<ServiceResponse>>(Json, ct);
        Assert.Empty(historico!);
    }

    [Fact]
    public async Task Delete_template_com_servico_vinculado_409()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync();
        var assetId = await CreateAssetAsync(client, ct);
        var templateId = await CreateTemplateAsync(client, assetId, ct);

        var servico = await PostServiceAsync(client, assetId,
            new { templateId, data = DateOnly.FromDateTime(DateTime.UtcNow), custo = 250m }, ct);
        Assert.Equal(HttpStatusCode.Created, servico.StatusCode);

        var del = await client.DeleteAsync($"/templates/{templateId}", ct);
        Assert.Equal(HttpStatusCode.Conflict, del.StatusCode);
        Assert.Contains("Template possui serviços vinculados",
            await del.Content.ReadAsStringAsync(ct));
    }
}

public sealed record ServiceResponse(
    Guid Id,
    Guid AssetId,
    Guid? TemplateId,
    DateOnly Data,
    int? Odometro,
    decimal Custo,
    string? Notas,
    DateTime CreatedAt);
