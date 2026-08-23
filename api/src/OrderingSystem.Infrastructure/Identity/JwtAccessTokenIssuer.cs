using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OrderingSystem.Application.Abstractions;
using OrderingSystem.Domain.Enums;
using OrderingSystem.Domain.Identity;

namespace OrderingSystem.Infrastructure.Identity;

public sealed class JwtAccessTokenIssuer(IOptions<JwtOptions> options, IClock clock) : IAccessTokenIssuer
{
    /// <summary>The claim every global query filter ultimately depends on.</summary>
    public const string RestaurantIdClaim = "restaurant_id";

    private readonly JwtOptions _options = options.Value;

    public (string Token, DateTimeOffset ExpiresAt) Issue(
        User user, IReadOnlyCollection<RoleType> roles, Guid? restaurantId)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(roles);

        var expiresAt = clock.UtcNow.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        claims.AddRange(roles.Select(role => new Claim("role", role.ToString())));

        if (restaurantId is not null)
        {
            claims.Add(new Claim(RestaurantIdClaim, restaurantId.Value.ToString()));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));

        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            Subject = new ClaimsIdentity(claims),
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
        });

        return (token, expiresAt);
    }
}
