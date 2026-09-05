using FluentValidation;

namespace OrderingSystem.Application.Features.Platform;

public sealed class SetCommissionRequestValidator : AbstractValidator<SetCommissionRequest>
{
    /// <summary>
    /// Not a legal limit, a typing one. The mistake this catches is 150 typed for 15, and a
    /// marketplace taking more than half of every order is not a rate anybody would have signed.
    /// </summary>
    public const decimal MaxCommissionPercent = 50m;

    public SetCommissionRequestValidator() =>
        RuleFor(r => r.CommissionPercent)
            .InclusiveBetween(0m, MaxCommissionPercent)
            .WithMessage($"Commission must be between 0% and {MaxCommissionPercent}%.")
            // Two decimal places is what the column stores, so a third would be silently rounded
            // away and the screen would show a number nobody typed.
            .Must(percent => decimal.Round(percent, 2) == percent)
            .WithMessage("Commission is stored to two decimal places.");
}

public sealed class CreateRestaurantRequestValidator : AbstractValidator<CreateRestaurantRequest>
{
    public CreateRestaurantRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(200);
        RuleFor(r => r.Phone).NotEmpty().MaximumLength(32);

        RuleFor(r => r.OwnerEmail).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(r => r.OwnerFullName).NotEmpty().MaximumLength(200);
        RuleFor(r => r.OwnerPhone).MaximumLength(32);

        RuleFor(r => r.CommissionPercent)
            .InclusiveBetween(0m, SetCommissionRequestValidator.MaxCommissionPercent)
            .WithMessage(
                $"Commission must be between 0% and {SetCommissionRequestValidator.MaxCommissionPercent}%.");

        // Only when one was typed. Left out, the service derives it and is the one responsible for
        // producing something valid — validating a value the caller never supplied would refuse
        // the request they actually made.
        When(r => !string.IsNullOrWhiteSpace(r.Slug), () =>
            RuleFor(r => r.Slug!)
                .MaximumLength(120)
                .Matches("^[a-z0-9]+(-[a-z0-9]+)*$")
                .WithMessage(
                    "A link can hold lowercase letters, numbers and single hyphens between them."));
    }
}
