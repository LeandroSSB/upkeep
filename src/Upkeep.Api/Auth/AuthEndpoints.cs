using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Upkeep;
using Upkeep.Api.Common;
using Upkeep.Infrastructure;

namespace Upkeep.Api.Auth;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth").WithTags("Auth").RequireRateLimiting("auth");

        group.MapPost("/register",
            async (RegisterRequest req, UpkeepDbContext db, IPasswordHasher hasher, ITokenService tokens,
                ILoggerFactory loggerFactory) =>
        {
            var email = req.Email.Trim().ToLowerInvariant();
            if (await db.Users.AnyAsync(u => u.Email == email))
                return EmailJaCadastrado();

            var user = new User
            {
                Id = Guid.NewGuid(),
                Email = email,
                PasswordHash = hasher.Hash(req.Password),
                CreatedAt = DateTime.UtcNow
            };
            db.Users.Add(user);
            var (access, _) = tokens.CreateAccessToken(user);
            var (refreshRaw, refreshEntity) = tokens.CreateRefreshToken(user.Id, DateTime.UtcNow);
            db.RefreshTokens.Add(refreshEntity);
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException ex)
            {
                // race do check-then-insert: duas requests passaram pelo AnyAsync juntas,
                // o unique index derruba a segunda → mesmo 409 do caminho feliz.
                // Log: qualquer outra DbUpdateException aqui também viraria 409 —
                // sem isso seria indistinguível de "e-mail já existe".
                loggerFactory.CreateLogger("Auth")
                    .LogWarning(ex, "register: DbUpdateException → 409 (email {Email})", email);
                return EmailJaCadastrado();
            }
            // 201 sem Location: /users/{id} não é rota da API — header apontaria p/ nada
            return Results.Json(
                new { accessToken = access, refreshToken = refreshRaw, user = new { id = user.Id, email = user.Email } },
                statusCode: StatusCodes.Status201Created);
        }).AddEndpointFilter<ValidationFilter<RegisterRequest>>();

        group.MapPost("/login",
            async (LoginRequest req, UpkeepDbContext db, IPasswordHasher hasher, ITokenService tokens) =>
        {
            var email = req.Email.Trim().ToLowerInvariant();
            var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email);
            if (user is null || !hasher.Verify(user.PasswordHash, req.Password))
                return Results.Unauthorized();
            var (access, _) = tokens.CreateAccessToken(user);
            var (refreshRaw, refreshEntity) = tokens.CreateRefreshToken(user.Id, DateTime.UtcNow);
            db.RefreshTokens.Add(refreshEntity);
            // emite o token novo PRIMEIRO (SaveChanges) e só então o purge — ExecuteDelete
            // roda fora do change tracker (1 SQL DELETE) e não pode engolir o insert acima
            await db.SaveChangesAsync();
            await db.RefreshTokens.Where(t =>
                    t.UserId == user.Id &&
                    (t.ExpiresAt <= DateTime.UtcNow || t.RevokedAt <= DateTime.UtcNow.AddDays(-30)))
                .ExecuteDeleteAsync();
            return Results.Ok(new { accessToken = access, refreshToken = refreshRaw,
                user = new { id = user.Id, email = user.Email } });
        }).AddEndpointFilter<ValidationFilter<LoginRequest>>();

        group.MapPost("/refresh", async (RefreshRequest req, UpkeepDbContext db, ITokenService tokens) =>
        {
            var hash = tokens.HashToken(req.RefreshToken);
            var stored = await db.RefreshTokens.Include(t => t.User)
                .SingleOrDefaultAsync(t => t.TokenHash == hash);

            if (stored is null || stored.RevokedAt is not null)
            {
                // reuso/inválido → revoga tudo do usuário (detecção de roubo)
                if (stored is not null)
                {
                    var all = db.RefreshTokens.Where(t => t.UserId == stored.UserId && t.RevokedAt == null);
                    foreach (var t in all)
                        t.RevokedAt = DateTime.UtcNow;
                }
                await db.SaveChangesAsync();
                return Results.Unauthorized();
            }
            if (stored.ExpiresAt <= DateTime.UtcNow)
                return Results.Unauthorized();

            stored.RevokedAt = DateTime.UtcNow; // rotação (entidade rastreada pelo EF — mutação persiste)
            var (access, _) = tokens.CreateAccessToken(stored.User!);
            var (refreshRaw, entity) = tokens.CreateRefreshToken(stored.UserId, DateTime.UtcNow);
            db.RefreshTokens.Add(entity);
            await db.SaveChangesAsync();
            return Results.Ok(new { accessToken = access, refreshToken = refreshRaw });
        }).AddEndpointFilter<ValidationFilter<RefreshRequest>>();

        // Rota anônima (como o refresh): quem chama pode não ter mais access válido.
        // Sempre 200 {} — idempotente e sem vazar existência do token.
        group.MapPost("/logout", async (RefreshRequest req, UpkeepDbContext db, ITokenService tokens) =>
        {
            var hash = tokens.HashToken(req.RefreshToken);
            var stored = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash);
            if (stored is not null && stored.RevokedAt is null)
            {
                stored.RevokedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }
            return Results.Ok(new { });
        }).AddEndpointFilter<ValidationFilter<RefreshRequest>>();

        return app;
    }

    /// <summary>409 ProblemDetails (contrato de erro da API — mesmo shape em todos os endpoints).</summary>
    private static IResult EmailJaCadastrado() => Results.Problem(
        title: "E-mail já cadastrado",
        statusCode: StatusCodes.Status409Conflict,
        type: "https://upkeep.leandrossb.com/errors/conflict");
}

public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user.FindFirstValue("sub"), out var id)
            ? id
            : throw new UnauthorizedAccessException("Token sem claim sub");
}
