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

            foreach (var (tpl, j) in (asset.Templates ?? []).Select((t, j) => (t, j)))
                if (tpl.BaselineData is { } baseline && !DataValida(baseline))
                    return AssetInvalido(i,
                        $"templates[{j}].baselineData '{baseline}' não é uma data válida (yyyy-MM-dd)");

            var datasServicos = new DateOnly?[asset.Services?.Count ?? 0];
            foreach (var (svc, j) in (asset.Services ?? []).Select((s, j) => (s, j)))
            {
                if (svc.Data is not { } dataStr || !DateOnly.TryParse(dataStr,
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var data))
                    return AssetInvalido(i,
                        $"services[{j}].data '{svc.Data ?? "ausente"}' inválida (yyyy-MM-dd)");
                datasServicos[j] = data;
                if (svc.Custo is < 0)
                    return AssetInvalido(i, $"services[{j}].custo não pode ser negativo ({svc.Custo})");
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
