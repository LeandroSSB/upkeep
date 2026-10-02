using FluentValidation;

namespace Upkeep.Api.Services;

public sealed class CreateServiceRequestValidator : AbstractValidator<CreateServiceRequest>
{
    public CreateServiceRequestValidator()
    {
        // tolerância de 1 dia (fuso do cliente); "não futura" de verdade
        RuleFor(x => x.Data)
            .LessThanOrEqualTo(_ => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1))
            .WithMessage("Data não pode ser futura");
        RuleFor(x => x.Custo).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Notas).MaximumLength(2000);
        RuleFor(x => x.Odometro).GreaterThanOrEqualTo(0).When(x => x.Odometro.HasValue);
        // Odometro só para veiculo é regra de domínio no endpoint (precisa do asset)
    }
}
