using FluentValidation;

namespace OrderingSystem.Application.Features.Restaurants;

public sealed class UpdateRestaurantSettingsRequestValidator
    : AbstractValidator<UpdateRestaurantSettingsRequest>
{
    /// <summary>
    /// Two hours. Long enough for a slow-cooked anything, short enough that a typo — 600 instead
    /// of 60 — is refused rather than promising a customer a delivery tomorrow.
    /// </summary>
    public const int MaxPrepMinutes = 120;

    /// <summary>
    /// A minimum order this high would refuse almost every basket. The cap exists so a mistyped
    /// figure fails here rather than as a mysterious "add $9,995 more to continue" at checkout.
    /// </summary>
    public const decimal MaxMinOrderUsd = 500m;

    public UpdateRestaurantSettingsRequestValidator()
    {
        // Lengths match the columns. A validator that allows more hands the caller a truncation
        // error from SQL Server instead of a 400 naming the field — which is exactly what a
        // customer note did until Phase 3 went looking.
        RuleFor(r => r.Name).NotEmpty().MaximumLength(200);
        RuleFor(r => r.Description).MaximumLength(1000);
        RuleFor(r => r.Phone).NotEmpty().MaximumLength(32);

        RuleFor(r => r.DefaultPrepMinutes)
            .InclusiveBetween(1, MaxPrepMinutes)
            .WithMessage($"Prep time must be between 1 and {MaxPrepMinutes} minutes.");

        RuleFor(r => r.MinOrderUsd)
            .InclusiveBetween(0m, MaxMinOrderUsd)
            .WithMessage($"A minimum order must be between $0 and ${MaxMinOrderUsd:0}.");
    }
}
