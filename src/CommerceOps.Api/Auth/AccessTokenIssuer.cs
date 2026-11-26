using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CommerceOps.Api.Auth;

internal sealed record TokenResponse(string AccessToken, string TokenType, int ExpiresIn);

internal sealed class AccessTokenIssuer(IOptions<JwtSettings> options, TimeProvider clock)
{
    private const int LifetimeSeconds = 900;

    public TokenResponse Issue(IdentityUser<Guid> user, IEnumerable<string> roles)
    {
        var settings = options.Value;
        var now = clock.GetUtcNow().UtcDateTime;
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new("name", user.UserName!),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        claims.AddRange(roles.Select(role => new Claim("role", role)));

        var token = new JwtSecurityTokenHandler().CreateEncodedJwt(new SecurityTokenDescriptor
        {
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddSeconds(LifetimeSeconds),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Convert.FromBase64String(settings.SigningKey)), SecurityAlgorithms.HmacSha256)
        });
        return new TokenResponse(token, "Bearer", LifetimeSeconds);
    }
}
