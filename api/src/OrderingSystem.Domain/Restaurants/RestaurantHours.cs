namespace OrderingSystem.Domain.Restaurants;

/// <summary>
/// One opening window on one weekday. Several rows per day are allowed, which is how a kitchen
/// that shuts between lunch and dinner is represented — a single open/close pair per day could
/// not express it.
/// </summary>
public class RestaurantHours
{
    public Guid Id { get; set; }
    public Guid RestaurantId { get; set; }

    public DayOfWeek DayOfWeek { get; set; }

    /// <summary>Local restaurant wall-clock time, not UTC. A kitchen opens at 11am regardless of offset.</summary>
    public TimeOnly OpenTime { get; set; }

    /// <summary>
    /// May be earlier than <see cref="OpenTime"/>, meaning the window runs past midnight —
    /// 18:00 to 02:00 is one row, not two.
    /// </summary>
    public TimeOnly CloseTime { get; set; }

    public Restaurant Restaurant { get; set; } = null!;
}
