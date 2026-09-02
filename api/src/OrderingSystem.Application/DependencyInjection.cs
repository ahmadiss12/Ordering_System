using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using OrderingSystem.Application.Abstractions;
using OrderingSystem.Application.Common;
using OrderingSystem.Application.Features.Auth;
using OrderingSystem.Application.Features.Cart;
using OrderingSystem.Application.Features.Catalog;
using OrderingSystem.Application.Features.Menu;

namespace OrderingSystem.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<RegisterRequestValidator>(
            ServiceLifetime.Singleton, includeInternalTypes: false);

        services.AddSingleton<IValidationService, ValidationService>();

        // Scoped: it reads the current request's tenant, so it must not outlive the request.
        services.AddScoped<ITenantGuard, TenantGuard>();
        services.AddScoped<AuthService>();
        services.AddScoped<CatalogService>();
        services.AddScoped<MenuAdminService>();
        services.AddScoped<CartService>();

        return services;
    }
}
