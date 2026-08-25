using OrderingSystem.Domain.Enums;
using OrderingSystem.Domain.Orders;

namespace OrderingSystem.Domain.Tests.Lifecycle;

/// <summary>
/// The payoff of declaring the lifecycle as data: the whole cross-product can be walked and
/// pinned. Eight statuses by eight statuses by two fulfilment types is 128 combinations, and this
/// asserts which fourteen of them are legal — not "the ones somebody remembered to test".
/// </summary>
public class OrderTransitionTests
{
    [Fact]
    public void Exactly_the_intended_transitions_are_legal()
    {
        var actual =
            (from from in Enum.GetValues<OrderStatus>()
             from to in Enum.GetValues<OrderStatus>()
             from fulfillment in Enum.GetValues<FulfillmentType>()
             where OrderTransitions.IsAllowed(from, to, fulfillment)
             select $"{fulfillment}: {from} -> {to}")
            .OrderBy(edge => edge, StringComparer.Ordinal)
            .ToArray();

        string[] expected =
        [
            "Delivery: Accepted -> Cancelled",
            "Delivery: Accepted -> Preparing",
            "Delivery: OutForDelivery -> Delivered",
            "Delivery: Placed -> Accepted",
            "Delivery: Placed -> Cancelled",
            "Delivery: Placed -> Rejected",
            "Delivery: Preparing -> OutForDelivery",
            "Pickup: Accepted -> Cancelled",
            "Pickup: Accepted -> Preparing",
            "Pickup: Placed -> Accepted",
            "Pickup: Placed -> Cancelled",
            "Pickup: Placed -> Rejected",
            "Pickup: Preparing -> ReadyForPickup",
            "Pickup: ReadyForPickup -> Delivered",
        ];

        actual.ShouldBe(expected.OrderBy(edge => edge, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void A_delivery_order_never_becomes_ready_for_pickup()
    {
        OrderTransitions
            .IsAllowed(OrderStatus.Preparing, OrderStatus.ReadyForPickup, FulfillmentType.Delivery)
            .ShouldBeFalse();

        OrderTransitions
            .IsAllowed(OrderStatus.Preparing, OrderStatus.ReadyForPickup, FulfillmentType.Pickup)
            .ShouldBeTrue();
    }

    [Fact]
    public void A_pickup_order_never_goes_out_for_delivery()
    {
        OrderTransitions
            .IsAllowed(OrderStatus.Preparing, OrderStatus.OutForDelivery, FulfillmentType.Pickup)
            .ShouldBeFalse();

        OrderTransitions
            .IsAllowed(OrderStatus.Preparing, OrderStatus.OutForDelivery, FulfillmentType.Delivery)
            .ShouldBeTrue();
    }

    [Theory]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.Rejected)]
    [InlineData(OrderStatus.Cancelled)]
    public void Terminal_statuses_lead_nowhere(OrderStatus terminal)
    {
        OrderTransitions.IsTerminal(terminal).ShouldBeTrue();

        foreach (var fulfillment in Enum.GetValues<FulfillmentType>())
        {
            OrderTransitions.NextFrom(terminal, fulfillment).ShouldBeEmpty();
        }
    }

    [Theory]
    [InlineData(OrderStatus.Placed)]
    [InlineData(OrderStatus.Accepted)]
    [InlineData(OrderStatus.Preparing)]
    [InlineData(OrderStatus.ReadyForPickup)]
    [InlineData(OrderStatus.OutForDelivery)]
    public void A_live_status_is_not_terminal(OrderStatus live) =>
        OrderTransitions.IsTerminal(live).ShouldBeFalse();

    [Fact]
    public void Next_from_lists_only_what_this_fulfilment_type_permits()
    {
        OrderTransitions.NextFrom(OrderStatus.Preparing, FulfillmentType.Delivery)
            .ShouldBe(new[] { OrderStatus.OutForDelivery });

        OrderTransitions.NextFrom(OrderStatus.Preparing, FulfillmentType.Pickup)
            .ShouldBe(new[] { OrderStatus.ReadyForPickup });

        // Ordered by the enum's numeric value, so the client gets a stable sequence to render.
        OrderTransitions.NextFrom(OrderStatus.Placed, FulfillmentType.Delivery)
            .ShouldBe(new[] { OrderStatus.Accepted, OrderStatus.Rejected, OrderStatus.Cancelled });
    }

    [Fact]
    public void No_status_may_transition_to_itself()
    {
        foreach (var status in Enum.GetValues<OrderStatus>())
        {
            foreach (var fulfillment in Enum.GetValues<FulfillmentType>())
            {
                OrderTransitions.IsAllowed(status, status, fulfillment)
                    .ShouldBeFalse($"{status} -> {status} is not a transition");
            }
        }
    }
}
