using FluentValidation;

namespace OrderingSystem.Application.Features.Orders;

public sealed class CheckoutRequestValidator : AbstractValidator<CheckoutRequest>
{
    public CheckoutRequestValidator()
    {
        RuleFor(r => r.Fulfillment).IsInEnum();
        RuleFor(r => r.PaymentMethod).IsInEnum();

        // 500, matching the column. It said 1000 until a sweep compared every validator against
        // its column: a 600-character note passed validation and then failed in SQL Server with a
        // truncation error, which reaches the customer as a 500 with nothing to act on.
        RuleFor(r => r.CustomerNote).MaximumLength(500);

        // Shape only. Whether it matches what the order actually costs is decided against a
        // freshly computed price, not here.
        RuleFor(r => r.ExpectedTotalUsd).GreaterThanOrEqualTo(0);

        // Required, not optional. The column's unique index has no filter, so an omitted key
        // becomes Guid.Empty and the second order ever placed collides with the first.
        RuleFor(r => r.IdempotencyKey).NotEmpty()
            .WithMessage("A checkout attempt needs its own idempotency key.");
    }
}
