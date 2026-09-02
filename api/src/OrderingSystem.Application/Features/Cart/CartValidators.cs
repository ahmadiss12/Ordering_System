using FluentValidation;

namespace OrderingSystem.Application.Features.Cart;

public sealed class AddCartLineRequestValidator : AbstractValidator<AddCartLineRequest>
{
    public AddCartLineRequestValidator()
    {
        RuleFor(r => r.MenuItemId).NotEmpty();
        RuleFor(r => r.Quantity).InclusiveBetween(1, CartService.MaxLineQuantity);
        RuleFor(r => r.Note).MaximumLength(500);
        RuleFor(r => r.Options).NotNull();

        // Shape only. Whether these options are allowed together is a menu rule, answered by
        // OptionSelection once the item's groups have been loaded.
        RuleForEach(r => r.Options).ChildRules(option =>
        {
            option.RuleFor(o => o.OptionId).NotEmpty();
            option.RuleFor(o => o.Quantity).GreaterThanOrEqualTo(1);
        });
    }
}

public sealed class UpdateCartLineRequestValidator : AbstractValidator<UpdateCartLineRequest>
{
    public UpdateCartLineRequestValidator()
    {
        // Zero is not how a line is removed - there is an endpoint for that, and treating it as
        // one would make "set quantity" silently destructive.
        RuleFor(r => r.Quantity).InclusiveBetween(1, CartService.MaxLineQuantity);
        RuleFor(r => r.Note).MaximumLength(500);
    }
}
