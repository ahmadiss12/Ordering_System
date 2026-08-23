using OrderingSystem.Domain.Enums;
using OrderingSystem.Domain.Identity;

namespace OrderingSystem.Application.Abstractions;

public interface IAccessTokenIssuer
{
    /// <summary>
    /// Signs a short-lived access token. The restaurant id becomes the claim every global query
    /// filter reads, so it must come from the database rather than from anything the client sent.
    /// </summary>
    (string Token, DateTimeOffset ExpiresAt) Issue(
        User user, IReadOnlyCollection<RoleType> roles, Guid? restaurantId);
}
