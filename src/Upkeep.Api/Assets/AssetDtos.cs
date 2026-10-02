using Upkeep;

namespace Upkeep.Api.Assets;

public sealed record CreateAssetRequest(string Nome, string Tipo, int? OdometroAtual, string? Notas);
public sealed record UpdateAssetRequest(string Nome, string? Notas);
public sealed record UpdateOdometerRequest(int Odometer);

/// <summary>Resposta de asset. statusAgregado entra na Task 10 — por ora sempre null (chave presente).</summary>
public sealed record AssetResponse(
    Guid Id,
    string Nome,
    string Tipo,
    int? OdometroAtual,
    string? Notas,
    object? StatusAgregado)
{
    public static AssetResponse From(Asset a) =>
        new(a.Id, a.Nome, a.Tipo.ToApiString(), a.OdometroAtual, a.Notas, StatusAgregado: null);
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

    /// <summary>Serializa o enum como string portuguesa lowercase (contrato da API).</summary>
    public static string ToApiString(this AssetTipo tipo) => tipo switch
    {
        AssetTipo.Veiculo => "veiculo",
        AssetTipo.Casa => "casa",
        _ => "aparelho"
    };
}
