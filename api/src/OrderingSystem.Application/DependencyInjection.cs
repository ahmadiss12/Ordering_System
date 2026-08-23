using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using OrderingSystem.Application.Abstractions;
using OrderingSystem.Application.Common;
using OrderingSystem.Application.Features.Auth;

namespace OrderingSystem.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<RegisterRequestValidator>(
            ServiceLifetime.Singleton, includeInternalTypes: false);

        services.AddSingleton<IValidationService, ValidationService>();
        services.AddScoped<AuthService>();

        return services;
    }
}
