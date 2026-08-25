using FluentValidation;

namespace OrderingSystem.Application.Features.Orders;

/// <summary>
/// Shape only. Whether the transition itself is legal is the state machine's job — asking a
/// validator would mean the rule lived in two places and one of them would eventually be wrong.
/// </summary>
public sealed class AdvanceOrderStatusRequestValidator : AbstractValidator<AdvanceOrderStatusRequest>
{
    public AdvanceOrderStatusRequestValidator()
    {
        // Enums arrive over the wire as integers, so an unmapped number is a real possibility
        // rather than a theoretical one.
        RuleFor(r => r.To).IsInEnum();

        RuleFor(r => r.RejectionReason).IsInEnum().When(r => r.RejectionReason is not null);

        // Mirrors Lengths.Note in the schema. Infrastructure's constants are internal to it, so
        // the number is repeated here rather than the layers being coupled for one integer.
        RuleFor(r => r.Note).MaximumLength(500);
    }
}
