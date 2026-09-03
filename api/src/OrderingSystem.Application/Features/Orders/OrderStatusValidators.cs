using FluentValidation;

namespace OrderingSystem.Application.Features.Orders;

public sealed class ChangeOrderStatusRequestValidator : AbstractValidator<ChangeOrderStatusRequest>
{
    public ChangeOrderStatusRequestValidator()
    {
        RuleFor(r => r.To).IsInEnum();

        // Only that it is a real member. Whether this particular move takes a reason at all
        // depends on who is making it, which is not known until the order has been loaded.
        RuleFor(r => r.Reason!.Value).IsInEnum().When(r => r.Reason is not null);

        // The column is 500. A validator that allowed more would hand the caller a 500 from a
        // truncation error instead of a 400 saying what was wrong.
        RuleFor(r => r.Note).MaximumLength(500);
    }
}
