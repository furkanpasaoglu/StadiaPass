using System.Text.RegularExpressions;

namespace StadiaPass.AgentHost.Guardrails;

/// <summary>The kinds of personal datum this host knows how to recognise, and therefore how to remove.</summary>
internal enum PersonalDataKind
{
    EmailAddress,
    PhoneNumber,
    PaymentCard,
    NationalId
}

/// <summary>
/// A piece of text with the personal data taken out of it, and a note of what kinds were found.
/// </summary>
/// <param name="Text">The text as it may now be passed on.</param>
/// <param name="Removed">Which kinds were removed - never how many, and never the values themselves.</param>
internal readonly record struct Redaction(string Text, IReadOnlyList<PersonalDataKind> Removed)
{
    public bool RemovedSomething => Removed.Count > 0;
}

/// <summary>
/// Finds personal data in free text and takes it out, leaving a placeholder that says what kind it was.
/// </summary>
/// <remarks>
/// <para>
/// The risk this answers is the ordinary one, not an exotic one. No tool here returns a customer - the
/// catalogue tools are the public site in another shape and the analytics tool reports totals - so the way
/// a customer's details reach a language model is that a member of staff types them into the question.
/// From there they are in the prompt, in the conversation history, in the traces and in whatever the model
/// provider keeps. Redacting at this seam is the difference between a policy nobody can follow and a
/// control that holds whether or not anyone remembers it.
/// </para>
/// <para>
/// Every detector validates where the datum allows it: a card number has to pass Luhn, a national
/// identifier has to pass its own checksum. This is deliberate and it is the harder half of the job. An
/// over-eager filter is not a safer filter - the first time it turns a price or a capacity into a
/// placeholder, the analyst starts reporting nonsense and somebody switches the guardrail off. The tests
/// that assert nothing changed are worth more than the ones that assert something did.
/// </para>
/// <para>
/// The patterns spell out <c>[0-9]</c> rather than <c>\d</c> on purpose: in .NET <c>\d</c> matches every
/// Unicode decimal digit, so Arabic-Indic or Devanagari digits would satisfy a "sixteen digits" test and
/// then fail Luhn for reasons that have nothing to do with the card.
/// </para>
/// </remarks>
internal static partial class PersonalData
{
    private const string EmailPlaceholder = "[redacted email address]";
    private const string PhonePlaceholder = "[redacted phone number]";
    private const string CardPlaceholder = "[redacted card number]";
    private const string NationalIdPlaceholder = "[redacted national id]";

    public static Redaction Redact(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new Redaction(text ?? string.Empty, []);
        }

        var removed = new List<PersonalDataKind>();

        // Order matters, and it is the order of how specific the evidence is. An address is the only thing
        // here that can hold an @, so it goes first and takes any digits inside it out of the running. A
        // card is thirteen digits or more, a national identifier is exactly eleven, and a mobile number is
        // ten after its prefix - so each pass can only see runs the earlier passes did not claim, and the
        // eleven digits of "00000000000" are offered to the identifier's checksum before they are offered
        // to the phone pattern, which is the only order in which both can be right.
        var redacted = EmailAddress().Replace(text, _ => Mark(removed, PersonalDataKind.EmailAddress, EmailPlaceholder));

        redacted = PaymentCard().Replace(
            redacted,
            match => PassesLuhn(DigitsOf(match.ValueSpan))
                ? Mark(removed, PersonalDataKind.PaymentCard, CardPlaceholder)
                : match.Value);

        redacted = NationalId().Replace(
            redacted,
            match => PassesNationalIdChecksum(match.ValueSpan)
                ? Mark(removed, PersonalDataKind.NationalId, NationalIdPlaceholder)
                : match.Value);

        redacted = MobileNumber().Replace(redacted, _ => Mark(removed, PersonalDataKind.PhoneNumber, PhonePlaceholder));

        return new Redaction(redacted, removed);
    }

    private static string Mark(List<PersonalDataKind> removed, PersonalDataKind kind, string placeholder)
    {
        // Recorded once however often it occurs: this list ends up in a log line and on a metric tag, and
        // what an operator needs to know is that an address went through, not that it went through twice.
        if (!removed.Contains(kind))
        {
            removed.Add(kind);
        }

        return placeholder;
    }

    [GeneratedRegex(
        "[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\\.[A-Za-z]{2,}",
        RegexOptions.CultureInvariant)]
    private static partial Regex EmailAddress();

    /// <summary>
    /// The three ways a card is ever written down: in fours, in the American Express four-six-five, or as
    /// one unbroken run. Deliberately not "any thirteen or more digits with anything between them" - that
    /// shape matches a list of seat numbers, and a long enough list of seat numbers will eventually pass
    /// Luhn by luck.
    /// </summary>
    [GeneratedRegex(
        "(?<![0-9-])(?:[0-9]{4}[ -][0-9]{4}[ -][0-9]{4}[ -][0-9]{4}"
        + "|[0-9]{4}[ -][0-9]{6}[ -][0-9]{5}"
        + "|[0-9]{13,19})(?![0-9-])",
        RegexOptions.CultureInvariant)]
    private static partial Regex PaymentCard();

    [GeneratedRegex("(?<![0-9])[0-9]{11}(?![0-9])", RegexOptions.CultureInvariant)]
    private static partial Regex NationalId();

    /// <summary>
    /// A Turkish mobile number, with or without its country or trunk prefix and with the separators people
    /// actually use. The lookbehind is what keeps it from finding a phone number in the middle of a longer
    /// number: ten digits starting with a five appear inside plenty of things that are not telephones.
    /// </summary>
    [GeneratedRegex(
        "(?<![0-9A-Za-z_+])(?:\\+90[ -]?|0)?5[0-9]{2}[ -]?[0-9]{3}[ -]?[0-9]{2}[ -]?[0-9]{2}(?![0-9])",
        RegexOptions.CultureInvariant)]
    private static partial Regex MobileNumber();

    private static string DigitsOf(ReadOnlySpan<char> value)
    {
        Span<char> digits = stackalloc char[value.Length];
        var length = 0;

        foreach (var character in value)
        {
            if (character is >= '0' and <= '9')
            {
                digits[length++] = character;
            }
        }

        return new string(digits[..length]);
    }

    private static bool PassesLuhn(ReadOnlySpan<char> digits)
    {
        if (digits.Length is < 13 or > 19)
        {
            return false;
        }

        var sum = 0;
        var doubling = false;

        for (var index = digits.Length - 1; index >= 0; index--)
        {
            var value = digits[index] - '0';

            if (doubling)
            {
                value *= 2;

                if (value > 9)
                {
                    value -= 9;
                }
            }

            sum += value;
            doubling = !doubling;
        }

        return sum % 10 == 0;
    }

    /// <summary>
    /// The Turkish national identifier's own check: the tenth digit is fixed by the nine before it and the
    /// eleventh by the ten before it, and the first can never be zero. Three independent constraints, which
    /// is why an ordinary eleven-digit reference number almost never passes.
    /// </summary>
    private static bool PassesNationalIdChecksum(ReadOnlySpan<char> digits)
    {
        if (digits.Length != 11 || digits[0] == '0')
        {
            return false;
        }

        var odd = 0;
        var even = 0;

        for (var index = 0; index < 9; index++)
        {
            var value = digits[index] - '0';

            if (index % 2 == 0)
            {
                odd += value;
            }
            else
            {
                even += value;
            }
        }

        var tenth = (((odd * 7) - even) % 10 + 10) % 10;

        if (tenth != digits[9] - '0')
        {
            return false;
        }

        return (odd + even + tenth) % 10 == digits[10] - '0';
    }
}
