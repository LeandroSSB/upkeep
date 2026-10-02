using System.Security.Claims;
using System.Text.RegularExpressions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Upkeep.Api.Auth;
using Upkeep.Api.Common;
using Upkeep.Api.Notifications;
using Upkeep.Infrastructure;

namespace Upkeep.Api.Me;

public sealed record NtfyTopicRequest(string? NtfyTopic);
public sealed record MeResponse(Guid Id, string Email, string? NtfyTopic);

/// <summary>
/// null/vazio/whitespace = limpar (operação válida); caso contrário o tópico só
/// pode ter letras, números, - e _ (máx 64) — nomes seguros para URL do ntfy.
/// </summary>
public sealed class NtfyTopicRequestValidator : AbstractValidator<NtfyTopicRequest>
{
    private const string Pattern = "^[a-zA-Z0-9_-]{1,64}$";

    public NtfyTopicRequestValidator()
    {
        RuleFor(x => x.NtfyTopic)
            .Must(t => t is null || string.IsNullOrWhiteSpace(t) || Regex.IsMatch(t, Pattern))
            .WithMessage("Tópico inválido: use apenas letras, números, - e _ (máx 64)");
    }
}

public static class MeEndpoints
{
    public static IEndpointRouteBuilder MapMeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/me").WithTags("Me").RequireAuthorization();

        group.MapGet("", async (UpkeepDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var userId = user.GetUserId();
            var u = await db.Users.SingleAsync(x => x.Id == userId, ct);
            return Results.Ok(new MeResponse(u.Id, u.Email, u.NtfyTopic));
        });

        group.MapPut("/ntfy-topic",
            async (NtfyTopicRequest req, UpkeepDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var userId = user.GetUserId();
            var u = await db.Users.SingleAsync(x => x.Id == userId, ct);
            // null/vazio/whitespace limpa; valor válido é salvo trimpado
            u.NtfyTopic = string.IsNullOrWhiteSpace(req.NtfyTopic) ? null : req.NtfyTopic.Trim();
            await db.SaveChangesAsync(ct);
            return Results.Ok(new MeResponse(u.Id, u.Email, u.NtfyTopic));
        })
            .WithSummary("Define o tópico ntfy do usuário; null/vazio/whitespace limpa")
            .AddEndpointFilter<ValidationFilter<NtfyTopicRequest>>();

        group.MapPost("/ntfy-topic/test",
            async (UpkeepDbContext db, ClaimsPrincipal user, INtfyPublisher ntfy, CancellationToken ct) =>
        {
            var userId = user.GetUserId();
            var u = await db.Users.SingleAsync(x => x.Id == userId, ct);
            // Testa o tópico SALVO — o usuário precisa salvar antes de testar.
            if (string.IsNullOrWhiteSpace(u.NtfyTopic))
                return Results.Problem(title: "Configure um tópico antes de testar", statusCode: 400);

            var enviado = await ntfy.PublishAsync(u.NtfyTopic,
                "upkeep: teste ✓",
                "Se você recebeu, os lembretes vão funcionar.",
                ["white_check_mark"], 3, ct);
            return enviado
                ? Results.Ok(new { enviado = true })
                : Results.Problem(title: "ntfy.sh não aceitou o envio — tente de novo", statusCode: 502);
        })
            .WithSummary("Envia 1 push ntfy de teste para o tópico salvo do usuário");

        return group;
    }
}
