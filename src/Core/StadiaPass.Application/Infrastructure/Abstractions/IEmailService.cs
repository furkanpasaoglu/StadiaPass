namespace StadiaPass.Application.Infrastructure.Abstractions;

/// <summary>
/// The way a message reaches a person. What carries it - SMTP, a transactional mail API, a file on disk in a
/// test - is a configuration decision made in Infrastructure; nothing above this line knows.
/// </summary>
public interface IEmailService
{
    /// <summary>
    /// Sends, or reports that it could not. Deliberately does not throw: the only caller today is a message
    /// consumer, and a mail server having a bad afternoon must not be able to turn a completed sale into a
    /// poisoned message.
    /// </summary>
    /// <param name="recipient">
    /// Named for the analyzer, which refuses <c>to</c> on an interface member because it is a keyword in
    /// another .NET language.
    /// </param>
    /// <returns><see langword="true"/> when the server accepted the message for delivery.</returns>
    Task<bool> SendEmailAsync(
        string recipient,
        string subject,
        string body,
        CancellationToken cancellationToken = default);
}
