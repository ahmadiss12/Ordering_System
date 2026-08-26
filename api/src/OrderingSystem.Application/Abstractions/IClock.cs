namespace OrderingSystem.Application.Abstractions;

/// <summary>
/// The current time, as a dependency. Nothing in the application calls DateTimeOffset.UtcNow
/// directly, because a token expiry, a stale-order threshold and an opening-hours check are all
/// untestable if the clock cannot be moved.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }

    /// <summary>
    /// Now, in the restaurant's own timezone. Opening hours are wall-clock: a kitchen opens at
    /// 11am regardless of what offset the server happens to run in.
    /// </summary>
    DateTimeOffset LocalNow { get; }

    /// <summary>
    /// Today's date in the restaurant's own timezone. Order numbering and opening hours are
    /// local-calendar concepts, not UTC ones.
    /// </summary>
    DateOnly LocalToday { get; }
}
