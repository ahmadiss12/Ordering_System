namespace OrderingSystem.Application.Abstractions;

/// <summary>
/// Runs the FluentValidation validator registered for a request type, throwing if it fails.
/// <para>
/// A use case calls this on its first line. That keeps validation on the same path for every
/// caller — a controller, a background job, a test — rather than depending on an MVC filter that
/// only fires for HTTP.
/// </para>
/// </summary>
public interface IValidationService
{
    Task ValidateAsync<T>(T instance, CancellationToken cancellationToken = default);
}
