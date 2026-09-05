namespace OrderingSystem.Application.Features.Auth;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>How long a refresh token stays valid if never used.</summary>
    public int RefreshTokenDays { get; set; } = 30;

    /// <summary>Password reset links are deliberately short-lived.</summary>
    public int PasswordResetHours { get; set; } = 2;

    /// <summary>
    /// How long a staff invitation stays open. Days rather than the reset link's hours: somebody
    /// recovering their own account is waiting at the screen for the mail, somebody being hired
    /// may not read it until their next shift.
    /// </summary>
    public int InvitationDays { get; set; } = 7;

    /// <summary>
    /// The storefront: where a customer who has forgotten their password is sent.
    ///
    /// <para>
    /// Two origins rather than one, because the two links go to different people. A customer
    /// resetting a password belongs on the storefront; somebody invited to run a restaurant
    /// belongs on the dashboard, and sending them to the customer app would leave them signed in
    /// somewhere with none of the screens they were invited for.
    /// </para>
    /// </summary>
    public string AppBaseUrl { get; set; } = "http://localhost:4201";

    /// <summary>The restaurant dashboard: where a staff invitation is sent.</summary>
    public string DashboardBaseUrl { get; set; } = "http://localhost:4200";
}
