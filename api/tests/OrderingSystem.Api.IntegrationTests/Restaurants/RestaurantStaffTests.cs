using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OrderingSystem.Api.IntegrationTests.Auth;
using OrderingSystem.Api.IntegrationTests.Persistence;
using OrderingSystem.Application.Features.Auth;
using OrderingSystem.Application.Common;
using OrderingSystem.Application.Features.Cart;
using OrderingSystem.Application.Features.Orders;
using OrderingSystem.Application.Features.Restaurants;
using OrderingSystem.Domain.Enums;
using OrderingSystem.Infrastructure.Persistence.Seed;

namespace OrderingSystem.Api.IntegrationTests.Restaurants;

/// <summary>
/// Who works at a restaurant.
///
/// <para>
/// A row on this list is what puts the restaurant_id claim in somebody's token, so these tests
/// are less about a list rendering and more about what each change grants and takes away. Where
/// it is possible to follow a change through to somebody signing in and reading orders, that is
/// what is asserted, because the row is not the point — the access is.
/// </para>
/// <para>
/// Every test invites a freshly generated address and removes what it created. The restaurant is
/// shared with the rest of the suite, and a staff list left with an extra owner on it would
/// change what the last-owner tests are even testing.
/// </para>
/// </summary>
public sealed class RestaurantStaffTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Owner = "owner@shawarma.test";
    private const string Slug = "shawarma-station";

    // ------------------------------------------------------------------ reading

    [Fact]
    public async Task An_owner_sees_their_own_restaurants_staff()
    {
        var owner = await SignInAsync(Owner);

        var staff = await ListAsync(owner);

        staff.ShouldContain(m => m.Email == Owner);
        staff.ShouldAllBe(m => m.Email != "owner@frieslab.test");
        staff.Single(m => m.Email == Owner).IsYou.ShouldBeTrue();
    }

    [Fact]
    public async Task A_staff_member_cannot_read_the_staff_list()
    {
        var staff = await SignInAsync("staff@frieslab.test");

        // Deliberately owner-only, reading included. This list is the set of people who can see
        // every customer's address and phone number, and a cook has no reason to enumerate it.
        (await staff.GetAsync("/api/restaurant/staff", Ct)).StatusCode
            .ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_customer_cannot_read_a_staff_list()
    {
        var customer = await SignInAsync("rita@example.test");

        (await customer.GetAsync("/api/restaurant/staff", Ct)).StatusCode
            .ShouldBe(HttpStatusCode.Forbidden);
    }

    // ------------------------------------------------------------------ inviting

    [Fact]
    public async Task Inviting_an_unknown_address_creates_an_account_and_emails_a_link()
    {
        var owner = await SignInAsync(Owner);
        var email = NewEmail();

        try
        {
            var invited = await InvitationAsync(owner, email, StaffRoleType.Staff);

            invited.Member.Email.ShouldBe(email);
            invited.Member.MustSetPassword.ShouldBeTrue("they have not chosen a password yet");
            invited.InvitationEmailed.ShouldBeTrue();

            var sent = factory.Emails.Sent.Last(m => m.To == email);
            sent.Body.ShouldContain("reset-password?token=");
            sent.Subject.ShouldContain("Shawarma Station");
        }
        finally
        {
            await ForgetAsync(email);
        }
    }

    [Fact]
    public async Task An_invited_account_cannot_be_signed_into_until_the_link_is_used()
    {
        var owner = await SignInAsync(Owner);
        var email = NewEmail();

        try
        {
            await InviteAsync(owner, email, StaffRoleType.Staff);

            // The account is created with a hash of a discarded secret rather than a known
            // placeholder, so there is no password to guess - not even by whoever invited them.
            var attempt = await factory.CreateClient().PostAsJsonAsync("/api/auth/login",
                new LoginRequest(email, DatabaseSeeder.SeedPassword), Ct);

            attempt.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }
        finally
        {
            await ForgetAsync(email);
        }
    }

    [Fact]
    public async Task Accepting_an_invitation_grants_the_restaurant()
    {
        var owner = await SignInAsync(Owner);
        var email = NewEmail();

        try
        {
            await InviteAsync(owner, email, StaffRoleType.Staff);

            var hired = await AcceptInvitationAsync(email);

            // The whole point, followed all the way through: the link works, and what it leads to
            // is a person who can read the kitchen's queue.
            var queue = await hired.GetAsync("/api/restaurant/orders?page=1&pageSize=1", Ct);
            queue.StatusCode.ShouldBe(HttpStatusCode.OK);

            // ...and no further. Staff, not owner.
            (await hired.GetAsync("/api/restaurant/staff", Ct)).StatusCode
                .ShouldBe(HttpStatusCode.Forbidden);
        }
        finally
        {
            await ForgetAsync(email);
        }
    }

    [Fact]
    public async Task Accepting_an_invitation_clears_the_pending_flag()
    {
        var owner = await SignInAsync(Owner);
        var email = NewEmail();

        try
        {
            await InviteAsync(owner, email, StaffRoleType.Staff);
            await AcceptInvitationAsync(email);

            var listed = (await ListAsync(owner)).Single(m => m.Email == email);

            // Otherwise the list would show a colleague who has been working for a month as
            // "invited", and an owner would keep re-sending a link they do not need.
            listed.MustSetPassword.ShouldBeFalse();
        }
        finally
        {
            await ForgetAsync(email);
        }
    }

    [Fact]
    public async Task Inviting_an_existing_customer_keeps_their_account_and_their_orders()
    {
        var owner = await SignInAsync(Owner);
        var email = NewEmail();

        var customer = await RegisterAsync(email);

        // Ordered from a restaurant they are NOT about to be hired by, which is what makes the
        // assertion below mean something. An order at Shawarma Station would still be visible
        // through the restaurant half of the query filter once they work there, so it would pass
        // whether or not a staff member can see their own orders.
        await PlaceOrderAsync(email, MezzeSlug);
        var before = await OrderCountAsync(email);
        before.ShouldBe(1, "the test proves nothing about a history that does not exist");

        try
        {
            var invited = await InvitationAsync(owner, email, StaffRoleType.Staff);

            invited.Member.MustSetPassword.ShouldBeFalse("they already have a password");

            // Nothing was sent that anybody is waiting for, and the screen has to be able to say
            // so rather than promise a link that is never coming.
            invited.InvitationEmailed.ShouldBeFalse();
            factory.Emails.Sent.Last(m => m.To == email).Body
                .ShouldNotContain("reset-password?token=", Case.Sensitive);

            // The reason this matters more than tidiness: a second account would strand their
            // order history on an address they can no longer sign in with. Same user id, same
            // orders, and the query filter still shows them their own now that being staff
            // somewhere no longer costs them that.
            var afterHiring = await SignInAsync(email, "Passw0rd123");
            var mine = await afterHiring.GetFromJsonAsync<PagedResult<OrderSummaryResponse>>("/api/orders", Ct);

            mine!.TotalCount.ShouldBe(before);
            (await UserIdAsync(email)).ShouldBe(customer);
        }
        finally
        {
            await ForgetAsync(email);
        }
    }

    [Fact]
    public async Task A_mail_server_that_is_down_does_not_undo_the_invitation()
    {
        var owner = await SignInAsync(Owner);
        var email = NewEmail();

        try
        {
            factory.Emails.FailNextSend();

            // Found by running it: with no mail server listening, this threw out of the sender
            // and the owner got "an unexpected error occurred" - for an operation that had
            // already committed and handed somebody the restaurant's entire order book.
            var invited = await InvitationAsync(owner, email, StaffRoleType.Staff);

            invited.InvitationEmailed.ShouldBeFalse("nothing was sent, and saying so is the point");
            invited.Member.MustSetPassword.ShouldBeTrue("so the screen knows they still need a link");

            // On the list, which is what makes reporting failure the wrong answer: the next
            // refresh would have contradicted the error.
            (await ListAsync(owner)).ShouldContain(m => m.Email == email);
        }
        finally
        {
            await ForgetAsync(email);
        }
    }

    [Fact]
    public async Task A_lost_invitation_can_be_sent_again_by_removing_and_reinviting()
    {
        var owner = await SignInAsync(Owner);
        var email = NewEmail();

        try
        {
            var first = await InviteAsync(owner, email, StaffRoleType.Staff);
            var firstLink = factory.Emails.Sent.Last(m => m.To == email).Body;

            // There is no resend button, so this is the recovery path for a link that never
            // arrived. It works because the half-finished account survives being taken off the
            // list: the second invitation finds it still waiting for a password and sends a
            // fresh link rather than treating it as a colleague who already has one.
            await RemoveAsync(owner, first.UserId);
            var second = await InviteAsync(owner, email, StaffRoleType.Staff);

            second.UserId.ShouldBe(first.UserId);
            second.MustSetPassword.ShouldBeTrue();

            var secondLink = factory.Emails.Sent.Last(m => m.To == email).Body;
            secondLink.ShouldContain("reset-password?token=");
            secondLink.ShouldNotBe(firstLink, "a fresh link, not the same one again");

            await AcceptInvitationAsync(email);
        }
        finally
        {
            await ForgetAsync(email);
        }
    }

    [Fact]
    public async Task Inviting_somebody_already_on_the_list_is_refused()
    {
        var owner = await SignInAsync(Owner);

        var response = await owner.PostAsJsonAsync("/api/restaurant/staff",
            new InviteStaffRequest(Owner, "Layla Again", null, StaffRoleType.Staff), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Inviting_somebody_who_works_at_another_restaurant_is_refused()
    {
        var owner = await SignInAsync(Owner);

        var response = await owner.PostAsJsonAsync("/api/restaurant/staff",
            new InviteStaffRequest("staff@frieslab.test", "Sami", null, StaffRoleType.Staff), Ct);

        // Not a policy so much as an admission: a token carries one restaurant_id and there is no
        // way for its holder to say which. A second membership would let SQL Server decide.
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("another restaurant");
    }

    [Fact]
    public async Task An_address_that_is_not_an_address_is_refused()
    {
        var owner = await SignInAsync(Owner);

        var response = await owner.PostAsJsonAsync("/api/restaurant/staff",
            new InviteStaffRequest("not-an-email", "Nobody", null, StaffRoleType.Staff), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ------------------------------------------------------------------ roles

    [Fact]
    public async Task Promoting_somebody_gives_them_the_owner_screens()
    {
        var owner = await SignInAsync(Owner);
        var email = NewEmail();

        try
        {
            var invited = await InviteAsync(owner, email, StaffRoleType.Staff);
            var hired = await AcceptInvitationAsync(email);

            (await hired.GetAsync("/api/restaurant/staff", Ct)).StatusCode
                .ShouldBe(HttpStatusCode.Forbidden);

            await SetRoleAsync(owner, invited.UserId, StaffRoleType.Owner);

            // Signing in again, because the role is in the token rather than read per request.
            // This is the assertion that catches the two role systems drifting apart: the staff
            // row says Owner, and if UserRoles still says RestaurantStaff the policy refuses.
            var promoted = await SignInAsync(email, "Chosen-Passw0rd");
            (await promoted.GetAsync("/api/restaurant/staff", Ct)).StatusCode
                .ShouldBe(HttpStatusCode.OK);
        }
        finally
        {
            await ForgetAsync(email);
        }
    }

    [Fact]
    public async Task Demoting_somebody_takes_the_owner_screens_away_again()
    {
        var owner = await SignInAsync(Owner);
        var email = NewEmail();

        try
        {
            var invited = await InviteAsync(owner, email, StaffRoleType.Owner);
            var hired = await AcceptInvitationAsync(email);

            (await hired.GetAsync("/api/restaurant/staff", Ct)).StatusCode
                .ShouldBe(HttpStatusCode.OK);

            await SetRoleAsync(owner, invited.UserId, StaffRoleType.Staff);

            var demoted = await SignInAsync(email, "Chosen-Passw0rd");
            (await demoted.GetAsync("/api/restaurant/staff", Ct)).StatusCode
                .ShouldBe(HttpStatusCode.Forbidden);
        }
        finally
        {
            await ForgetAsync(email);
        }
    }

    [Fact]
    public async Task The_last_owner_cannot_be_demoted()
    {
        var owner = await SignInAsync(Owner);
        var me = (await ListAsync(owner)).Single(m => m.Email == Owner);

        var response = await owner.PutAsJsonAsync($"/api/restaurant/staff/{me.UserId}/role",
            new SetStaffRoleRequest(StaffRoleType.Staff), Ct);

        // Restored unconditionally. If the rule ever breaks, this test has just demoted the owner
        // every other test in the class signs in as, and the real failure would be buried under a
        // dozen unrelated ones.
        await RestoreOwnerAsync(Owner);

        // A restaurant with no owner cannot set a fee, edit its hours, or invite anybody who
        // could put an owner back. It would need platform support to recover from one click.
        //
        // Exactly 409, not "either of two plausible refusals". This test was written accepting
        // 403 as well, and it passed with the rule broken: a separate ban on acting on your own
        // account was refusing first, so the count was never reached.
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ListAsync(owner)).Single(m => m.Email == Owner).StaffRole
            .ShouldBe(StaffRoleType.Owner);
    }

    [Fact]
    public async Task An_owner_can_step_back_once_somebody_else_is_an_owner()
    {
        var owner = await SignInAsync(Owner);
        var email = NewEmail();
        var me = (await ListAsync(owner)).Single(m => m.Email == Owner);

        try
        {
            // Handing over, which is the whole reason self-demotion is allowed: promote your
            // successor, then step back. Needing a second person for the second half of that
            // would be a strange thing to insist on.
            await InviteAsync(owner, email, StaffRoleType.Owner);
            await SetRoleAsync(owner, me.UserId, StaffRoleType.Staff);

            var steppedBack = await SignInAsync(Owner);
            (await steppedBack.GetAsync("/api/restaurant/staff", Ct)).StatusCode
                .ShouldBe(HttpStatusCode.Forbidden);
        }
        finally
        {
            await RestoreOwnerAsync(Owner);
            await ForgetAsync(email);
        }
    }

    // ------------------------------------------------------------------ removing

    [Fact]
    public async Task Removing_somebody_ends_their_access_and_their_sessions()
    {
        var owner = await SignInAsync(Owner);
        var email = NewEmail();

        try
        {
            var invited = await InviteAsync(owner, email, StaffRoleType.Staff);
            var accepted = await AcceptTokensAsync(email);
            var hired = Authorized(accepted.AccessToken);

            (await hired.GetAsync("/api/restaurant/orders?page=1&pageSize=1", Ct)).StatusCode
                .ShouldBe(HttpStatusCode.OK);

            await RemoveAsync(owner, invited.UserId);

            // Their refresh token is dead, so they cannot mint a new one at all...
            var refreshed = await factory.CreateClient().PostAsJsonAsync("/api/auth/refresh",
                new RefreshRequest(accepted.RefreshToken), Ct);
            refreshed.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

            // ...and signing in afresh gets a token with no restaurant on it.
            var stranger = await SignInAsync(email, "Chosen-Passw0rd");
            (await stranger.GetAsync("/api/restaurant/orders?page=1&pageSize=1", Ct)).StatusCode
                .ShouldBe(HttpStatusCode.Forbidden);
        }
        finally
        {
            await ForgetAsync(email);
        }
    }

    [Fact]
    public async Task Removing_somebody_keeps_their_account_and_their_orders()
    {
        var owner = await SignInAsync(Owner);
        var email = NewEmail();

        var customer = await RegisterAsync(email);
        await PlaceOrderAsync(email, MezzeSlug);

        try
        {
            var invited = await InviteAsync(owner, email, StaffRoleType.Staff);
            await RemoveAsync(owner, invited.UserId);

            // Leaving a job is not leaving the platform. The account survives, and so does every
            // order it placed - orders have to keep resolving whoever placed them.
            (await UserIdAsync(email)).ShouldBe(customer);

            var stillACustomer = await SignInAsync(email, "Passw0rd123");
            var mine = await stillACustomer.GetFromJsonAsync<PagedResult<OrderSummaryResponse>>("/api/orders", Ct);

            mine!.TotalCount.ShouldBe(1);
        }
        finally
        {
            await ForgetAsync(email);
        }
    }

    [Fact]
    public async Task The_last_owner_cannot_be_removed()
    {
        var owner = await SignInAsync(Owner);
        var me = (await ListAsync(owner)).Single(m => m.Email == Owner);

        var response = await owner.DeleteAsync($"/api/restaurant/staff/{me.UserId}", Ct);

        await RestoreOwnerAsync(Owner);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ListAsync(owner)).ShouldContain(m => m.Email == Owner);
    }

    [Fact]
    public async Task An_owner_can_resign_once_somebody_else_is_an_owner()
    {
        var owner = await SignInAsync(Owner);
        var email = NewEmail();
        var me = (await ListAsync(owner)).Single(m => m.Email == Owner);

        try
        {
            await InviteAsync(owner, email, StaffRoleType.Owner);
            await RemoveAsync(owner, me.UserId);

            // Off the list, and their sessions ended with everybody else's - resigning is not a
            // special case that skips the part where access stops.
            var former = await SignInAsync(Owner);
            (await former.GetAsync("/api/restaurant/orders?page=1&pageSize=1", Ct)).StatusCode
                .ShouldBe(HttpStatusCode.Forbidden);
        }
        finally
        {
            await RestoreOwnerAsync(Owner);
            await ForgetAsync(email);
        }
    }

    [Fact]
    public async Task One_restaurant_cannot_remove_anothers_staff()
    {
        var shawarma = await SignInAsync(Owner);
        var friesLab = await SignInAsync("owner@frieslab.test");

        var theirs = (await ListAsync(friesLab)).First(m => m.StaffRole == StaffRoleType.Staff);

        // Not forbidden - not found. The query filter hides the row, and saying "that is not
        // yours" would confirm to a stranger that the id belongs to somebody.
        var response = await shawarma.DeleteAsync($"/api/restaurant/staff/{theirs.UserId}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ListAsync(friesLab)).ShouldContain(m => m.UserId == theirs.UserId);
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>
    /// Puts a seeded owner back, from the database.
    ///
    /// <para>
    /// Not through the API, because by the time this runs the account it is restoring has just
    /// demoted or removed itself and can no longer reach the endpoint that would do it. The
    /// restaurant is shared with the rest of the suite and every other test here starts by
    /// signing in as its owner.
    /// </para>
    /// </summary>
    private async Task RestoreOwnerAsync(string email)
    {
        await using var db = factory.CreateDbContext(TestTenant.PlatformAdmin());

        var userId = await db.Users.Where(u => u.Email == email).Select(u => u.Id).FirstAsync(Ct);
        var restaurantId = await db.Restaurants.Where(r => r.Slug == Slug).Select(r => r.Id).FirstAsync(Ct);

        var membership = await db.RestaurantStaff.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.UserId == userId && s.RestaurantId == restaurantId, Ct);

        if (membership is null)
        {
            db.RestaurantStaff.Add(new Domain.Restaurants.RestaurantStaff
            {
                UserId = userId,
                RestaurantId = restaurantId,
                StaffRole = StaffRoleType.Owner,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }
        else
        {
            membership.StaffRole = StaffRoleType.Owner;
        }

        await db.UserRoles.Where(r => r.UserId == userId && r.Role == RoleType.RestaurantStaff)
            .ExecuteDeleteAsync(Ct);

        if (!await db.UserRoles.AnyAsync(r => r.UserId == userId && r.Role == RoleType.RestaurantOwner, Ct))
        {
            db.UserRoles.Add(new Domain.Identity.UserRole { UserId = userId, Role = RoleType.RestaurantOwner });
        }

        await db.SaveChangesAsync(Ct);
    }

    /// <summary>Somewhere other than the restaurant doing the hiring.</summary>
    private const string MezzeSlug = "beirut-mezze-house";

    private static string NewEmail() => $"hire-{Guid.NewGuid():N}@example.test";

    /// <summary>
    /// A real pickup order, placed as the customer. Pickup so no delivery address is needed, and
    /// the priciest dish with a choice from every group that demands one, because several mezze
    /// items refuse to go in a basket without their bread picked.
    /// </summary>
    private async Task PlaceOrderAsync(string email, string slug)
    {
        var customer = await SignInAsync(email, "Passw0rd123");

        Guid restaurantId;
        Guid itemId;
        List<ChosenOptionRequest> choices;

        await using (var db = factory.CreateDbContext(TestTenant.PlatformAdmin()))
        {
            restaurantId = await db.Restaurants.Where(r => r.Slug == slug).Select(r => r.Id).FirstAsync(Ct);

            var item = await db.MenuItems
                .Where(i => i.Restaurant.Slug == slug)
                .OrderByDescending(i => i.BasePriceUsd)
                .Select(i => new
                {
                    i.Id,
                    Required = i.OptionGroups
                        .Where(g => (g.MinSelectOverride ?? g.OptionGroup.MinSelect) > 0)
                        .Select(g => g.OptionGroup.Options.OrderBy(o => o.SortOrder).First().Id)
                        .ToList(),
                })
                .FirstAsync(Ct);

            itemId = item.Id;
            choices = [.. item.Required.Select(id => new ChosenOptionRequest(id, 1))];
        }

        await EnsureSucceededAsync(await customer.PostAsJsonAsync(
            $"/api/restaurants/{restaurantId}/cart/lines",
            new AddCartLineRequest(itemId, 1, null, choices), Ct));

        var quote = await customer.GetFromJsonAsync<QuoteResponse>(
            $"/api/restaurants/{restaurantId}/cart/quote?fulfillment=Pickup", Ct);

        await EnsureSucceededAsync(await customer.PostAsJsonAsync(
            $"/api/restaurants/{restaurantId}/orders",
            new CheckoutRequest(FulfillmentType.Pickup, null, PaymentMethod.CashOnDelivery,
                null, quote!.TotalUsd, Guid.NewGuid()), Ct));
    }

    private static async Task<IReadOnlyList<StaffMemberResponse>> ListAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<StaffMemberResponse>>("/api/restaurant/staff", Ct))!;

    private static async Task<StaffMemberResponse> InviteAsync(
        HttpClient owner, string email, StaffRoleType role) =>
        (await InvitationAsync(owner, email, role)).Member;

    private static async Task<InvitedStaffResponse> InvitationAsync(
        HttpClient owner, string email, StaffRoleType role)
    {
        var response = await owner.PostAsJsonAsync("/api/restaurant/staff",
            new InviteStaffRequest(email, "Newly Hired", "+9613111222", role), Ct);

        await EnsureSucceededAsync(response);
        return (await response.Content.ReadFromJsonAsync<InvitedStaffResponse>(Ct))!;
    }

    private static async Task SetRoleAsync(HttpClient owner, Guid userId, StaffRoleType role) =>
        await EnsureSucceededAsync(await owner.PutAsJsonAsync(
            $"/api/restaurant/staff/{userId}/role", new SetStaffRoleRequest(role), Ct));

    private static async Task RemoveAsync(HttpClient owner, Guid userId) =>
        await EnsureSucceededAsync(await owner.DeleteAsync($"/api/restaurant/staff/{userId}", Ct));

    /// <summary>Follows the emailed link, chooses a password, and signs in with it.</summary>
    private async Task<HttpClient> AcceptInvitationAsync(string email) =>
        Authorized((await AcceptTokensAsync(email)).AccessToken);

    private async Task<AuthTokensResponse> AcceptTokensAsync(string email)
    {
        var body = factory.Emails.Sent.Last(m => m.To == email).Body;
        var match = Regex.Match(body, @"token=([A-Za-z0-9_\-%]+)", RegexOptions.None, TimeSpan.FromSeconds(1));
        match.Success.ShouldBeTrue("the invitation must carry a link");

        var reset = await factory.CreateClient().PostAsJsonAsync("/api/auth/reset-password",
            new ResetPasswordRequest(Uri.UnescapeDataString(match.Groups[1].Value), "Chosen-Passw0rd"), Ct);
        await EnsureSucceededAsync(reset);

        return await SignInTokensAsync(email, "Chosen-Passw0rd");
    }

    private HttpClient Authorized(string accessToken)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    private async Task<HttpClient> SignInAsync(string email, string? password = null) =>
        Authorized((await SignInTokensAsync(email, password ?? DatabaseSeeder.SeedPassword)).AccessToken);

    private async Task<AuthTokensResponse> SignInTokensAsync(string email, string password)
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new LoginRequest(email, password), Ct);

        await EnsureSucceededAsync(response);
        return (await response.Content.ReadFromJsonAsync<AuthTokensResponse>(Ct))!;
    }

    private async Task<Guid> RegisterAsync(string email)
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, "Passw0rd123", "Existing Customer", "+9613000111"), Ct);

        await EnsureSucceededAsync(response);
        return await UserIdAsync(email);
    }

    private async Task<Guid> UserIdAsync(string email)
    {
        await using var db = factory.CreateDbContext(TestTenant.PlatformAdmin());
        return await db.Users.Where(u => u.Email == email).Select(u => u.Id).FirstAsync(Ct);
    }

    private async Task<int> OrderCountAsync(string email)
    {
        await using var db = factory.CreateDbContext(TestTenant.PlatformAdmin());
        return await db.Orders.CountAsync(o => o.Customer.Email == email, Ct);
    }

    /// <summary>
    /// Removes everything a test created, straight from the database.
    ///
    /// <para>
    /// Not through the API on purpose. Half of these tests deliberately leave the API refusing to
    /// remove the row — that is what they are testing — and a cleanup that went through the same
    /// endpoint would quietly leave an extra owner on a staff list the whole suite shares.
    /// </para>
    /// </summary>
    private async Task ForgetAsync(string email)
    {
        await using var db = factory.CreateDbContext(TestTenant.PlatformAdmin());

        var userId = await db.Users.Where(u => u.Email == email).Select(u => u.Id).FirstOrDefaultAsync(Ct);
        if (userId == Guid.Empty)
        {
            return;
        }

        // The membership and the roles are the part that has to go: an extra owner left on a
        // shared restaurant would change what the last-owner tests are testing.
        await db.RestaurantStaff.IgnoreQueryFilters().Where(s => s.UserId == userId).ExecuteDeleteAsync(Ct);
        await db.UserRoles.Where(r => r.UserId == userId).ExecuteDeleteAsync(Ct);
        await db.RefreshTokens.Where(t => t.UserId == userId).ExecuteDeleteAsync(Ct);
        await db.PasswordResetTokens.Where(t => t.UserId == userId).ExecuteDeleteAsync(Ct);

        // The account itself only if nothing depends on it. A user who has ordered cannot be
        // deleted - the foreign key from Orders says so, deliberately, because an order has to
        // keep resolving whoever placed it. Leaving the row is harmless: the address is random,
        // it belongs to no restaurant any more, and nothing in the suite enumerates users.
        if (!await db.Orders.IgnoreQueryFilters().AnyAsync(o => o.CustomerId == userId, Ct))
        {
            await db.Carts.IgnoreQueryFilters().Where(c => c.UserId == userId).ExecuteDeleteAsync(Ct);
            await db.Users.Where(u => u.Id == userId).ExecuteDeleteAsync(Ct);
        }
    }

    private static async Task EnsureSucceededAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(Ct);
        throw new InvalidOperationException(
            $"{(int)response.StatusCode} {response.StatusCode} from "
            + $"{response.RequestMessage?.RequestUri}: {body}");
    }
}
