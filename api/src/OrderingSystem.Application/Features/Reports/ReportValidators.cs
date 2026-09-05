using FluentValidation;

namespace OrderingSystem.Application.Features.Reports;

public sealed class ReportRangeRequestValidator : AbstractValidator<ReportRangeRequest>
{
    /// <summary>What a range covers when the caller names neither end.</summary>
    public const int DefaultDays = 30;

    /// <summary>
    /// A ceiling on how much a single request may ask for. Every day in the range comes back as a
    /// row whether anything happened on it or not, so an unbounded range is a response the size of
    /// the years between the two dates — and a mistyped year is how that happens.
    /// </summary>
    public const int MaxDays = 366;

    public ReportRangeRequestValidator()
    {
        RuleFor(r => r)
            .Must(r => r.From is null || r.To is null || r.From <= r.To)
            .WithMessage("The start of the range must not be after its end.");

        RuleFor(r => r)
            .Must(r => r.From is null || r.To is null || r.To.Value.DayNumber - r.From.Value.DayNumber < MaxDays)
            .WithMessage($"A report covers at most {MaxDays} days at a time.");
    }
}
