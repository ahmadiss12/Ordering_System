using Microsoft.EntityFrameworkCore;
using OrderingSystem.Application.Abstractions;
using OrderingSystem.Domain.Exceptions;
using OrderingSystem.Domain.Restaurants;

namespace OrderingSystem.Application.Features.Restaurants;

/// <summary>
/// When a restaurant says it is open.
///
/// <para>
/// The one screen in this phase with real domain logic behind it. <see cref="OpeningHours"/> has
/// existed since Phase 2 — several windows a day, windows that run past midnight, a window opened
/// yesterday that has not closed yet — and until now it has only ever been fed seed data.
/// </para>
/// </summary>
public sealed class OpeningHoursService(
    IAppDbContext db, ITenantGuard guard, IValidationService validation, IClock clock)
{
    public async Task<WeeklyHoursResponse> GetAsync(CancellationToken ct = default)
    {
        var restaurantId = guard.RequireRestaurantId();
        var hours = await LoadAsync(restaurantId, ct);

        return Describe(hours);
    }

    public async Task<WeeklyHoursResponse> SetAsync(
        SetWeeklyHoursRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await validation.ValidateAsync(request, ct);

        var restaurantId = guard.RequireRestaurantId();

        if (request.Windows.Count == 0 && !request.ConfirmClosedIndefinitely)
        {
            // A week with nothing in it shuts the restaurant to customers indefinitely. That is a
            // real thing to want and also what a screen produces halfway through an edit, so the
            // caller has to say which this is.
            throw new ValidationFailedException(
                "Removing every window closes you to customers until you add some back.",
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["windows"] = ["Confirm that you mean to close indefinitely."],
                });
        }

        var proposed = request.Windows
            .Select(w => new RestaurantHours
            {
                Id = Guid.NewGuid(),
                RestaurantId = restaurantId,
                DayOfWeek = w.Day,
                OpenTime = w.OpenTime,
                CloseTime = w.CloseTime,
            })
            .ToList();

        // Checked before anything is written, and against the whole week rather than a day at a
        // time — a window that runs past midnight belongs to two days.
        if (OpeningHours.FindOverlap(proposed) is { } clash)
        {
            throw new ValidationFailedException(
                $"{Describe(clash.First)} and {Describe(clash.Second)} cover the same time.",
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["windows"] = ["Two opening windows overlap."],
                });
        }

        // Replaced whole rather than reconciled row by row. These rows have no identity anybody
        // refers to — nothing points at an opening window — so keeping ids stable would buy
        // nothing and a diff would be a second place for the set to go wrong.
        db.RestaurantHours.RemoveRange(
            await db.RestaurantHours.Where(h => h.RestaurantId == restaurantId).ToListAsync(ct));

        foreach (var window in proposed)
        {
            db.RestaurantHours.Add(window);
        }

        await db.SaveChangesAsync(ct);

        return Describe(proposed);
    }

    private async Task<List<RestaurantHours>> LoadAsync(Guid restaurantId, CancellationToken ct) =>
        await db.RestaurantHours.AsNoTracking()
            .Where(h => h.RestaurantId == restaurantId)
            .ToListAsync(ct);

    private WeeklyHoursResponse Describe(List<RestaurantHours> hours)
    {
        // Wall-clock in the restaurant's own timezone, the same reading the checkout takes.
        var local = clock.LocalNow;

        var windows = hours
            .OrderBy(h => ((int)h.DayOfWeek + 6) % 7)
            .ThenBy(h => h.OpenTime)
            .Select(h => new OpeningWindow(h.DayOfWeek, h.OpenTime, h.CloseTime))
            .ToList();

        return new WeeklyHoursResponse(
            windows,
            OpeningHours.IsOpenAt(hours, local.DayOfWeek, TimeOnly.FromDateTime(local.DateTime)),
            hours.Count == 0);
    }

    private static string Describe(RestaurantHours window) =>
        $"{window.DayOfWeek} {window.OpenTime:HH\\:mm}–{window.CloseTime:HH\\:mm}";
}
