using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using OrderingSystem.Application.Features.Auth;

namespace OrderingSystem.Api.IntegrationTests.Auth;

/// <summary>
/// A person's own account: what they may correct about themselves, and what proving it costs.
/// </summary>
public sealed class ProfileTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Reading_your_own_account_needs_an_account()
    {
        (await factory.CreateClient().GetAsync("/api/me", Ct)).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Says_who_you_are_from_the_database_not_from_your_token()
    {
        var email = NewEmail();
        var client = await RegisterAsync(email);

        await Update(client, "Corrected Name", "+9613111000");

        // The same token as before the change. Reading the profile from it would show the name
        // they have just corrected, which looks like the save silently failed.
        var me = await client.GetFromJsonAsync<ProfileResponse>("/api/me", Ct);

        me!.FullName.ShouldBe("Corrected Name");
        me.Email.ShouldBe(email);
        me.Roles.ShouldContain("Customer");
    }

    [Fact]
    public async Task A_courier_needs_a_number_so_an_empty_one_is_refused()
    {
        var client = await RegisterAsync(NewEmail());

        var response = await client.PutAsJsonAsync("/api/me",
            new UpdateProfileRequest("Still Named", ""), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_email_address_cannot_be_changed_here()
    {
        var email = NewEmail();
        var client = await RegisterAsync(email);

        // Sent anyway, the way a hand-rolled client would. It is the login identifier and the
        // only way back into a locked-out account, so it is not on the request at all — and an
        // extra field has to be ignored rather than quietly honoured.
        var response = await client.PutAsJsonAsync("/api/me",
            new { fullName = "New Name", phone = "+9613111000", email = "somebody@else.test" }, Ct);

        response.EnsureSuccessStatusCode();
        (await client.GetFromJsonAsync<ProfileResponse>("/api/me", Ct))!.Email.ShouldBe(email);
    }

    // ------------------------------------------------------------------ passwords

    [Fact]
    public async Task Changing_a_password_needs_the_current_one()
    {
        var client = await RegisterAsync(NewEmail());

        var response = await client.PostAsJsonAsync("/api/me/password",
            new ChangePasswordRequest("NotTheOne123", "Brand-New-Passw0rd"), Ct);

        // A borrowed phone is a session. Letting it set a new password without proving the old
        // one turns that into the account itself, permanently and without the owner noticing.
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_changed_password_is_the_one_that_works_afterwards()
    {
        var email = NewEmail();
        var client = await RegisterAsync(email);

        (await client.PostAsJsonAsync("/api/me/password",
            new ChangePasswordRequest(Password, "Brand-New-Passw0rd"), Ct))
            .EnsureSuccessStatusCode();

        (await SignInAsync(email, Password)).ShouldBe(HttpStatusCode.Unauthorized);
        (await SignInAsync(email, "Brand-New-Passw0rd")).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Changing_a_password_signs_the_other_sessions_out()
    {
        var email = NewEmail();

        var onTheLostPhone = await TokensAsync(email, register: true);
        var here = Authorized((await TokensFromLoginAsync(email, Password)).AccessToken);

        (await here.PostAsJsonAsync("/api/me/password",
            new ChangePasswordRequest(Password, "Brand-New-Passw0rd"), Ct))
            .EnsureSuccessStatusCode();

        // The whole point of changing it. Somebody doing this because a phone went missing has
        // changed nothing if the phone stays signed in.
        var refreshed = await factory.CreateClient().PostAsJsonAsync("/api/auth/refresh",
            new RefreshRequest(onTheLostPhone.RefreshToken), Ct);

        refreshed.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_weak_new_password_is_refused()
    {
        var client = await RegisterAsync(NewEmail());

        var response = await client.PostAsJsonAsync("/api/me/password",
            new ChangePasswordRequest(Password, "short"), Ct);

        // The same rule registration applies. A password nobody could have chosen at sign-up
        // should not be reachable by changing to it afterwards.
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ------------------------------------------------------------------ helpers

    private const string Password = "Passw0rd123";

    private static string NewEmail() => $"me-{Guid.NewGuid():N}@example.test";

    private async Task<HttpClient> RegisterAsync(string email) =>
        Authorized((await TokensAsync(email, register: true)).AccessToken);

    private async Task<AuthTokensResponse> TokensAsync(string email, bool register)
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, Password, "Test Person", "+9613000000"), Ct);

        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthTokensResponse>(Ct))!;
    }

    private async Task<AuthTokensResponse> TokensFromLoginAsync(string email, string password)
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new LoginRequest(email, password), Ct);

        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthTokensResponse>(Ct))!;
    }

    private async Task<HttpStatusCode> SignInAsync(string email, string password) =>
        (await factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new LoginRequest(email, password), Ct)).StatusCode;

    private HttpClient Authorized(string accessToken)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    private static async Task Update(HttpClient client, string name, string phone) =>
        (await client.PutAsJsonAsync("/api/me", new UpdateProfileRequest(name, phone), Ct))
            .EnsureSuccessStatusCode();
}
