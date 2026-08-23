namespace OrderingSystem.Infrastructure.Email;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public string Host { get; set; } = "localhost";

    /// <summary>1025 is Mailpit's SMTP port in the development compose file.</summary>
    public int Port { get; set; } = 1025;

    public string FromAddress { get; set; } = "no-reply@ordering.test";
    public string FromName { get; set; } = "Ordering System";

    public string? UserName { get; set; }
    public string? Password { get; set; }

    /// <summary>False for Mailpit, which speaks plain SMTP. True everywhere real.</summary>
    public bool UseStartTls { get; set; }
}
