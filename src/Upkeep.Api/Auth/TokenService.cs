using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Upkeep;

namespace Upkeep.Api.Auth;

public sealed class JwtOptions
{
    public string Key { get; init; } = "";
    public string Issuer { get; init; } = "";
    public string Audience { get; init; } = "";
    public int AccessMinutes { get; init; } = 15;
    public int RefreshDays { get; init; } = 7;
}

public interface ITokenService
{
    (string AccessToken, DateTime ExpiresAt) CreateAccessToken(User user);
    (string Raw, RefreshToken Entity) CreateRefreshToken(Guid userId, DateTime utcNow);
    string HashToken(string raw);
}

public sealed class TokenService(JwtOptions options) : ITokenService
{
    public (string, DateTime) CreateAccessToken(User user)
    {
        var agora = DateTime.UtcNow;
        var expira = agora.AddMinutes(options.AccessMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Key)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(options.Issuer, options.Audience, claims,
            expires: expira, signingCredentials: creds);
        return (new JwtSecurityTokenHandler().WriteToken(token), expira);
    }

    public (string, RefreshToken) CreateRefreshToken(Guid userId, DateTime utcNow)
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var entity = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = HashToken(raw),
            ExpiresAt = utcNow.AddDays(options.RefreshDays),
            CreatedAt = utcNow
        };
        return (raw, entity);
    }

    public string HashToken(string raw) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
}
