using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using OrderingSystem.Application.Abstractions;
using OrderingSystem.Domain.Exceptions;

namespace OrderingSystem.Application.Common;

internal sealed class ValidationService(IServiceProvider services) : IValidationService
{
    public async Task ValidateAsync<T>(T instance, CancellationToken cancellationToken = default)
    {
        // Not every request type has rules, and that is legitimate rather than an error.
        var validator = services.GetService<IValidator<T>>();
        if (validator is null)
        {
            return;
        }

        var result = await validator.ValidateAsync(instance!, cancellationToken);
        if (result.IsValid)
        {
            return;
        }

        var errors = result.Errors
            .GroupBy(e => e.PropertyName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray(), StringComparer.Ordinal);

        throw new ValidationFailedException("One or more fields are invalid.", errors);
    }
}
