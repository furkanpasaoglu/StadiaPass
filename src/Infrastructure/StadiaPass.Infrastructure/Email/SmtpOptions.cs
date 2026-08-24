using System.ComponentModel.DataAnnotations;

namespace StadiaPass.Infrastructure.Email;

/// <summary>
/// Where mail goes out through. Gmail is what this is configured against locally, but nothing here is
/// Gmail-specific: any SMTP server that speaks STARTTLS on 587 takes the same six values.
/// </summary>
public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    [Required]
    public string Host { get; init; } = "smtp.gmail.com";

    /// <summary>
    /// 587 is the submission port, which starts in the clear and is upgraded with STARTTLS. 465 is the
    /// older implicit-TLS port and works too; 25 is server-to-server and Gmail will not take it.
    /// </summary>
    [Range(1, 65535)]
    public int Port { get; init; } = 587;

    [Required]
    public string SenderName { get; init; } = "StadiaPass";

    [Required]
    [EmailAddress]
    public string SenderEmail { get; init; } = string.Empty;

    /// <summary>The Google account itself. Comes from Vault; see the README.</summary>
    [Required(ErrorMessage = "Smtp:UserName is not set. It is expected to come from Vault.")]
    public string UserName { get; init; } = string.Empty;

    /// <summary>
    /// A Google App Password - sixteen characters, generated per application, revocable on its own. Never
    /// the account password: that one carries the whole account and cannot be revoked without changing it.
    /// </summary>
    [Required(ErrorMessage = "Smtp:Password is not set. It is expected to come from Vault.")]
    public string Password { get; init; } = string.Empty;

    /// <summary>
    /// How long to wait on the server before giving up. A consumer is not a request, but it is also not
    /// somewhere to hang forever.
    /// </summary>
    [Range(1, 120)]
    public int TimeoutSeconds { get; init; } = 20;

    /// <summary>
    /// Whether mail is configured at all. A clone with no Vault entry gets a checkout that works and a
    /// consumer that says out loud it had nowhere to send to, rather than a startup that refuses to run.
    /// </summary>
    public bool IsConfigured =>
        UserName is { Length: > 0 } && Password is { Length: > 0 } && SenderEmail is { Length: > 0 };
}
