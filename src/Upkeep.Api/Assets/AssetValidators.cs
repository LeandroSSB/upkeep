using FluentValidation;

namespace Upkeep.Api.Assets;

public sealed class CreateAssetRequestValidator : AbstractValidator<CreateAssetRequest>
{
    public CreateAssetRequestValidator()
    {
        RuleFor(x => x.Nome).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Tipo).NotEmpty(); // parse válido é regra de domínio no endpoint (precisa de contexto)
        RuleFor(x => x.OdometroAtual)
            .GreaterThanOrEqualTo(0).When(x => x.OdometroAtual.HasValue);
    }
}

public sealed class UpdateAssetRequestValidator : AbstractValidator<UpdateAssetRequest>
{
    public UpdateAssetRequestValidator()
    {
        RuleFor(x => x.Nome).NotEmpty().MaximumLength(200);
    }
}

public sealed class UpdateOdometerRequestValidator : AbstractValidator<UpdateOdometerRequest>
{
    public UpdateOdometerRequestValidator()
    {
        RuleFor(x => x.Odometer).GreaterThanOrEqualTo(0);
    }
}
