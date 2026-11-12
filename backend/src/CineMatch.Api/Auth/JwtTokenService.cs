using System.Security.Claims;
using System.Text;
using CineMatch.Core.Models;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace CineMatch.Api.Auth;

public class JwtSettings
{
    public string Issuer { get; set; } = "cinematch";
    public string Audience { get; set; } = "cinematch";

    /// <summary>At least 32 bytes. Comes from User Secrets (<c>Jwt:SigningKey</c>), never from appsettings.json.</summary>
    public string SigningKey { get; set; } = "";

    public int LifetimeMinutes { get; set; } = 60;

    public SymmetricSecurityKey CreateKey() => new(Encoding.UTF8.GetBytes(SigningKey));
}

public static class JwtClaims
{
    public const string UserId = "sub";
    public const string Name = "name";
    public const string Role = "role";
}

public class JwtTokenService(IOptions<JwtSettings> options)
{
    private readonly JwtSettings _settings = options.Value;

    public (string Token, DateTime ExpiresAt) Create(User user)
    {
        var expiresAt = DateTime.UtcNow.AddMinutes(_settings.LifetimeMinutes);
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtClaims.UserId, user.Id.ToString()),
                new Claim(JwtClaims.Name, user.Username),
                new Claim(JwtClaims.Role, user.Role),
            ]),
            Issuer = _settings.Issuer,
            Audience = _settings.Audience,
            Expires = expiresAt,
            SigningCredentials = new SigningCredentials(_settings.CreateKey(), SecurityAlgorithms.HmacSha256),
        });
        return (token, expiresAt);
    }
}

public static class ClaimsPrincipalExtensions
{
    /// <summary>The id of the logged-in user. Only call on endpoints that require login.</summary>
    public static int GetUserId(this ClaimsPrincipal principal) =>
        int.Parse(principal.FindFirstValue(JwtClaims.UserId)
                  ?? throw new InvalidOperationException("The request has no user id claim."));

    /// <summary>The id of the logged-in user, or null for anonymous requests.</summary>
    public static int? GetUserIdOrNull(this ClaimsPrincipal principal) =>
        int.TryParse(principal.FindFirstValue(JwtClaims.UserId), out var id) ? id : null;
}
