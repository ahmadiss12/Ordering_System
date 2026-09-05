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
