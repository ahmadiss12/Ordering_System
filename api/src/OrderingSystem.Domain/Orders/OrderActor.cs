namespace OrderingSystem.Domain.Orders;

/// <summary>
/// Who is asking for a status change.
///
/// <para>
/// Not a role: a restaurant owner and a staff member are the same actor here, because the order
/// lifecycle does not distinguish them — both accept and both cook. Roles decide who may reach
/// the endpoint; this decides which moves make sense once they have.
/// </para>
/// <para>
/// Deliberately two members. A platform admin intervening in a live order is plausible but has
/// no defined behaviour yet, and a member nothing can produce is a branch nothing can test.
/// </para>
/// </summary>
public enum OrderActor
{
    Customer = 1,
    Restaurant = 2,
}
