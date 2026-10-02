// UnreachableException: no .NET 10 mora em System.Diagnostics (era CodeAnalysis até o 9)
using System.Diagnostics;
using Upkeep;

namespace Upkeep.Api.Assets;

public sealed record CreateAssetRequest(string Nome, string Tipo, int? OdometroAtual, string? Notas);
public sealed record UpdateAssetRequest(string Nome, string? Notas);
public sealed record UpdateOdometerRequest(int Odometer);

/// <summary>
/// Resposta de asset. statusAgregado/overdue/dueSoon/ok são preenchidos pelo IStatusService
/// no GET /assets (Task 10); demais endpoints seguem com as chaves presentes e null.
/// </summary>
public sealed record AssetResponse(
    Guid Id,
    string Nome,
    string Tipo,
    int? OdometroAtual,
    string? Notas,
    string? StatusAgregado,
    int? Overdue,
    int? DueSoon,
    int? Ok)
{
    public static AssetResponse From(Asset a, AssetStatusDto? status = null) =>
        new(a.Id, a.Nome, a.Tipo.ToApiString(), a.OdometroAtual, a.Notas,
            status?.Status, status?.Overdue, status?.DueSoon, status?.Ok);
}

public static class AssetTipoApi
{
    /// <summary>"veiculo"|"casa"|"aparelho", case-insensitive.</summary>
    public static bool TryParse(string? valor, out AssetTipo tipo)
    {
        switch (valor?.Trim().ToLowerInvariant())
        {
            case "veiculo": tipo = AssetTipo.Veiculo; return true;
            case "casa": tipo = AssetTipo.Casa; return true;
            case "aparelho": tipo = AssetTipo.Aparelho; return true;
            default: tipo = default; return false;
        }
    }

    /// <summary>
    /// Serializa o enum como string portuguesa lowercase (contrato da API).
    /// Switch exaustivo sem default silencioso: valor fora do enum vindo do banco
    /// (impossível hoje — coluna é int com check de EF) estoura em vez de virar "aparelho".
    /// </summary>
    public static string ToApiString(this AssetTipo tipo) => tipo switch
    {
        AssetTipo.Veiculo => "veiculo",
        AssetTipo.Casa => "casa",
        AssetTipo.Aparelho => "aparelho",
        _ => throw new UnreachableException()
    };
}
