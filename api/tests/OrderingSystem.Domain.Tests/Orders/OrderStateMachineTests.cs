using OrderingSystem.Domain.Enums;
using OrderingSystem.Domain.Exceptions;
using OrderingSystem.Domain.Orders;

namespace OrderingSystem.Domain.Tests.Orders;

/// <summary>
/// The guard, and specifically which failure it reports.
///
/// <para>
/// Which exception is thrown is not a detail: the API's one exception handler turns each into a
/// status code, so getting it wrong here means a permissions problem arriving at the browser as
/// a 409 and a form error arriving as a 403. Neither is something a screen can respond to well.
/// </para>
/// </summary>
public class OrderStateMachineTests
{
    [Fact]
    public void A_legal_move_is_simply_allowed()
    {
        var act = () => OrderStateMachine.EnsureAllowed(
            OrderStatus.Placed, OrderStatus.Accepted, FulfillmentType.Delivery, OrderActor.Restaurant);

        act.ShouldNotThrow();
    }

    [Fact]
    public void Doing_the_other_partys_job_is_a_permissions_failure()
    {
        // A customer pressing Accept. The move is real, the caller is not the one who makes it,
        // and 403 is the honest answer — 409 would suggest waiting and trying again.
        var act = () => OrderStateMachine.EnsureAllowed(
            OrderStatus.Placed, OrderStatus.Accepted, FulfillmentType.Delivery, OrderActor.Customer);

        var error = act.ShouldThrow<ForbiddenException>();
        error.Message.ShouldContain("restaurant");
    }

    [Fact]
    public void A_restaurant_cannot_cancel_on_the_customers_behalf()
    {
        var act = () => OrderStateMachine.EnsureAllowed(
            OrderStatus.Placed, OrderStatus.Cancelled, FulfillmentType.Pickup, OrderActor.Restaurant);

        act.ShouldThrow<ForbiddenException>().Message.ShouldContain("customer");
    }

    [Fact]
    public void Moving_a_finished_order_is_a_conflict()
    {
        var act = () => OrderStateMachine.EnsureAllowed(
            OrderStatus.Delivered, OrderStatus.Preparing, FulfillmentType.Delivery, OrderActor.Restaurant);

        act.ShouldThrow<ConflictException>().Message.ShouldContain("delivered");
    }

    [Fact]
    public void Pressing_the_same_button_twice_says_so_plainly()
    {
        // Two tablets in one kitchen, or one impatient thumb. The message has to read as "that
        // already happened" rather than as something being wrong.
        var act = () => OrderStateMachine.EnsureAllowed(
            OrderStatus.Accepted, OrderStatus.Accepted, FulfillmentType.Delivery, OrderActor.Restaurant);

        act.ShouldThrow<ConflictException>().Message.ShouldContain("already");
    }

    [Fact]
    public void A_pickup_order_is_never_sent_out_for_delivery()
    {
        var act = () => OrderStateMachine.EnsureAllowed(
            OrderStatus.Preparing, OrderStatus.OutForDelivery, FulfillmentType.Pickup, OrderActor.Restaurant);

        act.ShouldThrow<ConflictException>().Message.ShouldContain("pickup");
    }

    [Fact]
    public void A_delivery_order_is_never_left_on_the_counter()
    {
        var act = () => OrderStateMachine.EnsureAllowed(
            OrderStatus.Preparing, OrderStatus.ReadyForPickup, FulfillmentType.Delivery, OrderActor.Restaurant);

        act.ShouldThrow<ConflictException>().Message.ShouldContain("delivery");
    }

    [Fact]
    public void Rejecting_without_a_reason_is_an_unfinished_form()
    {
        var act = () => OrderStateMachine.EnsureAllowed(
            OrderStatus.Placed, OrderStatus.Rejected, FulfillmentType.Delivery, OrderActor.Restaurant);

        // 400 with a field name, not 409: nothing about the order is wrong, the request is
        // missing something the person can supply.
        var error = act.ShouldThrow<ValidationFailedException>();
        error.Errors.ShouldContainKey("reason");
    }

    [Fact]
    public void Rejecting_with_a_reason_goes_through()
    {
        var act = () => OrderStateMachine.EnsureAllowed(
            OrderStatus.Placed, OrderStatus.Rejected, FulfillmentType.Delivery, OrderActor.Restaurant,
            RejectionReason.OutOfStock);

        act.ShouldNotThrow();
    }

    [Fact]
    public void A_customer_changing_their_mind_needs_no_reason()
    {
        // Whether a reason is required depends on who is asking, not only on where to: a
        // restaurant dropping an order is reportable, a customer changing their mind is nobody's
        // business, and a form standing between them and the button would be rude.
        var act = () => OrderStateMachine.EnsureAllowed(
            OrderStatus.Accepted, OrderStatus.Cancelled, FulfillmentType.Delivery, OrderActor.Customer);

        act.ShouldNotThrow();
        OrderTransitions.RequiresReason(OrderStatus.Cancelled, OrderActor.Customer).ShouldBeFalse();
        OrderTransitions.RequiresReason(OrderStatus.Cancelled, OrderActor.Restaurant).ShouldBeTrue();
    }

    [Fact]
    public void Skipping_the_kitchen_is_refused()
    {
        // Placed straight to Delivered would leave an order with no record of being cooked.
        var act = () => OrderStateMachine.EnsureAllowed(
            OrderStatus.Placed, OrderStatus.Delivered, FulfillmentType.Pickup, OrderActor.Restaurant);

        act.ShouldThrow<ConflictException>();
    }

    [Fact]
    public void A_customer_cannot_cancel_once_cooking_has_started()
    {
        // The line the customer's cancellation right is drawn at: Accepted means somebody saw
        // it, Preparing means food is being made and somebody is out of pocket.
        var act = () => OrderStateMachine.EnsureAllowed(
            OrderStatus.Preparing, OrderStatus.Cancelled, FulfillmentType.Delivery, OrderActor.Customer);

        // A conflict rather than a 403: they could have done this a minute ago, so it is the
        // state that changed and not their permissions.
        var error = act.ShouldThrow<ConflictException>();
        error.Message.ShouldContain("already being prepared");
        error.Message.ShouldContain("Call the restaurant");
    }

    [Fact]
    public void A_restaurant_can_back_out_of_an_order_it_already_accepted()
    {
        // The gap step 1 left open. Without this an order sits in Preparing forever when the
        // power cuts, which is worse for everybody than a cancellation somebody can report on.
        var act = () => OrderStateMachine.EnsureAllowed(
            OrderStatus.Preparing, OrderStatus.Cancelled, FulfillmentType.Delivery,
            OrderActor.Restaurant, RejectionReason.OutOfStock);

        act.ShouldNotThrow();
    }

    [Fact]
    public void A_restaurant_backing_out_must_say_why()
    {
        var act = () => OrderStateMachine.EnsureAllowed(
            OrderStatus.Accepted, OrderStatus.Cancelled, FulfillmentType.Pickup, OrderActor.Restaurant);

        // Same report as a rejection: a restaurant that keeps dropping accepted orders is
        // exactly what the platform needs to be able to see.
        var error = act.ShouldThrow<ValidationFailedException>();
        error.Errors.ShouldContainKey("reason");
        error.Message.ShouldContain("Cancelling");
    }

    // ------------------------------------------------------------------ what a screen asks

    [Fact]
    public void A_kitchen_is_offered_only_the_moves_that_would_work()
    {
        OrderTransitions.NextFor(OrderStatus.Placed, FulfillmentType.Delivery, OrderActor.Restaurant)
            .ShouldBe([OrderStatus.Accepted, OrderStatus.Rejected], ignoreOrder: true);

        // Getting on with it, or backing out — both real once an order is accepted.
        OrderTransitions.NextFor(OrderStatus.Accepted, FulfillmentType.Delivery, OrderActor.Restaurant)
            .ShouldBe([OrderStatus.Preparing, OrderStatus.Cancelled], ignoreOrder: true);

        OrderTransitions.NextFor(OrderStatus.Preparing, FulfillmentType.Delivery, OrderActor.Restaurant)
            .ShouldBe([OrderStatus.OutForDelivery, OrderStatus.Cancelled], ignoreOrder: true);

        OrderTransitions.NextFor(OrderStatus.Preparing, FulfillmentType.Pickup, OrderActor.Restaurant)
            .ShouldBe([OrderStatus.ReadyForPickup, OrderStatus.Cancelled], ignoreOrder: true);

        // Once it is out of the building the kitchen only confirms the handover.
        OrderTransitions.NextFor(OrderStatus.OutForDelivery, FulfillmentType.Delivery, OrderActor.Restaurant)
            .ShouldBe([OrderStatus.Delivered]);
    }

    [Fact]
    public void A_customer_is_offered_cancelling_and_nothing_else()
    {
        OrderTransitions.NextFor(OrderStatus.Placed, FulfillmentType.Pickup, OrderActor.Customer)
            .ShouldBe([OrderStatus.Cancelled]);
        OrderTransitions.NextFor(OrderStatus.Accepted, FulfillmentType.Pickup, OrderActor.Customer)
            .ShouldBe([OrderStatus.Cancelled]);

        OrderTransitions.NextFor(OrderStatus.Preparing, FulfillmentType.Pickup, OrderActor.Customer)
            .ShouldBeEmpty("once cooking starts there is nothing for the customer to press");
    }

    [Fact]
    public void A_finished_order_offers_nobody_anything()
    {
        foreach (var actor in Enum.GetValues<OrderActor>())
        {
            foreach (var fulfillment in Enum.GetValues<FulfillmentType>())
            {
                OrderTransitions.NextFor(OrderStatus.Delivered, fulfillment, actor).ShouldBeEmpty();
                OrderTransitions.NextFor(OrderStatus.Cancelled, fulfillment, actor).ShouldBeEmpty();
                OrderTransitions.NextFor(OrderStatus.Rejected, fulfillment, actor).ShouldBeEmpty();
            }
        }
    }

    [Fact]
    public void Every_status_has_wording_a_person_would_recognise()
    {
        foreach (var status in Enum.GetValues<OrderStatus>())
        {
            var described = OrderStateMachine.Describe(status);

            described.ShouldNotBeNullOrWhiteSpace();
            // Dropped into the middle of a sentence, so it must not arrive capitalised.
            described.ShouldBe(described.ToLowerInvariant());
        }

        // The two that would read badly if left as enum names.
        OrderStateMachine.Describe(OrderStatus.Preparing).ShouldBe("being prepared");
        OrderStateMachine.Describe(OrderStatus.OutForDelivery).ShouldBe("out for delivery");
    }
}
