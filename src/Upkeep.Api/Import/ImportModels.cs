namespace Upkeep.Api.Import;

// DTOs de ENTRADA do POST /import — espelham o shape do export (M6), porém tolerantes:
// campos extras são ignorados (default do System.Text.Json) e opcionais ausentes → null.
// `user` e `exportadoEm` do arquivo são deliberadamente ignorados (dados do exportador).
// Datas chegam como string para validar em português com o índice do asset; o `id`
// antigo do template só interessa para remapear templateId dos serviços.

public sealed record ImportAsset(
    string? Nome,
    string? Tipo,
    int? OdometroAtual,
    string? Notas,
    List<ImportTemplate>? Templates,
    List<ImportService>? Services);

public sealed record ImportTemplate(
    Guid? Id,
    string? Titulo,
    string? Categoria,
    int? IntervaloKm,
    int? IntervaloMeses,
    decimal? CustoEstimado,
    int? BaselineOdometro,
    string? BaselineData);

public sealed record ImportService(
    Guid? TemplateId,
    string? Data,
    int? Odometro,
    decimal? Custo,
    string? Notas);

/// <summary>Corpo 201 do /import: `{ "importados": { "assets", "templates", "services" } }`.</summary>
public sealed record ImportadosResult(int Assets, int Templates, int Services);
