using System.Globalization;
using System.Net;
using System.Text;
using StadiaPass.Application.Tickets.Events;

namespace StadiaPass.Infrastructure.Email;

/// <summary>
/// What a ticket holder gets when their match is called off.
/// </summary>
/// <remarks>
/// Same shape and the same reasons as the confirmation beside it: inline-styled tables, because mail clients
/// are not browsers, and every value from the message HTML-encoded, because a reason somebody typed into a
/// back-office form should not be able to break the layout - let alone anything worse.
/// <para>
/// It says the amount and the card it goes back to rather than only that a refund is coming. A mail that
/// leaves somebody wondering how much and to where is a mail that generates a support message.
/// </para>
/// </remarks>
internal static class MatchCancellationEmail
{
    private const string Ink = "#212529";
    private const string Muted = "#6c757d";
    private const string Alert = "#b02a37";
    private const string Line = "#dee2e6";

    public static string SubjectFor(MatchCancellationNotice notice) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"Cancelled: {notice.HomeTeam} vs {notice.AwayTeam} - your seat {notice.SeatNumber} is refunded");

    public static string BodyFor(MatchCancellationNotice notice)
    {
        var kickOff = notice.KickOffUtc.ToLocalTime()
            .ToString("dd MMMM yyyy, HH:mm", CultureInfo.InvariantCulture);

        var amount = notice.Amount.ToString("N2", CultureInfo.InvariantCulture);

        var builder = new StringBuilder(2048);

        builder.Append(CultureInfo.InvariantCulture, $"""
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0"
                   style="background:#f8f9fa;padding:24px 0;font-family:Segoe UI,Helvetica,Arial,sans-serif;">
              <tr><td align="center">
                <table role="presentation" width="560" cellpadding="0" cellspacing="0"
                       style="width:560px;max-width:100%;background:#ffffff;border:1px solid {Line};border-radius:8px;">
                  <tr>
                    <td style="padding:24px 28px 8px 28px;">
                      <p style="margin:0 0 4px 0;font-size:12px;letter-spacing:.08em;text-transform:uppercase;color:{Alert};">
                        Match cancelled
                      </p>
                      <h1 style="margin:0;font-size:22px;line-height:1.3;color:{Ink};">
                        {Encode(notice.HomeTeam)} v {Encode(notice.AwayTeam)}
                      </h1>
                      <p style="margin:6px 0 0 0;font-size:14px;color:{Muted};">
                        {Encode(kickOff)} &middot; {Encode(notice.VenueName)}
                      </p>
                    </td>
                  </tr>
                  <tr>
                    <td style="padding:16px 28px 0 28px;font-size:15px;line-height:1.6;color:{Ink};">
                      <p style="margin:0 0 12px 0;">
                        This match will not be played, so your ticket is no longer valid. The reason given was:
                        <strong>{Encode(notice.Reason)}</strong>
                      </p>
                      <p style="margin:0 0 12px 0;">
                        <strong>{Encode(amount)} {Encode(notice.Currency)}</strong> for seat
                        <strong>{Encode(notice.SeatNumber)}</strong> is being refunded to the card that paid
                        for it. You do not need to do anything. Refunds can take a few days to appear on a
                        statement, depending on your bank.
                      </p>
                      <p style="margin:0 0 20px 0;font-size:14px;color:{Muted};">
                        If you bought more than one seat for this match, each one is refunded separately and
                        gets its own message.
                      </p>
                    </td>
                  </tr>
                  <tr>
                    <td style="padding:0 28px 24px 28px;font-size:12px;color:{Muted};border-top:1px solid {Line};padding-top:16px;">
                      Sent by StadiaPass. Please do not reply to this message.
                    </td>
                  </tr>
                </table>
              </td></tr>
            </table>
            """);

        return builder.ToString();
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
