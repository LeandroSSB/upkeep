using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Upkeep.IntegrationTests;

[Collection("ApiTests")]
public class AssetTests(ApiFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Create_retorna_201_com_id_tipo_lowercase_e_statusAgregado_nulo()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync();

        var resp = await client.PostAsJsonAsync("/assets",
            new { nome = "Gol 1.0", tipo = "veiculo", odometroAtual = 50_000, notas = (string?)null }, ct);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        var raw = await resp.Content.ReadAsStringAsync(ct);
        Assert.Contains("statusAgregado", raw); // chave presente mesmo nula (placeholder Task 10)
        var body = await resp.Content.ReadFromJsonAsync<AssetResponse>(Json, ct);
        Assert.NotEqual(Guid.Empty, body!.Id);
        Assert.Equal("Gol 1.0", body.Nome);
        Assert.Equal("veiculo", body.Tipo); // round-trip como string portuguesa lowercase
        Assert.Equal(50_000, body.OdometroAtual);
        Assert.Null(body.Notas);
        Assert.Null(body.StatusAgregado);

        // tipo case-insensitive ("VEICULO" também aceita)
        var upper = await client.PostAsJsonAsync("/assets",
            new { nome = "Fiesta", tipo = "VEICULO" }, ct);
        Assert.Equal(HttpStatusCode.Created, upper.StatusCode);
    }

    [Fact]
    public async Task Listar_retorna_somente_assets_do_proprio_usuario()
    {
        var ct = TestContext.Current.CancellationToken;
        var (alice, _) = await fixture.CreateAuthenticatedClientAsync();
        var (bob, _) = await fixture.CreateAuthenticatedClientAsync();

        var a1 = await (await alice.PostAsJsonAsync("/assets", new { nome = "Carro", tipo = "veiculo" }, ct))
            .Content.ReadFromJsonAsync<AssetResponse>(Json, ct);
        var a2 = await (await alice.PostAsJsonAsync("/assets", new { nome = "Casa", tipo = "casa" }, ct))
            .Content.ReadFromJsonAsync<AssetResponse>(Json, ct);
        await bob.PostAsJsonAsync("/assets", new { nome = "Do Bob", tipo = "aparelho" }, ct);

        var resp = await alice.GetAsync("/assets", ct);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var list = await resp.Content.ReadFromJsonAsync<List<AssetResponse>>(Json, ct);
        var ids = list!.Select(x => x.Id).ToHashSet();
        Assert.Equal(2, list!.Count);
        Assert.Contains(a1!.Id, ids);
        Assert.Contains(a2!.Id, ids);
    }

    [Fact]
    public async Task Put_renomeia_e_retorna_dto_atualizado()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync();
        var created = await (await client.PostAsJsonAsync("/assets",
            new { nome = "Nome Antigo", tipo = "casa" }, ct))
            .Content.ReadFromJsonAsync<AssetResponse>(Json, ct);

        var resp = await client.PutAsJsonAsync($"/assets/{created!.Id}",
            new { nome = "Nome Novo", notas = "reforma na cozinha" }, ct);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<AssetResponse>(Json, ct);
        Assert.Equal("Nome Novo", body!.Nome);
        Assert.Equal("reforma na cozinha", body.Notas);
        Assert.Equal("casa", body.Tipo);

        var list = await (await client.GetAsync("/assets", ct))
            .Content.ReadFromJsonAsync<List<AssetResponse>>(Json, ct);
        Assert.Equal("Nome Novo", list!.Single(x => x.Id == created.Id).Nome);
    }

    [Fact]
    public async Task Delete_retorna_204_e_depois_lista_vazia()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync();
        var created = await (await client.PostAsJsonAsync("/assets",
            new { nome = "Purificador", tipo = "aparelho" }, ct))
            .Content.ReadFromJsonAsync<AssetResponse>(Json, ct);

        var del = await client.DeleteAsync($"/assets/{created!.Id}", ct);
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        var list = await (await client.GetAsync("/assets", ct))
            .Content.ReadFromJsonAsync<List<AssetResponse>>(Json, ct);
        Assert.Empty(list!);
    }

    [Fact]
    public async Task Cross_user_put_delete_e_odometer_retornam_404()
    {
        var ct = TestContext.Current.CancellationToken;
        var (alice, _) = await fixture.CreateAuthenticatedClientAsync();
        var (bob, _) = await fixture.CreateAuthenticatedClientAsync();
        var created = await (await alice.PostAsJsonAsync("/assets",
            new { nome = "Civic", tipo = "veiculo", odometroAtual = 10_000 }, ct))
            .Content.ReadFromJsonAsync<AssetResponse>(Json, ct);

        var put = await bob.PutAsJsonAsync($"/assets/{created!.Id}", new { nome = "Hackeado" }, ct);
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);

        var odo = await bob.PostAsJsonAsync($"/assets/{created.Id}/odometer", new { odometer = 99_999 }, ct);
        Assert.Equal(HttpStatusCode.NotFound, odo.StatusCode);

        var del = await bob.DeleteAsync($"/assets/{created.Id}", ct);
        Assert.Equal(HttpStatusCode.NotFound, del.StatusCode);

        // asset segue intacto para a dona
        var list = await (await alice.GetAsync("/assets", ct))
            .Content.ReadFromJsonAsync<List<AssetResponse>>(Json, ct);
        Assert.Equal(10_000, list!.Single(x => x.Id == created.Id).OdometroAtual);
    }

    [Fact]
    public async Task Odometer_em_casa_400_e_regras_de_tipo_e_criacao()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync();

        // odometer via endpoint só para veiculo
        var casa = await (await client.PostAsJsonAsync("/assets",
            new { nome = "Apartamento", tipo = "casa" }, ct))
            .Content.ReadFromJsonAsync<AssetResponse>(Json, ct);
        var odo = await client.PostAsJsonAsync($"/assets/{casa!.Id}/odometer", new { odometer = 100 }, ct);
        Assert.Equal(HttpStatusCode.BadRequest, odo.StatusCode);

        // odometroAtual na criação também é exclusivo de veiculo
        var criaCasaComOdometro = await client.PostAsJsonAsync("/assets",
            new { nome = "Sobrado", tipo = "casa", odometroAtual = 5 }, ct);
        Assert.Equal(HttpStatusCode.BadRequest, criaCasaComOdometro.StatusCode);

        // tipo inválido → 400
        var tipoInvalido = await client.PostAsJsonAsync("/assets",
            new { nome = "Barco", tipo = "barco" }, ct);
        Assert.Equal(HttpStatusCode.BadRequest, tipoInvalido.StatusCode);

        // odometer negativo → 400
        var negativo = await client.PostAsJsonAsync("/assets",
            new { nome = "Kombi", tipo = "veiculo", odometroAtual = -1 }, ct);
        Assert.Equal(HttpStatusCode.BadRequest, negativo.StatusCode);
    }

    [Fact]
    public async Task Odometer_menor_que_atual_400()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync();
        var created = await (await client.PostAsJsonAsync("/assets",
            new { nome = "Onix", tipo = "veiculo", odometroAtual = 50_000 }, ct))
            .Content.ReadFromJsonAsync<AssetResponse>(Json, ct);

        var resp = await client.PostAsJsonAsync($"/assets/{created!.Id}/odometer",
            new { odometer = 40_000 }, ct);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);

        var list = await (await client.GetAsync("/assets", ct))
            .Content.ReadFromJsonAsync<List<AssetResponse>>(Json, ct);
        Assert.Equal(50_000, list!.Single(x => x.Id == created.Id).OdometroAtual); // não voltou
    }

    [Fact]
    public async Task Odometer_atualiza_200_com_novo_valor()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync();
        var created = await (await client.PostAsJsonAsync("/assets",
            new { nome = "Corolla", tipo = "veiculo" }, ct))
            .Content.ReadFromJsonAsync<AssetResponse>(Json, ct);

        var resp = await client.PostAsJsonAsync($"/assets/{created!.Id}/odometer",
            new { odometer = 60_000 }, ct);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<AssetResponse>(Json, ct);
        Assert.Equal(60_000, body!.OdometroAtual);
        Assert.Equal("Corolla", body.Nome);
        Assert.Equal("veiculo", body.Tipo);
    }

    [Fact]
    public async Task Sem_token_todos_endpoints_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var anon = fixture.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anon.PostAsJsonAsync("/assets", new { nome = "X", tipo = "veiculo" }, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/assets", ct)).StatusCode);
        var id = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anon.PutAsJsonAsync($"/assets/{id}", new { nome = "X" }, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.DeleteAsync($"/assets/{id}", ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anon.PostAsJsonAsync($"/assets/{id}/odometer", new { odometer = 1 }, ct)).StatusCode);
    }
}

public sealed record AssetResponse(
    Guid Id,
    string Nome,
    string Tipo,
    int? OdometroAtual,
    string? Notas,
    string? StatusAgregado,
    int? Overdue,
    int? DueSoon,
    int? Ok);
