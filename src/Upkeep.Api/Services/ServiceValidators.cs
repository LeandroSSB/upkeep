using FluentValidation;

namespace Upkeep.Api.Services;

public sealed class CreateServiceRequestValidator : AbstractValidator<CreateServiceRequest>
{
    public CreateServiceRequestValidator()
    {
        // Data é DateOnly não-nulo: campo ausente no JSON binda como default 0001-01-01
        // (não null) — NotEqual(MinValue) é o que pega esse caso; 0001-01-01 também
        // passaria pelo LessThanOrEqualTo (está no passado).
        RuleFor(x => x.Data)
            .NotEqual(DateOnly.MinValue).WithMessage("Data é obrigatória")
            .LessThanOrEqualTo(_ => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1))
            .WithMessage("Data não pode ser futura");
        RuleFor(x => x.Custo).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Notas).MaximumLength(2000);
        RuleFor(x => x.Odometro).GreaterThanOrEqualTo(0).When(x => x.Odometro.HasValue);
        // Odometro só para veiculo é regra de domínio no endpoint (precisa do asset)
    }
}
