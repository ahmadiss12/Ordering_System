using Microsoft.EntityFrameworkCore;
using OrderingSystem.Application.Abstractions;
using OrderingSystem.Domain.Enums;
using OrderingSystem.Domain.Exceptions;
using OrderingSystem.Domain.Orders;
using OrderingSystem.Application.Features.Restaurants;

namespace OrderingSystem.Application.Features.Platform;

/// <summary>
/// The platform's side of the two-level split: every restaurant, what it is charged, and whether
/// customers can find it.
///
/// <para>
/// The one rule that matters here is who may call it. Every method opens with
/// <see cref="ITenantGuard.RequirePlatformAdmin"/> rather than the usual
/// <see cref="ITenantGuard.EnsureCanActFor"/>, because that one admits a restaurant acting on
/// itself — correct for a menu, and quite wrong for the rate the platform charges it. Without
/// this check the controller's policy would be the only thing between an owner and their own
/// commission, and one attribute is a thin place for that to live.
/// </para>
/// <para>
/// Nothing here edits a restaurant's own settings. A name, a phone number and a prep time belong
/// to the restaurant, and an admin quietly correcting one would be a different feature with
/// different consequences.
/// </para>
/// </summary>
public sealed class PlatformRestaurantsService(
    IAppDbContext db,
    ITenantGuard guard,
    IValidationService validation,
    IClock clock,
    StaffInvitations invitations)
{
    /// <summary>How long a link may be, matching the column. Kept here so slugs are trimmed to fit
    /// rather than refused by the database.</summary>
    private const int MaxSlugLength = 120;

    /// <summary>
    /// Takes a restaurant on to the platform and hands it to somebody.
    ///
    /// <para>
    /// Until this existed, onboarding began with a database insert: an admin could list, hide and
    /// price a restaurant, but the row itself had to be put there by hand. That made the last
    /// mile of the product a support task.
    /// </para>
    /// <para>
    /// It arrives in exactly the state a restaurant is in before anybody has set it up — hidden,
    /// no hours, no delivery zones, no menu — because those are the owner's to decide and the
    /// platform guessing at them would be worse than an empty screen. Hidden especially: a
    /// restaurant that appeared to customers the moment it was created would take orders it has
    /// no hours for and no way to deliver.
    /// </para>
    /// <para>
    /// The owner is invited through the same path a colleague is, so an address that already
    /// shops here keeps its account and its order history.
    /// </para>
    /// </summary>
    public async Task<CreatedRestaurantResponse> CreateAsync(
        CreateRestaurantRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        guard.RequirePlatformAdmin();
        await validation.ValidateAsync(request, ct);

        var slug = await ResolveSlugAsync(request, ct);
        var restaurantId = Guid.NewGuid();

        db.Restaurants.Add(new Domain.Restaurants.Restaurant
        {
            Id = restaurantId,
            Name = request.Name.Trim(),
            Slug = slug,
            Description = null,
            Phone = request.Phone.Trim(),

            // Hidden until its owner has set it up and the platform has looked at it.
            IsActive = false,

            // The restaurant's own switch starts on: it is the kitchen's to pause, and starting
            // it paused would leave an owner wondering which of two switches was the problem.
            IsAcceptingOrders = true,

            CommissionPercent = request.CommissionPercent,
            MinOrderUsd = 0m,
            DefaultPrepMinutes = DefaultPrepMinutes,
            CreatedAt = clock.UtcNow,
        });

        await db.SaveChangesAsync(ct);

        // allowExistingMembership stays false: an owner who already runs another restaurant
        // cannot run this one, for the same reason they cannot be invited to a second — a token
        // carries one restaurant and nothing lets its holder choose.
        var (_, emailed) = await invitations.InviteAsync(
            restaurantId,
            request.OwnerEmail,
            request.OwnerFullName,
            request.OwnerPhone,
            StaffRoleType.Owner,
            ct: ct);

        return new CreatedRestaurantResponse(await SingleAsync(restaurantId, ct), emailed);
    }

    /// <summary>What a new restaurant promises until its owner says otherwise.</summary>
    private const int DefaultPrepMinutes = 20;

    /// <summary>
    /// The link this restaurant will live at.
    ///
    /// <para>
    /// A collision is refused rather than resolved with a suffix. "beirut-mezze-house-2" is a link
    /// somebody has to live with forever, chosen by a computer in a moment nobody was watching —
    /// an admin told the name is taken can pick something they meant.
    /// </para>
    /// </summary>
    private async Task<string> ResolveSlugAsync(CreateRestaurantRequest request, CancellationToken ct)
    {
        var slug = string.IsNullOrWhiteSpace(request.Slug)
            ? Slugs.From(request.Name, MaxSlugLength)
            : request.Slug.Trim();

        if (slug.Length == 0)
        {
            // A name with no Latin letters in it — Arabic, most obviously. Nothing sensible can be
            // derived, and inventing one would saddle the restaurant with a link it never chose.
            throw new ConflictException(
                "A link could not be made from that name. Type the one this restaurant should use.");
        }

        if (await db.Restaurants.AnyAsync(r => r.Slug == slug, ct))
        {
            throw new ConflictException($"Another restaurant is already at /{slug}. Choose a different link.");
        }

        return slug;
    }


    /// <summary>
    /// Every restaurant, listed or not.
    ///
    /// <para>
    /// Deliberately not the public catalog, which filters to the active ones. A switched-off
    /// restaurant is invisible everywhere else on the platform, so if this list hid it too there
    /// would be no screen anywhere that could switch it back on.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<PlatformRestaurantResponse>> ListAsync(CancellationToken ct = default)
    {
        guard.RequirePlatformAdmin();

        return await db.Restaurants.AsNoTracking()
            .OrderBy(r => r.Name)
            .Select(r => new PlatformRestaurantResponse(
                r.Id,
                r.Name,
                r.Slug,
                r.Phone,
                r.IsActive,
                r.IsAcceptingOrders,
                r.CommissionPercent,
                r.Orders.Count(o => LiveStatuses.Contains(o.Status)),
                r.CreatedAt))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Changes what the platform charges, from the next order onwards.
    ///
    /// <para>
    /// Every order snapshots the rate it was placed under, so nothing here rewrites a settlement
    /// that has already happened. That is the property worth stating plainly, because a rate
    /// change that reached back through history would be indistinguishable from a bug and would
    /// only be noticed at the end of the month.
    /// </para>
    /// </summary>
    public async Task<PlatformRestaurantResponse> SetCommissionAsync(
        Guid restaurantId, SetCommissionRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        guard.RequirePlatformAdmin();
        await validation.ValidateAsync(request, ct);

        var restaurant = await LoadAsync(restaurantId, ct);

        restaurant.CommissionPercent = request.CommissionPercent;
        await db.SaveChangesAsync(ct);

        return await SingleAsync(restaurantId, ct);
    }

    /// <summary>
    /// Shows or hides a restaurant.
    ///
    /// <para>
    /// Hiding it stops customers finding it, quoting it, or ordering from it. It does not touch
    /// orders already placed, and it does not lock the restaurant's own staff out of their queue
    /// — people who are waiting for food they have paid for should get it, whatever the platform
    /// has decided about the listing.
    /// </para>
    /// </summary>
    public async Task<PlatformRestaurantResponse> SetListingAsync(
        Guid restaurantId, SetListingRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        guard.RequirePlatformAdmin();

        var restaurant = await LoadAsync(restaurantId, ct);

        restaurant.IsActive = request.IsActive;
        await db.SaveChangesAsync(ct);

        return await SingleAsync(restaurantId, ct);
    }

    /// <summary>
    /// What counts as still in flight, and so as somebody waiting for food.
    ///
    /// <para>
    /// Derived from the state machine's own list of finished statuses rather than written out
    /// again here. A second copy would be right until somebody added a status, and then it would
    /// be quietly wrong in a count nobody double-checks. Deriving it also picks the safer default:
    /// a status nobody declared terminal counts as somebody still waiting.
    /// </para>
    /// <para>
    /// An array rather than the frozen set it comes from, because this one goes into a query and
    /// EF turns an array into an IN clause.
    /// </para>
    /// </summary>
    private static readonly OrderStatus[] LiveStatuses =
        [.. Enum.GetValues<OrderStatus>().Where(status => !OrderTransitions.IsTerminal(status))];

    private async Task<Domain.Restaurants.Restaurant> LoadAsync(Guid restaurantId, CancellationToken ct) =>
        await db.Restaurants.FirstOrDefaultAsync(r => r.Id == restaurantId, ct)
        ?? throw new NotFoundException("No such restaurant.");

    private async Task<PlatformRestaurantResponse> SingleAsync(Guid restaurantId, CancellationToken ct) =>
        (await ListAsync(ct)).FirstOrDefault(r => r.Id == restaurantId)
        ?? throw new NotFoundException("No such restaurant.");
}
