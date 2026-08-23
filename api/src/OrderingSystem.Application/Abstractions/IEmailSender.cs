namespace OrderingSystem.Application.Abstractions;

/// <summary>
/// Transactional email only — password resets and, later, order receipts. There is no marketing
/// path through this interface and there should not be one.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(string toEmail, string subject, string body, CancellationToken cancellationToken = default);
}
