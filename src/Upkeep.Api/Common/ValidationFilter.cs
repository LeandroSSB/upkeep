using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Upkeep.Api.Common;

/// <summary>Endpoint filter: valida o DTO com FluentValidation; 400 ProblemDetails com errors[].</summary>
public sealed class ValidationFilter<T>(IServiceProvider sp) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var validator = sp.GetService<IValidator<T>>();
        if (validator is not null)
        {
            var dto = ctx.Arguments.OfType<T>().First();
            var result = await validator.ValidateAsync(dto);
            if (!result.IsValid)
            {
                return Results.ValidationProblem(
                    result.Errors.GroupBy(e => e.PropertyName)
                        .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()));
            }
        }
        return await next(ctx);
    }
}
