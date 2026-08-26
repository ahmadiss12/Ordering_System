using FluentValidation;

namespace OrderingSystem.Application.Features.Menu;

public sealed class CreateCategoryRequestValidator : AbstractValidator<CreateCategoryRequest>
{
    public CreateCategoryRequestValidator() => RuleFor(r => r.Name).NotEmpty().MaximumLength(200);
}

public sealed class UpdateCategoryRequestValidator : AbstractValidator<UpdateCategoryRequest>
{
    public UpdateCategoryRequestValidator() => RuleFor(r => r.Name).NotEmpty().MaximumLength(200);
}

public sealed class CreateMenuItemRequestValidator : AbstractValidator<CreateMenuItemRequest>
{
    public CreateMenuItemRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(200);
        RuleFor(r => r.Description).MaximumLength(1000);
        RuleFor(r => r.BasePriceUsd).ApplyMoneyRules();
    }
}

public sealed class UpdateMenuItemRequestValidator : AbstractValidator<UpdateMenuItemRequest>
{
    public UpdateMenuItemRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(200);
        RuleFor(r => r.Description).MaximumLength(1000);
        RuleFor(r => r.BasePriceUsd).ApplyMoneyRules();
    }
}

public sealed class CreateOptionGroupRequestValidator : AbstractValidator<CreateOptionGroupRequest>
{
    public CreateOptionGroupRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(200);
        RuleFor(r => r.MinSelect).GreaterThanOrEqualTo(0);
        RuleFor(r => r.MaxSelect).GreaterThanOrEqualTo(1).When(r => r.MaxSelect is not null);
        RuleFor(r => r).Must(HaveAConsistentRange)
            .WithMessage("Minimum selections cannot exceed the maximum.");
    }

    // Mirrors CK_OptionGroups_SelectRange. The database enforces it regardless; this exists so a
    // person gets a sentence back instead of a constraint-violation error.
    internal static bool HaveAConsistentRange(CreateOptionGroupRequest r) =>
        r.MaxSelect is null || r.MinSelect <= r.MaxSelect;
}

public sealed class UpdateOptionGroupRequestValidator : AbstractValidator<UpdateOptionGroupRequest>
{
    public UpdateOptionGroupRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(200);
        RuleFor(r => r.MinSelect).GreaterThanOrEqualTo(0);
        RuleFor(r => r.MaxSelect).GreaterThanOrEqualTo(1).When(r => r.MaxSelect is not null);
        RuleFor(r => r).Must(r => r.MaxSelect is null || r.MinSelect <= r.MaxSelect)
            .WithMessage("Minimum selections cannot exceed the maximum.");
    }
}

public sealed class CreateOptionRequestValidator : AbstractValidator<CreateOptionRequest>
{
    public CreateOptionRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(200);
        RuleFor(r => r.MaxQuantity).InclusiveBetween(1, 20);
        // Deliberately not GreaterThan(0): a delta of zero is "no pickles", and a negative one is
        // a removal that genuinely discounts the line.
        RuleFor(r => r.PriceDeltaUsd).InclusiveBetween(-9999.99m, 9999.99m);
    }
}

public sealed class UpdateOptionRequestValidator : AbstractValidator<UpdateOptionRequest>
{
    public UpdateOptionRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(200);
        RuleFor(r => r.MaxQuantity).InclusiveBetween(1, 20);
        RuleFor(r => r.PriceDeltaUsd).InclusiveBetween(-9999.99m, 9999.99m);
    }
}

public sealed class AttachOptionGroupRequestValidator : AbstractValidator<AttachOptionGroupRequest>
{
    public AttachOptionGroupRequestValidator()
    {
        RuleFor(r => r.OptionGroupId).NotEmpty();
        RuleFor(r => r.MinSelectOverride).GreaterThanOrEqualTo(0).When(r => r.MinSelectOverride is not null);
        RuleFor(r => r.MaxSelectOverride).GreaterThanOrEqualTo(1).When(r => r.MaxSelectOverride is not null);
        RuleFor(r => r)
            .Must(r => r.MinSelectOverride is null || r.MaxSelectOverride is null
                       || r.MinSelectOverride <= r.MaxSelectOverride)
            .WithMessage("Minimum selections cannot exceed the maximum.");
    }
}

internal static class MoneyRules
{
    /// <summary>
    /// The column is decimal(10,2), so anything with more precision would be silently rounded on
    /// write. Rejecting it is better than quietly changing a price the restaurant typed.
    /// </summary>
    public static IRuleBuilderOptions<T, decimal> ApplyMoneyRules<T>(
        this IRuleBuilder<T, decimal> rule) =>
        rule.GreaterThanOrEqualTo(0)
            .LessThanOrEqualTo(99_999_999.99m)
            .Must(value => decimal.Round(value, 2) == value)
            .WithMessage("Price cannot have more than two decimal places.");
}
