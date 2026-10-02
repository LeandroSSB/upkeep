using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Upkeep.IntegrationTests;

[Collection("ApiTests")]
public class TemplateTests(ApiFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static async Task<Guid> CreateAssetAsync(HttpClient client, CancellationToken ct) =>
        (await (await client.PostAsJsonAsync("/assets",
            new { nome = "Carro de Teste", tipo = "veiculo" }, ct))
            .Content.ReadFromJsonAsync<AssetResponse>(Json, ct))!.Id;

    private static async Task<Guid> CreateTemplateAsync(HttpClient client, Guid assetId,
        CancellationToken ct, string titulo = "Troca de óleo") =>
        (await (await client.PostAsJsonAsync($"/assets/{assetId}/templates",
            new { titulo, categoria = (string?)"Motor", intervaloKm = 10_000, intervaloMeses = (int?)null,
                  custoEstimado = 250.90m, baselineOdometro = 50_000, baselineData = new DateOnly(2026, 1, 5) }, ct))
            .Content.ReadFromJsonAsync<TemplateResponse>(Json, ct))!.Id;

    [Fact]
    public async Task Create_201_com_campos_defaults_e_placeholders_nulos()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync();
        var assetId = await CreateAssetAsync(client, ct);

        var antes = DateOnly.FromDateTime(DateTime.UtcNow);
        var resp = await client.PostAsJsonAsync($"/assets/{assetId}/templates",
            new { titulo = "Troca de óleo", categoria = (string?)"Motor", intervaloKm = 10_000,
                  intervaloMeses = (int?)null, custoEstimado = 250.90m, baselineOdometro = 50_000,
                  baselineData = (DateOnly?)null }, ct);
        var depois = DateOnly.FromDateTime(DateTime.UtcNow);

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        // placeholders Task 10: chaves presentes mesmo nulas
        var raw = await resp.Content.ReadAsStringAsync(ct);
        Assert.Contains("\"status\"", raw);
        Assert.Contains("\"kmRemaining\"", raw);
        Assert.Contains("\"dateDue\"", raw);

        var body = await resp.Content.ReadFromJsonAsync<TemplateResponse>(Json, ct);
        Assert.NotNull(body);
        Assert.NotEqual(Guid.Empty, body.Id);
        Assert.Equal(assetId, body.AssetId);
        Assert.Equal("Troca de óleo", body.Titulo);
        Assert.Equal("Motor", body.Categoria);
        Assert.Equal(10_000, body.IntervaloKm);
        Assert.Null(body.IntervaloMeses);
        Assert.Equal(250.90m, body.CustoEstimado);
        Assert.Equal(50_000, body.BaselineOdometro);
        Assert.True(body.BaselineData >= antes && body.BaselineData <= depois,
            $"baselineData default deveria ser hoje ({antes}–{depois}), veio {body.BaselineData}");
        Assert.Null(body.Status);
        Assert.Null(body.KmRemaining);
        Assert.Null(body.DateDue);
    }

    [Fact]
    public async Task Listar_por_asset_200_so_templates_daquele_asset()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync();
        var carro = await CreateAssetAsync(client, ct);
        var casa = await CreateAssetAsync(client, ct);

        var t1 = await CreateTemplateAsync(client, carro, ct, titulo: "Troca de óleo");
        var t2 = await CreateTemplateAsync(client, carro, ct, titulo: "Revisão freios");
        await CreateTemplateAsync(client, casa, ct, titulo: "Limpeza caixa d'água");

        var resp = await client.GetAsync($"/assets/{carro}/templates", ct);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var list = await resp.Content.ReadFromJsonAsync<List<TemplateResponse>>(Json, ct);
        var ids = list!.Select(t => t.Id).ToHashSet();
        Assert.Equal(2, list.Count);
        Assert.Contains(t1, ids);
        Assert.Contains(t2, ids);
    }

    [Fact]
    public async Task Put_200_substitui_todos_os_campos_e_baseline_data_null_vira_hoje()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync();
        var assetId = await CreateAssetAsync(client, ct);
        var id = await CreateTemplateAsync(client, assetId, ct);

        var antes = DateOnly.FromDateTime(DateTime.UtcNow);
        var resp = await client.PutAsJsonAsync($"/templates/{id}",
            new { titulo = "Revisão anual", categoria = (string?)null, intervaloKm = (int?)null,
                  intervaloMeses = 12, custoEstimado = 800m, baselineOdometro = (int?)null,
                  baselineData = (DateOnly?)null }, ct);
        var depois = DateOnly.FromDateTime(DateTime.UtcNow);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<TemplateResponse>(Json, ct);
        Assert.NotNull(body);
        Assert.Equal("Revisão anual", body.Titulo);
        Assert.Null(body.Categoria);
        Assert.Null(body.IntervaloKm); // substituição total, não merge
        Assert.Equal(12, body.IntervaloMeses);
        Assert.Equal(800m, body.CustoEstimado);
        Assert.Null(body.BaselineOdometro);
        Assert.True(body.BaselineData >= antes && body.BaselineData <= depois,
            $"baselineData null deveria virar hoje ({antes}–{depois}), veio {body.BaselineData}");

        // persistiu
        var list = await (await client.GetAsync($"/assets/{assetId}/templates", ct))
            .Content.ReadFromJsonAsync<List<TemplateResponse>>(Json, ct);
        Assert.Equal("Revisão anual", list!.Single(t => t.Id == id).Titulo);
    }

    [Fact]
    public async Task Delete_204_e_segundo_delete_404()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync();
        var assetId = await CreateAssetAsync(client, ct);
        var id = await CreateTemplateAsync(client, assetId, ct);

        var del = await client.DeleteAsync($"/templates/{id}", ct);
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        var deNovo = await client.DeleteAsync($"/templates/{id}", ct);
        Assert.Equal(HttpStatusCode.NotFound, deNovo.StatusCode);

        var list = await (await client.GetAsync($"/assets/{assetId}/templates", ct))
            .Content.ReadFromJsonAsync<List<TemplateResponse>>(Json, ct);
        Assert.Empty(list!);
    }

    [Fact]
    public async Task Sem_intervalo_nenhum_400_com_mensagem_especifica()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync();
        var assetId = await CreateAssetAsync(client, ct);

        var cria = await client.PostAsJsonAsync($"/assets/{assetId}/templates",
            new { titulo = "Sem intervalo", intervaloKm = (int?)null, intervaloMeses = (int?)null }, ct);
        Assert.Equal(HttpStatusCode.BadRequest, cria.StatusCode);
        var problem = await cria.Content.ReadFromJsonAsync<ValidationProblemBody>(Json, ct);
        Assert.NotNull(problem!.Errors);
        Assert.Contains("Informe intervalo_km e/ou intervalo_meses",
            string.Join("; ", problem.Errors.Values.SelectMany(v => v)));

        // PUT também exige intervalo
        var id = await CreateTemplateAsync(client, assetId, ct);
        var put = await client.PutAsJsonAsync($"/templates/{id}",
            new { titulo = "Sem intervalo", intervaloKm = (int?)null, intervaloMeses = (int?)null }, ct);
        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
    }

    [Fact]
    public async Task Intervalo_zero_ou_negativo_e_limites_400()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync();
        var assetId = await CreateAssetAsync(client, ct);

        var kmZero = await client.PostAsJsonAsync($"/assets/{assetId}/templates",
            new { titulo = "Km zero", intervaloKm = 0, intervaloMeses = 6 }, ct);
        Assert.Equal(HttpStatusCode.BadRequest, kmZero.StatusCode);

        var mesesNeg = await client.PostAsJsonAsync($"/assets/{assetId}/templates",
            new { titulo = "Meses neg", intervaloKm = 5_000, intervaloMeses = -3 }, ct);
        Assert.Equal(HttpStatusCode.BadRequest, mesesNeg.StatusCode);

        var custoNeg = await client.PostAsJsonAsync($"/assets/{assetId}/templates",
            new { titulo = "Custo neg", intervaloKm = 5_000, custoEstimado = -0.01m }, ct);
        Assert.Equal(HttpStatusCode.BadRequest, custoNeg.StatusCode);

        var odoNeg = await client.PostAsJsonAsync($"/assets/{assetId}/templates",
            new { titulo = "Odo neg", intervaloKm = 5_000, baselineOdometro = -10 }, ct);
        Assert.Equal(HttpStatusCode.BadRequest, odoNeg.StatusCode);
    }

    [Fact]
    public async Task BaselineOdometro_sem_intervalo_km_400()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync();
        var assetId = await CreateAssetAsync(client, ct);

        // criação: só meses + baselineOdometro → 400
        var cria = await client.PostAsJsonAsync($"/assets/{assetId}/templates",
            new { titulo = "Só meses", intervaloMeses = 6, baselineOdometro = 10_000 }, ct);
        Assert.Equal(HttpStatusCode.BadRequest, cria.StatusCode);

        // update: tirar o km mantendo baselineOdometro → 400
        var id = await CreateTemplateAsync(client, assetId, ct);
        var put = await client.PutAsJsonAsync($"/templates/{id}",
            new { titulo = "Vira só meses", intervaloMeses = 6, baselineOdometro = 10_000 }, ct);
        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
    }

    [Fact]
    public async Task Cross_user_404_em_get_post_put_delete_template()
    {
        var ct = TestContext.Current.CancellationToken;
        var (alice, _) = await fixture.CreateAuthenticatedClientAsync();
        var (bob, _) = await fixture.CreateAuthenticatedClientAsync();
        var assetId = await CreateAssetAsync(alice, ct);
        var templateId = await CreateTemplateAsync(alice, assetId, ct);

        // asset alheio: não vaza existência (404, não lista vazia)
        var get = await bob.GetAsync($"/assets/{assetId}/templates", ct);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);

        var post = await bob.PostAsJsonAsync($"/assets/{assetId}/templates",
            new { titulo = "Invadindo", intervaloKm = 1_000 }, ct);
        Assert.Equal(HttpStatusCode.NotFound, post.StatusCode);

        // template alheio
        var put = await bob.PutAsJsonAsync($"/templates/{templateId}",
            new { titulo = "Hackeado", intervaloKm = 1 }, ct);
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);

        var del = await bob.DeleteAsync($"/templates/{templateId}", ct);
        Assert.Equal(HttpStatusCode.NotFound, del.StatusCode);

        // segue intacto para a dona
        var list = await (await alice.GetAsync($"/assets/{assetId}/templates", ct))
            .Content.ReadFromJsonAsync<List<TemplateResponse>>(Json, ct);
        Assert.Equal(templateId, list!.Single().Id);
    }

    [Fact]
    public async Task Titulo_vazio_whitespace_ou_oversize_400()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync();
        var assetId = await CreateAssetAsync(client, ct);

        var vazio = await client.PostAsJsonAsync($"/assets/{assetId}/templates",
            new { titulo = "", intervaloKm = 5_000 }, ct);
        Assert.Equal(HttpStatusCode.BadRequest, vazio.StatusCode);

        // só espaços — fix carregado da review da Task 7
        var whitespace = await client.PostAsJsonAsync($"/assets/{assetId}/templates",
            new { titulo = "   ", intervaloKm = 5_000 }, ct);
        Assert.Equal(HttpStatusCode.BadRequest, whitespace.StatusCode);

        var longo = await client.PostAsJsonAsync($"/assets/{assetId}/templates",
            new { titulo = new string('a', 201), intervaloKm = 5_000 }, ct);
        Assert.Equal(HttpStatusCode.BadRequest, longo.StatusCode);

        var categoriaLonga = await client.PostAsJsonAsync($"/assets/{assetId}/templates",
            new { titulo = "Ok", categoria = new string('c', 65), intervaloKm = 5_000 }, ct);
        Assert.Equal(HttpStatusCode.BadRequest, categoriaLonga.StatusCode);
    }
}

public sealed record TemplateResponse(
    Guid Id,
    Guid AssetId,
    string Titulo,
    string? Categoria,
    int? IntervaloKm,
    int? IntervaloMeses,
    decimal? CustoEstimado,
    int? BaselineOdometro,
    DateOnly? BaselineData,
    JsonElement? Status,
    JsonElement? KmRemaining,
    JsonElement? DateDue);

public sealed record ValidationProblemBody(Dictionary<string, string[]>? Errors);
