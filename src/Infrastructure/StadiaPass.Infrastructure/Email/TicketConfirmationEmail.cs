using System.Globalization;
using System.Net;
using System.Text;
using StadiaPass.Application.Tickets.Events;

namespace StadiaPass.Infrastructure.Email;

/// <summary>
/// The confirmation a customer gets after buying a seat.
/// </summary>
/// <remarks>
/// Written as inline-styled tables on purpose. Mail clients are not browsers: Outlook renders with Word,
/// Gmail strips anything in a &lt;style&gt; block, and flexbox is not reliably supported anywhere. The rules
/// that produce a mail which survives all of them are the ones the web left behind twenty years ago.
/// Every value from the message is HTML-encoded - a team name with an ampersand in it should not be able to
/// break the layout, let alone anything worse.
/// </remarks>
internal static class TicketConfirmationEmail
{
    private const string Ink = "#212529";
    private const string Muted = "#6c757d";
    private const string Accent = "#146c43";
    private const string Line = "#dee2e6";

    public static string SubjectFor(TicketPurchasedEvent purchase) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"Your ticket for {purchase.HomeTeam} vs {purchase.AwayTeam} - seat {purchase.SeatNumber}");

    public static string BodyFor(TicketPurchasedEvent purchase)
    {
        var kickOff = purchase.KickOffUtc.ToLocalTime()
            .ToString("dd MMMM yyyy, HH:mm", CultureInfo.InvariantCulture);

        var price = purchase.Price.ToString("N2", CultureInfo.InvariantCulture);

        var builder = new StringBuilder(2048);

        builder.Append(CultureInfo.InvariantCulture, $"""
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0"
                   style="background:#f8f9fa;padding:24px 0;font-family:Segoe UI,Helvetica,Arial,sans-serif;">
              <tr><td align="center">
                <table role="presentation" width="560" cellpadding="0" cellspacing="0"
                       style="width:560px;max-width:100%;background:#ffffff;border:1px solid {Line};border-radius:8px;">
                  <tr>
                    <td style="background:{Accent};color:#ffffff;padding:20px 28px;border-radius:8px 8px 0 0;">
                      <div style="font-size:13px;letter-spacing:.18em;text-transform:uppercase;opacity:.85;">StadiaPass</div>
                      <div style="font-size:22px;font-weight:600;padding-top:4px;">Your ticket is confirmed</div>
                    </td>
                  </tr>
                  <tr>
                    <td style="padding:24px 28px 8px;color:{Ink};font-size:20px;font-weight:600;">
                      {Encode(purchase.HomeTeam)} <span style="color:{Muted};font-weight:400;">vs</span> {Encode(purchase.AwayTeam)}
                    </td>
                  </tr>
                  <tr>
                    <td style="padding:0 28px 20px;color:{Muted};font-size:14px;">
                      {Encode(purchase.VenueName)} &middot; {Encode(kickOff)}
                    </td>
                  </tr>
                  <tr>
                    <td style="padding:0 28px 20px;">
                      <table role="presentation" width="100%" cellpadding="0" cellspacing="0"
                             style="border:1px solid {Line};border-radius:6px;">
                        <tr>
                          <td align="center" style="padding:18px 12px;border-right:1px solid {Line};">
                            <div style="color:{Muted};font-size:11px;letter-spacing:.12em;text-transform:uppercase;">Block</div>
                            <div style="color:{Ink};font-size:18px;font-weight:600;padding-top:4px;">{Encode(BlockOf(purchase.SeatNumber))}</div>
                          </td>
                          <td align="center" style="padding:18px 12px;border-right:1px solid {Line};">
                            <div style="color:{Muted};font-size:11px;letter-spacing:.12em;text-transform:uppercase;">Seat</div>
                            <div style="color:{Ink};font-size:18px;font-weight:600;padding-top:4px;">{Encode(purchase.SeatNumber)}</div>
                          </td>
                          <td align="center" style="padding:18px 12px;">
                            <div style="color:{Muted};font-size:11px;letter-spacing:.12em;text-transform:uppercase;">Paid</div>
                            <div style="color:{Ink};font-size:18px;font-weight:600;padding-top:4px;">{Encode(price)} {Encode(purchase.Currency)}</div>
                          </td>
                        </tr>
                      </table>
                    </td>
                  </tr>
                  <tr>
                    <td style="padding:0 28px 24px;">
                      <div style="border:1px dashed {Accent};border-radius:6px;padding:16px;text-align:center;">
                        <div style="color:{Muted};font-size:11px;letter-spacing:.12em;text-transform:uppercase;">Show this at the turnstile</div>
                        <div style="color:{Accent};font-size:26px;font-weight:700;letter-spacing:.22em;padding-top:6px;">{Encode(purchase.AccessCode)}</div>
                      </div>
                    </td>
                  </tr>
                  <tr>
                    <td style="padding:0 28px 26px;color:{Muted};font-size:12px;line-height:1.6;">
                      Ticket reference {Encode(purchase.TicketId.ToString())}<br/>
                      Payment reference {Encode(purchase.PaymentTransactionId)}<br/>
                      Purchased {Encode(purchase.PurchasedAtUtc.ToLocalTime().ToString("dd MMMM yyyy, HH:mm", CultureInfo.InvariantCulture))}
                    </td>
                  </tr>
                </table>
                <div style="color:{Muted};font-size:11px;padding-top:14px;">
                  This message was sent by StadiaPass because a ticket was bought with this address.
                </div>
              </td></tr>
            </table>
            """);

        return builder.ToString();
    }

    /// <summary>Seat numbers read BLOCK-ROW-NUMBER, and the block on its own is what people look for.</summary>
    private static string BlockOf(string seatNumber) =>
        seatNumber.Split('-') is [var block, ..] ? block : seatNumber;

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
