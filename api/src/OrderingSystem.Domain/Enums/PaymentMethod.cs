namespace OrderingSystem.Domain.Enums;

/// <summary>
/// Also decides which way commission flows. On <see cref="CashOnDelivery"/> the restaurant holds
/// the cash and owes the platform its commission; on <see cref="Online"/> the platform holds the
/// money and owes the restaurant the rest.
/// </summary>
public enum PaymentMethod
{
    CashOnDelivery = 1,
    Online = 2,
}
