namespace Upkeep;

public enum AssetTipo
{
    Veiculo,
    Casa,
    Aparelho
}

/// <summary>Usuário da conta (e-mail + hash da senha).</summary>
public sealed class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public sealed class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Ativo monitorado (veículo, casa, aparelho...).</summary>
public sealed class Asset
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Nome { get; set; } = "";
    public AssetTipo Tipo { get; set; }
    public int? OdometroAtual { get; set; }
    public string? Notas { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Modelo de manutenção recorrente (intervalos de km e/ou meses).</summary>
public sealed class MaintenanceTemplate
{
    public Guid Id { get; set; }
    public Guid AssetId { get; set; }
    public string Titulo { get; set; } = "";
    public string? Categoria { get; set; }
    public int? IntervaloKm { get; set; }
    public int? IntervaloMeses { get; set; }
    public decimal? CustoEstimado { get; set; }
    public int? BaselineOdometro { get; set; }
    public DateOnly BaselineData { get; set; }
}

/// <summary>Serviço/execução registrada num ativo.</summary>
public sealed class ServiceRecord
{
    public Guid Id { get; set; }
    public Guid AssetId { get; set; }
    public Guid? TemplateId { get; set; }
    public DateOnly Data { get; set; }
    public int? Odometro { get; set; }
    public decimal Custo { get; set; }
    public string? Notas { get; set; }
    public DateTime CreatedAt { get; set; }
}
