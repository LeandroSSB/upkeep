using FluentValidation;

namespace Upkeep.Api.Templates;

/// <summary>Contrato comum dos DTOs de create/update de template (mesma shape).</summary>
public interface ITemplateRequest
{
    string Titulo { get; }
    string? Categoria { get; }
    int? IntervaloKm { get; }
    int? IntervaloMeses { get; }
    decimal? CustoEstimado { get; }
    int? BaselineOdometro { get; }
    DateOnly? BaselineData { get; }
}

/// <summary>Regras compartilhadas por Create/Update (shapes idênticas — DRY).</summary>
internal static class TemplateRequestRules
{
    public static void Apply<T>(AbstractValidator<T> v) where T : ITemplateRequest
    {
        v.RuleFor(x => x.Titulo)
            .Must(t => !string.IsNullOrWhiteSpace(t)).WithMessage("Título é obrigatório (sem apenas espaços)")
            .MaximumLength(200);
        v.RuleFor(x => x.Categoria).MaximumLength(64);
        v.RuleFor(x => x.IntervaloKm).GreaterThan(0).When(x => x.IntervaloKm.HasValue);
        v.RuleFor(x => x.IntervaloMeses).GreaterThan(0).When(x => x.IntervaloMeses.HasValue);
        v.RuleFor(x => x.CustoEstimado).GreaterThanOrEqualTo(0).When(x => x.CustoEstimado.HasValue);
        v.RuleFor(x => x.BaselineOdometro).GreaterThanOrEqualTo(0).When(x => x.BaselineOdometro.HasValue);

        // pelo menos um intervalo. OverridePropertyName: regra root (RuleFor(x => x))
        // produziria errors com chave "" — inútil pro cliente; keyed no campo faltante
        v.RuleFor(x => x)
            .Must(t => t.IntervaloKm.HasValue || t.IntervaloMeses.HasValue)
            .WithMessage("Informe intervalo_km e/ou intervalo_meses")
            .OverridePropertyName("intervaloKm");

        // baseline de odômetro só faz sentido com intervalo de km
        v.RuleFor(x => x)
            .Must(t => !t.BaselineOdometro.HasValue || t.IntervaloKm.HasValue)
            .WithMessage("Baseline de odômetro só é permitido quando intervalo_km é informado")
            .OverridePropertyName("intervaloKm");
    }
}

public sealed class CreateTemplateRequestValidator : AbstractValidator<CreateTemplateRequest>
{
    public CreateTemplateRequestValidator() => TemplateRequestRules.Apply(this);
}

public sealed class UpdateTemplateRequestValidator : AbstractValidator<UpdateTemplateRequest>
{
    public UpdateTemplateRequestValidator() => TemplateRequestRules.Apply(this);
}
