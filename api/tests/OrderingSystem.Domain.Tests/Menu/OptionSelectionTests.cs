using OrderingSystem.Domain.Menu;

namespace OrderingSystem.Domain.Tests.Menu;

/// <summary>
/// The rules a restaurant declared in the Phase 2 editor, enforced at the moment somebody orders.
///
/// <para>
/// The storefront will stop most of this before a request is ever sent. That is a convenience,
/// not a control: these tests describe what happens to a request built by hand.
/// </para>
/// </summary>
public class OptionSelectionTests
{
    private static readonly Guid SizeGroup = Guid.NewGuid();
    private static readonly Guid SauceGroup = Guid.NewGuid();

    private static GroupBounds Size(int min = 1, int? max = 1) =>
        new(SizeGroup, "Size", min, max);

    private static GroupBounds Sauces(int min = 0, int? max = 3) =>
        new(SauceGroup, "Sauces", min, max);

    private static PickedOption Pick(
        Guid group, string name, int quantity = 1, int maxQuantity = 1, bool available = true) =>
        new(Guid.NewGuid(), group, name, quantity, maxQuantity, available);

    [Fact]
    public void A_valid_selection_produces_nothing()
    {
        var errors = OptionSelection.Validate(
            [Size(), Sauces()],
            [Pick(SizeGroup, "Large"), Pick(SauceGroup, "Garlic")]);

        errors.ShouldBeEmpty();
    }

    [Fact]
    public void An_optional_group_may_be_skipped_entirely()
    {
        var errors = OptionSelection.Validate(
            [Sauces(min: 0)],
            []);

        errors.ShouldBeEmpty();
    }

    [Fact]
    public void A_required_group_must_be_answered()
    {
        var errors = OptionSelection.Validate([Size()], []);

        errors.ShouldHaveSingleItem();
        errors[0].Field.ShouldBe("Size");
        errors[0].Message.ShouldBe("Choose one from Size.");
    }

    [Fact]
    public void Choosing_two_where_one_is_allowed_is_refused()
    {
        var errors = OptionSelection.Validate(
            [Size()],
            [Pick(SizeGroup, "Regular"), Pick(SizeGroup, "Large")]);

        errors.ShouldHaveSingleItem();
        errors[0].Message.ShouldBe("Choose only one from Size.");
    }

    [Fact]
    public void Going_over_a_larger_limit_names_the_limit()
    {
        var errors = OptionSelection.Validate(
            [Sauces(max: 3)],
            [
                Pick(SauceGroup, "Garlic"),
                Pick(SauceGroup, "BBQ"),
                Pick(SauceGroup, "Ketchup"),
                Pick(SauceGroup, "Ranch"),
            ]);

        errors.ShouldHaveSingleItem();
        errors[0].Message.ShouldBe("Choose at most 3 from Sauces.");
    }

    [Fact]
    public void A_group_with_no_maximum_accepts_everything()
    {
        var errors = OptionSelection.Validate(
            [Sauces(min: 0, max: null)],
            [.. Enumerable.Range(0, 12).Select(i => Pick(SauceGroup, $"Sauce {i}"))]);

        errors.ShouldBeEmpty();
    }

    [Fact]
    public void An_option_from_a_group_this_item_does_not_offer_is_refused()
    {
        // The menu changed under the customer, or the request was not built by our storefront.
        var errors = OptionSelection.Validate(
            [Size()],
            [Pick(SizeGroup, "Large"), Pick(SauceGroup, "Garlic")]);

        errors.ShouldHaveSingleItem();
        errors[0].Field.ShouldBe("options");
        errors[0].Message.ShouldContain("not one of the choices");
    }

    [Fact]
    public void A_sold_out_option_cannot_be_ordered()
    {
        var errors = OptionSelection.Validate(
            [Sauces()],
            [Pick(SauceGroup, "Garlic", available: false)]);

        errors.ShouldHaveSingleItem();
        errors[0].Field.ShouldBe("Sauces");
        errors[0].Message.ShouldContain("not available");
    }

    [Fact]
    public void More_of_one_option_than_the_menu_allows_is_refused()
    {
        var errors = OptionSelection.Validate(
            [Sauces()],
            [Pick(SauceGroup, "Extra Cheese", quantity: 3, maxQuantity: 2)]);

        errors.ShouldHaveSingleItem();
        errors[0].Message.ShouldBe("You can add at most 2 × Extra Cheese.");
    }

    [Fact]
    public void A_quantity_of_zero_is_not_a_way_to_deselect()
    {
        var errors = OptionSelection.Validate(
            [Sauces()],
            [Pick(SauceGroup, "Garlic", quantity: 0)]);

        errors.ShouldHaveSingleItem();
        errors[0].Message.ShouldContain("at least one");
    }

    [Fact]
    public void The_same_option_twice_is_refused_with_a_sentence()
    {
        // The composite key would refuse it too, but a constraint violation is not something a
        // person can act on.
        var repeated = Pick(SauceGroup, "Garlic");

        var errors = OptionSelection.Validate([Sauces()], [repeated, repeated]);

        errors.ShouldHaveSingleItem();
        errors[0].Message.ShouldContain("more than once");
        errors[0].Message.ShouldContain("quantity");
    }

    [Fact]
    public void Quantity_is_not_what_the_group_limit_counts()
    {
        // "Choose up to 3" means three different sauces. Two of one and one of another is two
        // choices, not three, and must pass.
        var errors = OptionSelection.Validate(
            [Sauces(max: 2)],
            [
                Pick(SauceGroup, "Extra Cheese", quantity: 2, maxQuantity: 3),
                Pick(SauceGroup, "Garlic"),
            ]);

        errors.ShouldBeEmpty();
    }

    [Fact]
    public void Every_problem_is_reported_at_once()
    {
        // Fixing one thing only to be told about the next is the worst version of a form.
        var errors = OptionSelection.Validate(
            [Size(), Sauces(min: 1)],
            [Pick(SizeGroup, "Regular"), Pick(SizeGroup, "Large")]);

        errors.Count.ShouldBe(2);
        errors.Select(e => e.Field).ShouldBe(["Size", "Sauces"], ignoreOrder: true);
    }

    [Fact]
    public void An_exact_count_reads_as_a_count_and_not_a_minimum()
    {
        var errors = OptionSelection.Validate(
            [new GroupBounds(SauceGroup, "Sides", MinSelect: 2, MaxSelect: 2)],
            []);

        errors[0].Message.ShouldBe("Choose 2 from Sides.");
    }

    [Fact]
    public void A_minimum_below_a_larger_maximum_reads_as_a_minimum()
    {
        var errors = OptionSelection.Validate(
            [new GroupBounds(SauceGroup, "Toppings", MinSelect: 2, MaxSelect: 5)],
            [Pick(SauceGroup, "Cheese")]);

        errors[0].Message.ShouldBe("Choose at least 2 from Toppings.");
    }
}
