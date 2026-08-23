using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using OrderingSystem.Application.Features.Auth;

namespace OrderingSystem.Api.IntegrationTests.Auth;

/// <summary>
/// The security properties of the auth flow, each asserted through the real HTTP pipeline.
/// These are behaviours that would otherwise regress silently — nothing crashes when an endpoint
/// quietly starts revealing which addresses are registered.
/// </summary>
public sealed class AuthFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Client => factory.CreateClient();

    [Fact]
    public async Task Register_then_login_issues_a_working_token_pair()
    {
        var email = NewEmail();
        var registered = await RegisterAsync(email);

        registered.AccessToken.ShouldNotBeNullOrWhiteSpace();
        registered.RefreshToken.ShouldNotBeNullOrWhiteSpace();
        registered.AccessTokenExpiresAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow);

        var response = await Client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(email, "Passw0rd123"), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Email_is_normalised_so_casing_cannot_create_a_second_account()
    {
        var email = NewEmail();
        await RegisterAsync(email);

        var duplicate = await Client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email.ToUpperInvariant() + "  ", "Passw0rd123", "Someone Else", "+9613000001"), Ct);

        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_wrong_password_and_an_unknown_account_are_indistinguishable()
    {
        var email = NewEmail();
        await RegisterAsync(email);

        var wrongPassword = await Client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(email, "DefinitelyWrong123"), Ct);
        var noSuchUser = await Client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(NewEmail(), "DefinitelyWrong123"), Ct);

        wrongPassword.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        noSuchUser.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // The bodies must match too - a different message is as good a membership oracle as a
        // different status. traceId is excluded because ASP.NET stamps a fresh one per request;
        // every other field is compared, so a real difference still fails this.
        var wrongBody = await ReadProblemAsync(wrongPassword);
        var unknownBody = await ReadProblemAsync(noSuchUser);
        wrongBody.ShouldBe(unknownBody);
    }

    [Fact]
    public async Task Refreshing_rotates_the_token()
    {
        var tokens = await RegisterAsync(NewEmail());
        var refreshed = await RefreshAsync(tokens.RefreshToken);

        refreshed.RefreshToken.ShouldNotBe(tokens.RefreshToken);
    }

    [Fact]
    public async Task Replaying_a_spent_refresh_token_revokes_the_whole_family()
    {
        var tokens = await RegisterAsync(NewEmail());
        var second = await RefreshAsync(tokens.RefreshToken);

        // The thief presents the token the real client already rotated away from.
        var replay = await Client.PostAsJsonAsync("/api/auth/refresh",
            new RefreshRequest(tokens.RefreshToken), Ct);
        replay.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // The legitimate client's newer token dies too. That is the point: we cannot tell which
        // party is the attacker, so the whole login is ended and both must sign in again.
        var legitimate = await Client.PostAsJsonAsync("/api/auth/refresh",
            new RefreshRequest(second.RefreshToken), Ct);
        legitimate.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Forgotten_password_answers_the_same_for_known_and_unknown_addresses()
    {
        var email = NewEmail();
        await RegisterAsync(email);

        var known = await Client.PostAsJsonAsync("/api/auth/forgot-password",
            new ForgotPasswordRequest(email), Ct);
        var unknown = await Client.PostAsJsonAsync("/api/auth/forgot-password",
            new ForgotPasswordRequest(NewEmail()), Ct);

        known.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        unknown.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        // Only the real address produced mail - the identical response is the whole defence.
        factory.Emails.Sent.Count(m => m.To == email).ShouldBe(1);
    }

    [Fact]
    public async Task A_reset_link_works_once_and_ends_every_existing_session()
    {
        var email = NewEmail();
        var tokens = await RegisterAsync(email);

        await Client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest(email), Ct);
        var resetToken = ExtractResetToken(email);

        var first = await Client.PostAsJsonAsync("/api/auth/reset-password",
            new ResetPasswordRequest(resetToken, "BrandNewPass123"), Ct);
        first.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var replay = await Client.PostAsJsonAsync("/api/auth/reset-password",
            new ResetPasswordRequest(resetToken, "YetAnother123"), Ct);
        replay.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, "a reset link is single use");

        var oldPassword = await Client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(email, "Passw0rd123"), Ct);
        oldPassword.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var newPassword = await Client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(email, "BrandNewPass123"), Ct);
        newPassword.StatusCode.ShouldBe(HttpStatusCode.OK);

        // If the reset happened because the account was compromised, leaving the attacker's
        // refresh token alive would defeat the point.
        var staleSession = await Client.PostAsJsonAsync("/api/auth/refresh",
            new RefreshRequest(tokens.RefreshToken), Ct);
        staleSession.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_password_below_the_minimum_is_refused_with_field_level_detail()
    {
        var response = await Client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(NewEmail(), "short", "Too Short", "+9613000000"), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("Password");
    }

    // ------------------------------------------------------------------ helpers

    private static string NewEmail() => $"user-{Guid.NewGuid():N}@example.test";

    /// <summary>The ProblemDetails body with the per-request traceId removed.</summary>
    private static async Task<string> ReadProblemAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));

        var fields = document.RootElement.EnumerateObject()
            .Where(p => !p.NameEquals("traceId"))
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => $"{p.Name}={p.Value.GetRawText()}");

        return string.Join("&", fields);
    }

    private async Task<AuthTokensResponse> RegisterAsync(string email)
    {
        var response = await Client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, "Passw0rd123", "Test User", "+9613000000"), Ct);

        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthTokensResponse>(Ct))!;
    }

    private async Task<AuthTokensResponse> RefreshAsync(string refreshToken)
    {
        var response = await Client.PostAsJsonAsync("/api/auth/refresh",
            new RefreshRequest(refreshToken), Ct);

        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthTokensResponse>(Ct))!;
    }

    private string ExtractResetToken(string email)
    {
        var body = factory.Emails.Sent.Last(m => m.To == email).Body;
        var match = Regex.Match(body, @"token=([A-Za-z0-9_\-%]+)", RegexOptions.None, TimeSpan.FromSeconds(1));
        match.Success.ShouldBeTrue("the reset email must carry a token");
        return Uri.UnescapeDataString(match.Groups[1].Value);
    }
}
