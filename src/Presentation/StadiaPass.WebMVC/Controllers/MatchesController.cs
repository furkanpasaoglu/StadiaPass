using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StadiaPass.SharedKernel.Authorization;
using StadiaPass.WebMVC.Models;
using StadiaPass.WebMVC.Services;

namespace StadiaPass.WebMVC.Controllers;

/// <summary>
/// Customer facing slice. Browsing is deliberately anonymous - a visitor sees the fixtures and the seat map
/// exactly like a real ticketing site - and only holding or buying a seat requires a sign-in.
/// </summary>
public sealed class MatchesController(IStadiaPassApiClient apiClient) : Controller
{
    /// <summary>
    /// One screen, two ways in. Typing searches the whole catalogue and the answer comes back in relevance
    /// order; the category tabs browse it. A search is not narrowed by the tab that happened to be open -
    /// somebody typing a team name means that team, not that team within basketball - so a term takes over
    /// and the tabs reset to All, which is also what makes clearing the box put everything back.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Index(string? category, string? q, CancellationToken cancellationToken)
    {
        var term = q?.Trim();

        if (term is { Length: > 0 })
        {
            var search = await apiClient.SearchMatchesAsync(term, cancellationToken);

            return View(new MatchListViewModel
            {
                Matches = search.Matches,
                Categories = CategoriesOf(search.Matches),
                SelectedCategory = null,
                Query = term,
                SearchAvailable = search.SearchAvailable
            });
        }

        var matches = await apiClient.GetMatchesAsync(category, cancellationToken);

        // The filter tabs come from what is actually on sale rather than a hard-coded list, so a category
        // added in the back office shows up here on its own.
        var categories = category is { Length: > 0 }
            ? await apiClient.GetMatchesAsync(null, cancellationToken)
            : matches;

        return View(new MatchListViewModel
        {
            Matches = matches,
            Categories = CategoriesOf(categories),
            SelectedCategory = category
        });
    }

    private static IReadOnlyList<string> CategoriesOf(IReadOnlyList<MatchSummary> matches) =>
        [.. matches.Select(match => match.Category).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> SeatSelection(
        Guid id,
        string? seat,
        CancellationToken cancellationToken)
    {
        var seatMap = await apiClient.GetSeatMapAsync(id, cancellationToken);

        if (seatMap is null)
        {
            return NotFound();
        }

        var hold = ReadHold(id);

        if (hold is not null && !IsReserved(seatMap, hold.Value.SeatNumber))
        {
            ForgetHold(id);
            hold = null;
        }

        return View(new SeatSelectionViewModel
        {
            SeatMap = seatMap,
            SelectedSeatNumber = hold?.SeatNumber,
            // Carried back from the sign-in round trip so the visitor lands on the seat they clicked.
            PendingSeatNumber = seat,
            ReservationExpiresAtUtc = hold?.ExpiresAtUtc
        });
    }

    /// <summary>
    /// What to put in front of the customer when the API refuses. A validation problem carries its detail
    /// per field - "That card number is not valid.", "The card has expired." - while its title is the
    /// generic "One or more validation errors occurred.", which tells a customer nothing about what to
    /// change. The redirect that follows cannot carry ModelState, so the field messages are joined into the
    /// banner instead of being dropped.
    /// </summary>
    private static string Explain<T>(ApiResult<T> result) =>
        result.ValidationErrors is { Count: > 0 } errors
            ? string.Join(" ", errors.SelectMany(entry => entry.Value).Distinct(StringComparer.Ordinal))
            : result.Error ?? "The request could not be completed.";

    private static bool IsReserved(SeatMap seatMap, string seatNumber) =>
        seatMap.Blocks
            .SelectMany(block => block.Rows)
            .SelectMany(row => row.Seats)
            .Any(candidate => string.Equals(candidate.SeatNumber, seatNumber, StringComparison.Ordinal)
                              && candidate.Status == "Reserved");

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = StadiaPassPermissions.Tickets.Reserve)]
    public async Task<IActionResult> Reserve(Guid id, string seatNumber, CancellationToken cancellationToken)
    {
        var result = await apiClient.ReserveSeatAsync(id, seatNumber, cancellationToken);

        if (result.Succeeded)
        {
            TempData["Message"] = $"Seat {seatNumber} is held for you until "
                                  + $"{result.Value!.ReservationExpiresAtUtc.ToLocalTime():HH:mm:ss}.";
            RememberHold(id, seatNumber, result.Value.ReservationExpiresAtUtc);
        }
        else
        {
            TempData["Error"] = Explain(result);
        }

        return RedirectToAction(nameof(SeatSelection), new { id });
    }

    /// <summary>
    /// The card is read off the form, handed to the API and forgotten. Nothing but the error message goes
    /// into TempData on failure - carrying card details through a redirect would put them in a cookie. The
    /// seat does not need carrying either: the hold cookie still names it.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = StadiaPassPermissions.Tickets.Purchase)]
    public async Task<IActionResult> Purchase(Guid id, PurchaseInput input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Check the card details and try again.";

            return RedirectToAction(nameof(SeatSelection), new { id });
        }

        var result = await apiClient.PurchaseAsync(id, input, cancellationToken);

        if (!result.Succeeded)
        {
            // A decline is not a dead end: the seat is still held, so the customer lands back on the seat
            // map with the panel open and can try another card while the hold lasts.
            TempData["Error"] = Explain(result);

            return RedirectToAction(nameof(SeatSelection), new { id });
        }

        TempData["Message"] = $"Ticket {result.Value!.AccessCode} issued for seat {result.Value.SeatNumber}.";
        ForgetHold(id);

        return RedirectToAction("Index", "Tickets");
    }

    // ---------------------------------------------------------------------------------------------------
    // The hold cookie.
    //
    // A hold outlives the redirect that created it: the customer has ten minutes, and refreshing the page or
    // stepping away to look at their tickets must not lose the checkout panel. TempData is read-once, so it
    // cannot carry this - the seat would look like somebody else's the moment the page was reloaded. The
    // cookie expires exactly when the hold does, and the seat map is still the authority: a hold whose seat
    // no longer reads as reserved is dropped on sight.
    // ---------------------------------------------------------------------------------------------------

    private const string HoldCookiePrefix = ".StadiaPass.Hold.";

    private static string HoldCookieName(Guid matchId) => HoldCookiePrefix + matchId.ToString("N");

    private void RememberHold(Guid matchId, string seatNumber, DateTimeOffset expiresAtUtc) =>
        Response.Cookies.Append(
            HoldCookieName(matchId),
            // Stamped with who holds it: a shared browser must not offer the next person a checkout panel
            // for a seat that is not theirs, even though the API would refuse the charge anyway.
            $"{User.Identity?.Name}|{seatNumber}|{expiresAtUtc:O}",
            new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                Secure = Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                Expires = expiresAtUtc
            });

    private void ForgetHold(Guid matchId) => Response.Cookies.Delete(HoldCookieName(matchId));

    private (string SeatNumber, DateTimeOffset ExpiresAtUtc)? ReadHold(Guid matchId)
    {
        if (!Request.Cookies.TryGetValue(HoldCookieName(matchId), out var value))
        {
            return null;
        }

        var parts = value.Split('|', 3);

        if (parts.Length is not 3
            || !DateTimeOffset.TryParse(
                parts[2],
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out var expiresAtUtc)
            || expiresAtUtc <= DateTimeOffset.UtcNow)
        {
            ForgetHold(matchId);

            return null;
        }

        // Somebody else's hold on a shared browser: left alone to expire on its own rather than deleted out
        // from under whoever it belongs to.
        return string.Equals(parts[0], User.Identity?.Name, StringComparison.Ordinal)
            ? (parts[1], expiresAtUtc)
            : null;
    }
}
