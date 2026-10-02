using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Upkeep;
using Upkeep.Api.Auth;
using Xunit;

namespace Upkeep.UnitTests;

public class TokenServiceTests
{
    private readonly TokenService _svc = new(new JwtOptions
    {
        Key = "chave-de-teste-bem-longa-com-256-bits-minimo!!",
        Issuer = "upkeep-tests",
        Audience = "upkeep-tests",
        AccessMinutes = 15,
        RefreshDays = 7
    });

    private static readonly User User = new()
    {
        Id = Guid.NewGuid(),
        Email = "a@b.c",
        PasswordHash = "hash"
    };

    [Fact]
    public void Access_token_tem_claims_sub_e_email_e_expira_em_15min()
    {
        var agora = DateTime.UtcNow;
        var (token, expires) = _svc.CreateAccessToken(User);

        var handler = new JwtSecurityTokenHandler();
        // sem isto o handler mapeia "sub"/"email" para os claim types longos do .NET
        handler.MapInboundClaims = false;
        var principal = handler.ValidateToken(token, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "upkeep-tests",
            ValidateAudience = true,
            ValidAudience = "upkeep-tests",
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes("chave-de-teste-bem-longa-com-256-bits-minimo!!")),
            ValidateLifetime = false,
            ClockSkew = TimeSpan.Zero
        }, out _);

        Assert.Equal(User.Id.ToString(), principal.FindFirst("sub")!.Value);
        Assert.Equal("a@b.c", principal.FindFirst("email")!.Value);
        Assert.True(expires - agora <= TimeSpan.FromMinutes(15));
    }

    [Fact]
    public void Refresh_token_raw_e_grande_e_hash_e_sha256_hex()
    {
        var agora = new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc);
        var (raw, entity) = _svc.CreateRefreshToken(User.Id, agora);

        Assert.True(raw.Length >= 43);
        Assert.Equal(User.Id, entity.UserId);
        Assert.Equal(agora.AddDays(7), entity.ExpiresAt);
        Assert.NotEqual(raw, entity.TokenHash);
        Assert.Matches("^[0-9a-f]{64}$", entity.TokenHash);
        Assert.Equal(entity.TokenHash, _svc.HashToken(raw));
    }

    [Fact]
    public void Refresh_tokens_diferentes_a_cada_chamada()
    {
        var agora = DateTime.UtcNow;
        var (raw1, _) = _svc.CreateRefreshToken(User.Id, agora);
        var (raw2, _) = _svc.CreateRefreshToken(User.Id, agora);
        Assert.NotEqual(raw1, raw2);
    }
}
