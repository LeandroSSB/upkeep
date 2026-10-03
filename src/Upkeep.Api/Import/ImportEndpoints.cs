using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Upkeep;
using Upkeep.Api.Assets;
using Upkeep.Api.Auth;
using Upkeep.Infrastructure;

namespace Upkeep.Api.Import;

public static class ImportEndpoints
{
    /// <summary>Proteção de abuse: arquivos distróicos não chegam ao banco.</summary>
    private const int MaxAssets = 200;

    // Espelham os HasMaxLength do UpkeepDbContext — violar aqui seria 500 do
    // banco, não 400 (validação de paridade com os creates da API).
    private const int MaxNome = 200;
    private const int MaxCategoria = 64;
    private const int MaxNotas = 2000;

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapImportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/import").WithTags("Import").RequireAuthorization();

        // Corpo lido CRU (JsonDocument) em vez de DTO vinculado: distinguir "assets
        // ausente/não-array" de JSON malformado é parte do contrato ("Arquivo inválido"),
        // e a validação por asset precisa do índice para a mensagem de erro.
        group.MapPost("", async (
            HttpContext http, UpkeepDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            JsonDocument doc;
            try
            {
                doc = await JsonDocument.ParseAsync(http.Request.Body, cancellationToken: ct);
            }
            catch (JsonException)
            {
                return ArquivoInvalido();
            }

            using (doc)
            {
                return await ImportarAsync(doc.RootElement, db, user.GetUserId(), ct);
            }
        })
            .WithSummary("Importa (ADITIVO) um arquivo de export: ids novos, dono = usuário atual");

        return app;
    }

    /// <summary>Valida o grafo (400 index-based) e materializa as entidades — um só SaveChanges.</summary>
    private static async Task<IResult> ImportarAsync(
        JsonElement root, UpkeepDbContext db, Guid userId, CancellationToken ct)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("assets", out var assetsEl)
            || assetsEl.ValueKind != JsonValueKind.Array)
            return ArquivoInvalido();

        if (assetsEl.GetArrayLength() > MaxAssets)
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                title: $"Importação acima do limite: {assetsEl.GetArrayLength()} assets (máximo {MaxAssets})",
                detail: "Divida o arquivo ou importe menos ativos por vez.");

        var now = DateTime.UtcNow;
        var hoje = DateOnly.FromDateTime(now);
        var (nAssets, nTemplates, nServices) = (0, 0, 0);

        foreach (var (el, i) in assetsEl.EnumerateArray().Select((el, i) => (el, i)))
        {
            ImportAsset? parsed;
            try
            {
                parsed = el.Deserialize<ImportAsset>(WebJson);
            }
            catch (JsonException)
            {
                return AssetInvalido(i, "campo com tipo incompatível (esperado número/string)");
            }
            if (parsed is null)
                return AssetInvalido(i, "entrada não é um objeto");
            var asset = parsed;

            // estrito no essencial: tipo válido, datas parseable, custo >= 0
            if (!AssetTipoApi.TryParse(asset.Tipo, out var tipo))
                return AssetInvalido(i,
                    $"tipo '{asset.Tipo ?? "ausente"}' inválido (use 'veiculo', 'casa' ou 'aparelho')");

            // paridade com os validadores de create (só alcançável com arquivo
            // forjado, mas o import não pode persistir o que o POST normal
            // recusaria, nem estourar length de coluna — que seria 500 opaco)
            if (string.IsNullOrWhiteSpace(asset.Nome))
                return AssetInvalido(i, "nome é obrigatório (sem apenas espaços)");
            if (asset.Nome.Length > MaxNome)
                return AssetInvalido(i, $"nome não pode passar de {MaxNome} caracteres ({asset.Nome.Length})");
            if (asset.Notas?.Length > MaxNotas)
                return AssetInvalido(i, $"notas não pode passar de {MaxNotas} caracteres ({asset.Notas.Length})");
            if (asset.OdometroAtual is < 0)
                return AssetInvalido(i, $"odometroAtual não pode ser negativo ({asset.OdometroAtual})");
            if (asset.OdometroAtual is not null && tipo != AssetTipo.Veiculo)
                return AssetInvalido(i, "odômetro só é permitido para veículos");

            foreach (var (tpl, j) in (asset.Templates ?? []).Select((t, j) => (t, j)))
            {
                if (tpl.BaselineData is { } baseline && !DataValida(baseline))
                    return AssetInvalido(i,
                        $"templates[{j}].baselineData '{baseline}' não é uma data válida (yyyy-MM-dd)");
                if (string.IsNullOrWhiteSpace(tpl.Titulo))
                    return AssetInvalido(i, $"templates[{j}].titulo é obrigatório (sem apenas espaços)");
                if (tpl.Titulo.Length > MaxNome)
                    return AssetInvalido(i,
                        $"templates[{j}].titulo não pode passar de {MaxNome} caracteres ({tpl.Titulo.Length})");
                if (tpl.Categoria?.Length > MaxCategoria)
                    return AssetInvalido(i,
                        $"templates[{j}].categoria não pode passar de {MaxCategoria} caracteres ({tpl.Categoria.Length})");
                if (tpl.IntervaloKm is <= 0)
                    return AssetInvalido(i, $"templates[{j}].intervaloKm deve ser maior que zero ({tpl.IntervaloKm})");
                if (tpl.IntervaloMeses is <= 0)
                    return AssetInvalido(i, $"templates[{j}].intervaloMeses deve ser maior que zero ({tpl.IntervaloMeses})");
                if (tpl.IntervaloKm is null && tpl.IntervaloMeses is null)
                    return AssetInvalido(i, $"templates[{j}]: informe intervaloKm e/ou intervaloMeses");
                if (tpl.CustoEstimado is < 0)
                    return AssetInvalido(i,
                        $"templates[{j}].custoEstimado não pode ser negativo ({tpl.CustoEstimado})");
                if (tpl.BaselineOdometro is < 0)
                    return AssetInvalido(i,
                        $"templates[{j}].baselineOdometro não pode ser negativo ({tpl.BaselineOdometro})");
                if (tpl.BaselineOdometro is not null && tpl.IntervaloKm is null)
                    return AssetInvalido(i,
                        $"templates[{j}].baselineOdometro só é permitido quando intervaloKm é informado");
            }

            var datasServicos = new DateOnly?[asset.Services?.Count ?? 0];
            foreach (var (svc, j) in (asset.Services ?? []).Select((s, j) => (s, j)))
            {
                if (svc.Data is not { } dataStr || !DateOnly.TryParse(dataStr,
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var data))
                    return AssetInvalido(i,
                        $"services[{j}].data '{svc.Data ?? "ausente"}' inválida (yyyy-MM-dd)");
                if (data == DateOnly.MinValue) // sentinel do bind: campo ausente no DTO não-nulo
                    return AssetInvalido(i, $"services[{j}].data é obrigatória (não pode ser 0001-01-01)");
                if (data > hoje.AddDays(1)) // mesma régua do validador de create (hoje+1)
                    return AssetInvalido(i,
                        $"services[{j}].data '{data.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}' não pode ser futura");
                datasServicos[j] = data;
                if (svc.Custo is < 0)
                    return AssetInvalido(i, $"services[{j}].custo não pode ser negativo ({svc.Custo})");
                if (svc.Odometro is < 0)
                    return AssetInvalido(i, $"services[{j}].odometro não pode ser negativo ({svc.Odometro})");
                if (svc.Odometro is not null && tipo != AssetTipo.Veiculo)
                    return AssetInvalido(i, $"services[{j}].odometro só é permitido para veículos");
                if (svc.Notas?.Length > MaxNotas)
                    return AssetInvalido(i,
                        $"services[{j}].notas não pode passar de {MaxNotas} caracteres ({svc.Notas.Length})");
            }

            // importação propriamente: ids NOVOS, dono é o usuário ATUAL (CreatedAt = agora)
            var assetId = Guid.NewGuid();
            db.Assets.Add(new Asset
            {
                Id = assetId,
                UserId = userId,
                Nome = (asset.Nome ?? "").Trim(),
                Tipo = tipo,
                OdometroAtual = asset.OdometroAtual,
                Notas = asset.Notas,
                CreatedAt = now
            });
            nAssets++;

            // remapeio templateId→novo id por asset: service da cópia aponta pro template
            // da PRÓPRIA cópia; referência a template fora do asset → serviço avulso
            var novoTemplateId = new Dictionary<Guid, Guid>();
            foreach (var tpl in asset.Templates ?? [])
            {
                var templateId = Guid.NewGuid();
                if (tpl.Id is { } antigo)
                    novoTemplateId[antigo] = templateId;
                db.Templates.Add(new MaintenanceTemplate
                {
                    Id = templateId,
                    AssetId = assetId,
                    Titulo = (tpl.Titulo ?? "").Trim(),
                    Categoria = tpl.Categoria,
                    IntervaloKm = tpl.IntervaloKm,
                    IntervaloMeses = tpl.IntervaloMeses,
                    CustoEstimado = tpl.CustoEstimado,
                    BaselineOdometro = tpl.BaselineOdometro,
                    BaselineData = DateOnly.TryParse(tpl.BaselineData,
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var b)
                        ? b
                        : hoje, // ausente → hoje (mesma semântica do POST de template)
                });
                nTemplates++;
            }

            foreach (var (svc, j) in (asset.Services ?? []).Select((s, j) => (s, j)))
            {
                db.Services.Add(new ServiceRecord
                {
                    Id = Guid.NewGuid(),
                    AssetId = assetId,
                    TemplateId = svc.TemplateId is { } antigo
                        && novoTemplateId.TryGetValue(antigo, out var novo) ? novo : null,
                    Data = datasServicos[j]!.Value,
                    Odometro = svc.Odometro,
                    Custo = svc.Custo ?? 0m,
                    Notas = svc.Notas,
                    CreatedAt = now
                });
                nServices++;
            }
        }

        // um único SaveChanges = uma transação: falha no meio não importa nada
        await db.SaveChangesAsync(ct);
        return Results.Json(new { importados = new ImportadosResult(nAssets, nTemplates, nServices) },
            statusCode: StatusCodes.Status201Created);
    }

    private static bool DataValida(string valor) => DateOnly.TryParse(valor,
        CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    private static IResult ArquivoInvalido() => Results.Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "Arquivo inválido",
        detail: "Envie o JSON de export com a propriedade 'assets' (array).");

    private static IResult AssetInvalido(int i, string motivo) => Results.Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: $"Asset {i}: {motivo}");
}
