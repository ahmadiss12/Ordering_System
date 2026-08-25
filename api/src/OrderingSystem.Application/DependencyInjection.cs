using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using OrderingSystem.Application.Abstractions;
using OrderingSystem.Application.Common;
using OrderingSystem.Application.Features.Auth;
using OrderingSystem.Application.Features.Orders;

namespace OrderingSystem.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<RegisterRequestValidator>(
            ServiceLifetime.Singleton, includeInternalTypes: false);

        services.AddSingleton<IValidationService, ValidationService>();
        services.AddScoped<AuthService>();

        // Scoped because it takes IAppDbContext, which is scoped. A singleton use case holding
        // a scoped context is the classic captive-dependency bug: it would keep the first
        // request's context alive for the life of the process.
        services.AddScoped<OrderStatusService>();

        return services;
    }
}
