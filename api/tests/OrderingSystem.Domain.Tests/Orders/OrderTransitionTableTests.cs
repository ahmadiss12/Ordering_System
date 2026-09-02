using OrderingSystem.Domain.Enums;
using OrderingSystem.Domain.Orders;

namespace OrderingSystem.Domain.Tests.Orders;

/// <summary>
/// The truth table ADR-11 exists for.
///
/// <para>
/// The spec asks for "every legal and illegal transition" to be covered. Written by hand that is
/// a list somebody maintains and eventually forgets; here it is the whole cross-product of
/// status × status × actor × fulfillment — 256 combinations — checked against one declared set.
/// Adding a status widens the product automatically, so a move nobody thought about fails here
/// rather than in a kitchen.
/// </para>
/// </summary>
public class OrderTransitionTableTests
{
    private static readonly OrderStatus[] Statuses = Enum.GetValues<OrderStatus>();
    private static readonly OrderActor[] Actors = Enum.GetValues<OrderActor>();
    private static readonly FulfillmentType[] Fulfillments = Enum.GetValues<FulfillmentType>();

    /// <summary>
    /// Every move that may happen, written out independently of the table under test.
    ///
    /// This is deliberately a second copy rather than something derived from
    /// <see cref="OrderTransitions.All"/> — a test that reads the same list it is checking
    /// proves only that the list equals itself.
    /// </summary>
    private static readonly HashSet<(OrderStatus From, OrderStatus To, OrderActor By, FulfillmentType For)> Expected =
    [
        // A new order: the restaurant accepts or refuses it.
        (OrderStatus.Placed, OrderStatus.Accepted, OrderActor.Restaurant, FulfillmentType.Delivery),
        (OrderStatus.Placed, OrderStatus.Accepted, OrderActor.Restaurant, FulfillmentType.Pickup),
        (OrderStatus.Placed, OrderStatus.Rejected, OrderActor.Restaurant, FulfillmentType.Delivery),
        (OrderStatus.Placed, OrderStatus.Rejected, OrderActor.Restaurant, FulfillmentType.Pickup),

        // The customer may withdraw until cooking starts.
        (OrderStatus.Placed, OrderStatus.Cancelled, OrderActor.Customer, FulfillmentType.Delivery),
        (OrderStatus.Placed, OrderStatus.Cancelled, OrderActor.Customer, FulfillmentType.Pickup),
        (OrderStatus.Accepted, OrderStatus.Cancelled, OrderActor.Customer, FulfillmentType.Delivery),
        (OrderStatus.Accepted, OrderStatus.Cancelled, OrderActor.Customer, FulfillmentType.Pickup),

        // A restaurant that accepted and then cannot deliver. Carries a reason, so it lands in
        // the same report a rejection does.
        (OrderStatus.Accepted, OrderStatus.Cancelled, OrderActor.Restaurant, FulfillmentType.Delivery),
        (OrderStatus.Accepted, OrderStatus.Cancelled, OrderActor.Restaurant, FulfillmentType.Pickup),
        (OrderStatus.Preparing, OrderStatus.Cancelled, OrderActor.Restaurant, FulfillmentType.Delivery),
        (OrderStatus.Preparing, OrderStatus.Cancelled, OrderActor.Restaurant, FulfillmentType.Pickup),

        // The kitchen starts work.
        (OrderStatus.Accepted, OrderStatus.Preparing, OrderActor.Restaurant, FulfillmentType.Delivery),
        (OrderStatus.Accepted, OrderStatus.Preparing, OrderActor.Restaurant, FulfillmentType.Pickup),

        // The fork, and the handover. One branch each, never both.
        (OrderStatus.Preparing, OrderStatus.ReadyForPickup, OrderActor.Restaurant, FulfillmentType.Pickup),
        (OrderStatus.Preparing, OrderStatus.OutForDelivery, OrderActor.Restaurant, FulfillmentType.Delivery),
        (OrderStatus.ReadyForPickup, OrderStatus.Delivered, OrderActor.Restaurant, FulfillmentType.Pickup),
        (OrderStatus.OutForDelivery, OrderStatus.Delivered, OrderActor.Restaurant, FulfillmentType.Delivery),
    ];

    [Fact]
    public void Exactly_the_intended_moves_are_permitted_and_nothing_else()
    {
        var wronglyAllowed = new List<string>();
        var wronglyRefused = new List<string>();

        foreach (var from in Statuses)
        {
            foreach (var to in Statuses)
            {
                foreach (var by in Actors)
                {
                    foreach (var fulfillment in Fulfillments)
                    {
                        // hasReason is true throughout: this test is about which moves exist,
                        // and the reason rule is a separate question asked further down.
                        var permitted = OrderTransitions.Check(from, to, fulfillment, by, hasReason: true)
                            == Refusal.None;
                        var shouldBe = Expected.Contains((from, to, by, fulfillment));

                        if (permitted && !shouldBe)
                        {
                            wronglyAllowed.Add($"{from} -> {to} by {by} ({fulfillment})");
                        }
                        else if (!permitted && shouldBe)
                        {
                            wronglyRefused.Add($"{from} -> {to} by {by} ({fulfillment})");
                        }
                    }
                }
            }
        }

        wronglyAllowed.ShouldBeEmpty(
            "these moves are permitted but should not be: " + string.Join(", ", wronglyAllowed));
        wronglyRefused.ShouldBeEmpty(
            "these moves should be permitted but are refused: " + string.Join(", ", wronglyRefused));
    }

    [Fact]
    public void The_whole_cross_product_really_is_being_checked()
    {
        // Without this, deleting a status from the enum would shrink the sweep above and the
        // suite would still report success on a smaller problem.
        Statuses.Length.ShouldBe(8);
        Actors.Length.ShouldBe(2);
        Fulfillments.Length.ShouldBe(2);
        (Statuses.Length * Statuses.Length * Actors.Length * Fulfillments.Length).ShouldBe(256);
    }

    [Fact]
    public void The_declared_table_matches_the_expected_set()
    {
        // The same assertion from the other side: expand the table's optional fulfillment into
        // concrete rows and compare. Catches a duplicate or a row nothing reaches.
        var declared = OrderTransitions.All
            .SelectMany(t => t.OnlyFor is null
                ? Fulfillments.Select(f => (t.From, t.To, t.By, For: f))
                : [(t.From, t.To, t.By, For: t.OnlyFor.Value)])
            .ToHashSet();

        declared.ShouldBe(Expected, ignoreOrder: true);
        OrderTransitions.All.Count.ShouldBe(11, "eleven declared rows expand to eighteen concrete moves");
    }

    [Theory]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.Rejected)]
    [InlineData(OrderStatus.Cancelled)]
    public void A_finished_order_never_moves_again(OrderStatus terminal)
    {
        OrderTransitions.IsTerminal(terminal).ShouldBeTrue();

        foreach (var to in Statuses.Where(s => s != terminal))
        {
            foreach (var by in Actors)
            {
                foreach (var fulfillment in Fulfillments)
                {
                    OrderTransitions.Check(terminal, to, fulfillment, by, hasReason: true)
                        .ShouldBe(Refusal.Terminal, $"{terminal} is final, so {terminal} -> {to} must not happen");
                }
            }
        }
    }

    [Theory]
    [InlineData(OrderStatus.Placed)]
    [InlineData(OrderStatus.Accepted)]
    [InlineData(OrderStatus.Preparing)]
    [InlineData(OrderStatus.ReadyForPickup)]
    [InlineData(OrderStatus.OutForDelivery)]
    public void An_order_in_progress_is_not_terminal(OrderStatus status) =>
        OrderTransitions.IsTerminal(status).ShouldBeFalse();

    [Fact]
    public void Every_status_except_the_first_can_be_reached()
    {
        // A status nothing leads to is dead code in the enum, and would show up in reports as a
        // column that is always zero.
        var reachable = OrderTransitions.All.Select(t => t.To).ToHashSet();

        foreach (var status in Statuses.Where(s => s != OrderStatus.Placed))
        {
            reachable.ShouldContain(status, $"nothing can ever reach {status}");
        }

        reachable.ShouldNotContain(OrderStatus.Placed, "Placed is where an order starts, not somewhere it returns to");
    }

    [Fact]
    public void Every_unfinished_status_leads_somewhere()
    {
        // The opposite trap: a status an order can enter and never leave, which is a stuck order
        // and a phone call to support.
        var hasExit = OrderTransitions.All.Select(t => t.From).ToHashSet();

        foreach (var status in Statuses.Where(s => !OrderTransitions.IsTerminal(s)))
        {
            hasExit.ShouldContain(status, $"an order in {status} would be stuck there");
        }
    }
}
