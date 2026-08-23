using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using OrderingSystem.Application.Abstractions;

namespace OrderingSystem.Infrastructure.Email;

/// <summary>
/// Sends over SMTP via MailKit. In development that is Mailpit in Docker, which accepts everything
/// and delivers nothing — so a password-reset flow can be exercised end to end without a real
/// address ever receiving mail.
/// </summary>
public sealed class SmtpEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    private readonly EmailOptions _options = options.Value;

    public async Task SendAsync(
        string toEmail, string subject, string body, CancellationToken cancellationToken = default)
    {
        var message = new MimeMessage
        {
            Subject = subject,
            Body = new TextPart("plain") { Text = body },
        };
        message.From.Add(new MailboxAddress(_options.FromName, _options.FromAddress));
        message.To.Add(MailboxAddress.Parse(toEmail));

        using var client = new SmtpClient();
        await client.ConnectAsync(
            _options.Host,
            _options.Port,
            _options.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None,
            cancellationToken);

        // Mailpit needs no credentials; a real relay does. Both parts must be present before
        // attempting to authenticate, or MailKit is handed a null password.
        if (!string.IsNullOrEmpty(_options.UserName) && !string.IsNullOrEmpty(_options.Password))
        {
            await client.AuthenticateAsync(_options.UserName, _options.Password, cancellationToken);
        }

        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
    }
}
