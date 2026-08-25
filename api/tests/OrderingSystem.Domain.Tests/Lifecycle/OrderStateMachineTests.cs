using OrderingSystem.Domain.Enums;
using OrderingSystem.Domain.Exceptions;
using OrderingSystem.Domain.Orders;

namespace OrderingSystem.Domain.Tests.Lifecycle;

/// <summary>
/// What applying a transition does, as distinct from which transitions exist. No database, no
/// HTTP, no mocking framework — the state machine takes the moment as an argument precisely so
/// these run as plain arithmetic.
/// </summary>
public class OrderStateMachineTests
{
    private static readonly DateTimeOffset Noon =
        new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_transition_records_both_ends_and_who_made_it()
    {
        var order = NewOrder(OrderStatus.Placed);
        var staffId = Guid.NewGuid();

        var moment = OrderStateMachine.Transition(order, OrderStatus.Accepted, Noon, staffId);

        order.Status.ShouldBe(OrderStatus.Accepted);
        moment.FromStatus.ShouldBe(OrderStatus.Placed);
        moment.ToStatus.ShouldBe(OrderStatus.Accepted);
        moment.ChangedByUserId.ShouldBe(staffId);
        moment.CreatedAt.ShouldBe(Noon);
        moment.OrderId.ShouldBe(order.Id);

        // Appended to the order, so one SaveChanges writes the status and its event together.
        order.Events.ShouldHaveSingleItem().ShouldBe(moment);
    }

    [Fact]
    public void An_illegal_transition_is_refused_and_changes_nothing()
    {
        var order = NewOrder(OrderStatus.Placed);

        var thrown = Should.Throw<ConflictException>(() =>
            OrderStateMachine.Transition(order, OrderStatus.Delivered, Noon, null));

        // The message names what would have worked, so the caller is not sent to the source.
        thrown.Message.ShouldContain("Accepted");

        order.Status.ShouldBe(OrderStatus.Placed);
        order.Events.ShouldBeEmpty();
    }

    [Fact]
    public void Repeating_a_transition_says_so_rather_than_blaming_the_table()
    {
        var order = NewOrder(OrderStatus.Accepted);

        var thrown = Should.Throw<ConflictException>(() =>
            OrderStateMachine.Transition(order, OrderStatus.Accepted, Noon, null));

        thrown.Message.ShouldContain("already");
    }

    [Fact]
    public void A_final_status_cannot_change_again()
    {
        var order = NewOrder(OrderStatus.Delivered);

        Should.Throw<ConflictException>(() =>
                OrderStateMachine.Transition(order, OrderStatus.Preparing, Noon, null))
            .Message.ShouldContain("final");
    }

    [Fact]
    public void Rejecting_without_a_reason_is_refused_with_the_field_named()
    {
        var order = NewOrder(OrderStatus.Placed);

        var thrown = Should.Throw<ValidationFailedException>(() =>
            OrderStateMachine.Transition(order, OrderStatus.Rejected, Noon, null));

        thrown.Errors.Keys.ShouldContain("rejectionReason");
        order.Status.ShouldBe(OrderStatus.Placed);
    }

    [Fact]
    public void Rejecting_with_a_reason_records_the_reason_and_the_note()
    {
        var order = NewOrder(OrderStatus.Placed);

        OrderStateMachine.Transition(
            order, OrderStatus.Rejected, Noon, null, RejectionReason.OutOfStock, "No patties left.");

        order.Status.ShouldBe(OrderStatus.Rejected);
        order.RejectionReason.ShouldBe(RejectionReason.OutOfStock);
        order.RejectionNote.ShouldBe("No patties left.");
    }

    [Fact]
    public void A_reason_supplied_on_a_non_rejection_is_refused()
    {
        var order = NewOrder(OrderStatus.Placed);

        Should.Throw<ValidationFailedException>(() =>
            OrderStateMachine.Transition(
                order, OrderStatus.Accepted, Noon, null, RejectionReason.TooBusy));
    }

    [Fact]
    public void A_cash_order_is_paid_on_delivery()
    {
        var order = NewOrder(OrderStatus.OutForDelivery);
        order.PaymentMethod = PaymentMethod.CashOnDelivery;
        order.PaymentStatus = PaymentStatus.Pending;

        OrderStateMachine.Transition(order, OrderStatus.Delivered, Noon, null);

        order.PaymentStatus.ShouldBe(PaymentStatus.Paid);
    }

    [Fact]
    public void A_refunded_cash_order_is_not_restated_as_paid_by_delivery()
    {
        var order = NewOrder(OrderStatus.OutForDelivery);
        order.PaymentMethod = PaymentMethod.CashOnDelivery;
        order.PaymentStatus = PaymentStatus.Refunded;

        OrderStateMachine.Transition(order, OrderStatus.Delivered, Noon, null);

        order.PaymentStatus.ShouldBe(PaymentStatus.Refunded);
    }

    [Fact]
    public void An_online_order_already_paid_is_untouched_by_delivery()
    {
        var order = NewOrder(OrderStatus.OutForDelivery);
        order.PaymentMethod = PaymentMethod.Online;
        order.PaymentStatus = PaymentStatus.Paid;

        OrderStateMachine.Transition(order, OrderStatus.Delivered, Noon, null);

        order.PaymentStatus.ShouldBe(PaymentStatus.Paid);
    }

    [Fact]
    public void A_pickup_order_walks_its_whole_happy_path()
    {
        var order = NewOrder(OrderStatus.Placed, FulfillmentType.Pickup);

        OrderStateMachine.Transition(order, OrderStatus.Accepted, Noon, null);
        OrderStateMachine.Transition(order, OrderStatus.Preparing, Noon, null);
        OrderStateMachine.Transition(order, OrderStatus.ReadyForPickup, Noon, null);
        OrderStateMachine.Transition(order, OrderStatus.Delivered, Noon, null);

        order.Status.ShouldBe(OrderStatus.Delivered);

        // Four moves, four rows. This history is what makes average prep time derivable.
        order.Events.Count.ShouldBe(4);
    }

    [Fact]
    public void A_delivery_order_walks_its_whole_happy_path()
    {
        var order = NewOrder(OrderStatus.Placed);

        OrderStateMachine.Transition(order, OrderStatus.Accepted, Noon, null);
        OrderStateMachine.Transition(order, OrderStatus.Preparing, Noon, null);
        OrderStateMachine.Transition(order, OrderStatus.OutForDelivery, Noon, null);
        OrderStateMachine.Transition(order, OrderStatus.Delivered, Noon, null);

        order.Status.ShouldBe(OrderStatus.Delivered);
        order.Events.Count.ShouldBe(4);
    }

    private static Order NewOrder(
        OrderStatus status, FulfillmentType fulfillment = FulfillmentType.Delivery) => new()
    {
        Id = Guid.NewGuid(),
        OrderNumber = "FL-042",
        CustomerId = Guid.NewGuid(),
        RestaurantId = Guid.NewGuid(),
        FulfillmentType = fulfillment,
        Status = status,
        PaymentMethod = PaymentMethod.CashOnDelivery,
        PaymentStatus = PaymentStatus.Pending,
        PlacedAt = Noon,
    };
}
