using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using StadiaPass.Application.Infrastructure.Abstractions;

namespace StadiaPass.Infrastructure.Email;

/// <summary>
/// Sends over SMTP with MailKit, which is what the .NET documentation itself points at now that
/// <c>SmtpClient</c> is obsolete.
/// </summary>
/// <remarks>
/// Nothing in here throws. The caller is a message consumer, and an exception there would put a completed,
/// paid-for sale into an error queue over a mail server having a bad afternoon. The failure is reported as
/// <see langword="false"/> and written to the log with enough to find the customer again.
/// </remarks>
internal sealed partial class MailKitEmailService(
    IOptions<SmtpOptions> options,
    ILogger<MailKitEmailService> logger) : IEmailService
{
    private readonly SmtpOptions _options = options.Value;

    public async Task<bool> SendEmailAsync(
        string recipient,
        string subject,
        string body,
        CancellationToken cancellationToken = default)
    {
        if (!_options.IsConfigured)
        {
            NotConfigured(logger, recipient);

            return false;
        }

        try
        {
            using var message = BuildMessage(recipient, subject, body);
            using var client = new SmtpClient { Timeout = _options.TimeoutSeconds * 1000 };

            // StartTlsWhenAvailable rather than StartTls: on 587 Gmail always offers it, and on 465 the
            // connection is already encrypted, so one setting covers both without pretending to be secure
            // when it is not - MailKit still refuses to hand over the password over a plaintext link.
            await client.ConnectAsync(
                _options.Host, _options.Port, SecureSocketOptions.StartTlsWhenAvailable, cancellationToken);

            // A Google App Password, not the account password. Google refuses plain account passwords over
            // SMTP entirely, which is the right refusal.
            await client.AuthenticateAsync(_options.UserName, _options.Password, cancellationToken);

            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(quit: true, cancellationToken);

            EmailSent(logger, recipient, subject);

            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            SendFailed(logger, recipient, subject, exception);

            return false;
        }
    }

    private MimeMessage BuildMessage(string recipient, string subject, string body)
    {
        var message = new MimeMessage
        {
            Subject = subject,
            Body = new BodyBuilder { HtmlBody = body }.ToMessageBody()
        };

        message.From.Add(new MailboxAddress(_options.SenderName, _options.SenderEmail));
        message.To.Add(MailboxAddress.Parse(recipient));

        return message;
    }

    [LoggerMessage(
        EventId = 6400,
        Level = LogLevel.Information,
        Message = "Sent \"{Subject}\" to {Recipient}")]
    private static partial void EmailSent(ILogger logger, string recipient, string subject);

    [LoggerMessage(
        EventId = 6401,
        Level = LogLevel.Error,
        Message = "Could not send \"{Subject}\" to {Recipient}; nothing was delivered")]
    private static partial void SendFailed(ILogger logger, string recipient, string subject, Exception exception);

    [LoggerMessage(
        EventId = 6402,
        Level = LogLevel.Warning,
        Message = "No SMTP credentials are configured, so nothing was sent to {Recipient}")]
    private static partial void NotConfigured(ILogger logger, string recipient);
}
