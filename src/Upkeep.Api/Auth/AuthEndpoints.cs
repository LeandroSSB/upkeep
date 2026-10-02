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
            async (RegisterRequest req, UpkeepDbContext db, IPasswordHasher hasher, ITokenService tokens) =>
        {
            var email = req.Email.Trim().ToLowerInvariant();
            if (await db.Users.AnyAsync(u => u.Email == email))
                return Results.Conflict(new { title = "E-mail já cadastrado" });

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
            await db.SaveChangesAsync();
            return Results.Created($"/users/{user.Id}",
                new { accessToken = access, refreshToken = refreshRaw, user = new { id = user.Id, email = user.Email } });
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
            await db.SaveChangesAsync();
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

        return app;
    }
}

public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user.FindFirstValue("sub"), out var id)
            ? id
            : throw new UnauthorizedAccessException("Token sem claim sub");
}
