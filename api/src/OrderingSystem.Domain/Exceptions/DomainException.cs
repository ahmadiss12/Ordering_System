namespace OrderingSystem.Domain.Exceptions;

/// <summary>
/// Base for every failure the business rules can produce. The API maps these to status codes in
/// one place, so a use case throws what went wrong and never decides an HTTP number.
/// </summary>
public abstract class DomainException(string message) : Exception(message);

/// <summary>The thing asked for does not exist, or the caller may not know that it does. → 404</summary>
public sealed class NotFoundException(string message) : DomainException(message);

/// <summary>The request is understood but conflicts with current state. → 409</summary>
public sealed class ConflictException(string message) : DomainException(message);

/// <summary>Authenticated, but not allowed to touch this. → 403</summary>
public sealed class ForbiddenException(string message) : DomainException(message);

/// <summary>Credentials or a token did not check out. → 401</summary>
public sealed class AuthenticationFailedException(string message) : DomainException(message);

/// <summary>A rule about the request itself was broken. → 400</summary>
public sealed class ValidationFailedException(string message, IReadOnlyDictionary<string, string[]> errors)
    : DomainException(message)
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
