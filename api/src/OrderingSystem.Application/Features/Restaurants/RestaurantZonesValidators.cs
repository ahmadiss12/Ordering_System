using FluentValidation;

namespace OrderingSystem.Application.Features.Restaurants;

public sealed class SetRestaurantZoneRequestValidator : AbstractValidator<SetRestaurantZoneRequest>
{
    /// <summary>
    /// A delivery fee above this is a mistyped figure rather than a price. The cap is here so it
    /// fails on the settings screen rather than at somebody's checkout.
    /// </summary>
    public const decimal MaxFeeUsd = 50m;

    /// <summary>Three hours of driving is not a delivery zone, it is a different city.</summary>
    public const int MaxEstimatedMinutes = 180;

    public SetRestaurantZoneRequestValidator()
    {
        // Zero is allowed and is not an oversight: free delivery into a nearby zone is a real
        // offer, and a restaurant that wants to make it should not have to charge a cent.
        RuleFor(r => r.DeliveryFeeUsd)
            .InclusiveBetween(0m, MaxFeeUsd)
            .WithMessage($"A delivery fee must be between $0 and ${MaxFeeUsd:0}.");

        RuleFor(r => r.EstimatedMinutes)
            .InclusiveBetween(1, MaxEstimatedMinutes)
            .WithMessage($"Travel time must be between 1 and {MaxEstimatedMinutes} minutes.");
    }
}
