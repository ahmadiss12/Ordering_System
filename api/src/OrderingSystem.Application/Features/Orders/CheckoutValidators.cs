using FluentValidation;

namespace OrderingSystem.Application.Features.Orders;

public sealed class CheckoutRequestValidator : AbstractValidator<CheckoutRequest>
{
    public CheckoutRequestValidator()
    {
        RuleFor(r => r.Fulfillment).IsInEnum();
        RuleFor(r => r.PaymentMethod).IsInEnum();
        RuleFor(r => r.CustomerNote).MaximumLength(1000);

        // Shape only. Whether it matches what the order actually costs is decided against a
        // freshly computed price, not here.
        RuleFor(r => r.ExpectedTotalUsd).GreaterThanOrEqualTo(0);

        // Required, not optional. The column's unique index has no filter, so an omitted key
        // becomes Guid.Empty and the second order ever placed collides with the first.
        RuleFor(r => r.IdempotencyKey).NotEmpty()
            .WithMessage("A checkout attempt needs its own idempotency key.");
    }
}
