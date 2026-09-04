using FluentValidation;

namespace OrderingSystem.Application.Features.Restaurants;

public sealed class SetWeeklyHoursRequestValidator : AbstractValidator<SetWeeklyHoursRequest>
{
    /// <summary>
    /// Breakfast, lunch, dinner and a late sitting is already more than any real kitchen files.
    /// The cap is here so a loop with a bug cannot post ten thousand rows.
    /// </summary>
    public const int MaxWindowsPerDay = 4;

    public SetWeeklyHoursRequestValidator()
    {
        RuleFor(r => r.Windows).NotNull();

        RuleForEach(r => r.Windows).ChildRules(window =>
        {
            window.RuleFor(w => w.Day).IsInEnum();

            // Equal times are a zero-length window, which the domain reads as closed — so a
            // restaurant that entered 09:00 to 09:00 would be shut and told nothing.
            window.RuleFor(w => w.CloseTime)
                .NotEqual(w => w.OpenTime)
                .WithMessage("A window that opens and closes at the same time is not open at all.");
        });

        RuleFor(r => r.Windows)
            .Must(windows => windows is null || windows
                .GroupBy(w => w.Day)
                .All(day => day.Count() <= MaxWindowsPerDay))
            .WithMessage($"A day can have at most {MaxWindowsPerDay} opening windows.");
    }
}
