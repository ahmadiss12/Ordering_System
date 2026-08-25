using FluentValidation;
using OrderingSystem.Application.Features.Auth;

namespace OrderingSystem.Application.Tests.Auth;

/// <summary>
/// The password and email rules, tested directly. They are reachable through HTTP too, but a rule
/// deserves a test that runs in milliseconds with no database and no web host — that is the whole
/// reason validators live in the Application layer rather than in a controller.
/// </summary>
public class AuthValidatorTests
{
    private readonly RegisterRequestValidator _register = new();
    private readonly LoginRequestValidator _login = new();
    private readonly ResetPasswordRequestValidator _reset = new();

    [Theory]
    [InlineData("short", "under the ten character minimum")]
    [InlineData("nodigitshere", "no digit")]
    [InlineData("1234567890", "no letter")]
    [InlineData("", "empty")]
    public void A_password_that_breaks_a_rule_is_rejected(string password, string why)
    {
        var result = _register.Validate(Registration(password));

        result.IsValid.ShouldBeFalse($"'{password}' should be rejected: {why}");
        result.Errors.ShouldContain(e => e.PropertyName == nameof(RegisterRequest.Password));
    }

    [Theory]
    [InlineData("Passw0rd123")]
    [InlineData("correct horse battery staple 7")]
    public void A_password_meeting_every_rule_is_accepted(string password) =>
        _register.Validate(Registration(password)).IsValid.ShouldBeTrue();

    [Fact]
    public void A_password_longer_than_the_maximum_is_rejected()
    {
        // The upper bound is not arbitrary: hashing cost scales with input length, so an
        // unbounded password is a cheap way to load the server's CPU.
        var result = _register.Validate(Registration(new string('a', 129) + "1"));

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Login_does_not_apply_complexity_rules()
    {
        // Deliberate. Applying today's rules to a login attempt would lock out anyone whose
        // password was set before those rules existed - they could not even sign in to change it.
        var result = _login.Validate(new LoginRequest("someone@example.test", "old"));

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Login_still_requires_both_fields()
    {
        _login.Validate(new LoginRequest("", "somepassword")).IsValid.ShouldBeFalse();
        _login.Validate(new LoginRequest("someone@example.test", "")).IsValid.ShouldBeFalse();
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("")]
    [InlineData("  ")]
    public void Registration_rejects_an_obviously_malformed_email(string email)
    {
        var result = _register.Validate(
            new RegisterRequest(email, "Passw0rd123", "Test User", "+9613000000"));

        result.IsValid.ShouldBeFalse();
    }

    [Theory]
    [InlineData("user@tld")]
    [InlineData("first.last+tag@sub.domain.example")]
    public void Registration_accepts_unusual_but_legal_addresses(string email)
    {
        // The check is deliberately permissive. Single-label domains are legal, and every
        // strict email regex ever written rejects addresses that real people actually have.
        // The only reliable proof an address works is sending to it, which the reset flow does.
        var result = _register.Validate(
            new RegisterRequest(email, "Passw0rd123", "Test User", "+9613000000"));

        result.IsValid.ShouldBeTrue($"'{email}' is a legal address");
    }

    [Fact]
    public void A_reset_needs_both_a_token_and_a_conforming_password()
    {
        _reset.Validate(new ResetPasswordRequest("", "Passw0rd123")).IsValid.ShouldBeFalse();
        _reset.Validate(new ResetPasswordRequest("a-token", "short")).IsValid.ShouldBeFalse();
        _reset.Validate(new ResetPasswordRequest("a-token", "Passw0rd123")).IsValid.ShouldBeTrue();
    }

    private static RegisterRequest Registration(string password) =>
        new("someone@example.test", password, "Test User", "+9613000000");
}
