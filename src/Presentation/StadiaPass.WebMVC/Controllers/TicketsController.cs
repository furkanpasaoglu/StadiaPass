using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StadiaPass.WebMVC.Models;
using StadiaPass.WebMVC.Services;

namespace StadiaPass.WebMVC.Controllers;

[Authorize]
public sealed class TicketsController(IStadiaPassApiClient apiClient) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(Guid matchId, CancellationToken cancellationToken)
    {
        var matches = await apiClient.GetUpcomingMatchesAsync(cancellationToken);

        if (matches.Count is 0)
        {
            return View("NoMatches");
        }

        var match = matches.FirstOrDefault(candidate => candidate.Id == matchId) ?? matches[0];
        var tickets = await apiClient.GetTicketsByMatchAsync(match.Id, cancellationToken);

        ViewBag.Matches = matches;

        return View(new TicketBoardViewModel { Match = match, Tickets = tickets });
    }

    [HttpGet]
    public async Task<IActionResult> Create(Guid matchId, CancellationToken cancellationToken)
    {
        ViewBag.Matches = await apiClient.GetUpcomingMatchesAsync(cancellationToken);

        return View(new CreateTicketInput { MatchId = matchId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateTicketInput input, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            var result = await apiClient.CreateTicketAsync(input, cancellationToken);

            if (result.Succeeded)
            {
                TempData["Message"] = $"Ticket {result.Value!.SeatNumber} issued.";

                return RedirectToAction(nameof(Index), new { matchId = input.MatchId });
            }

            ApplyErrors(result);
        }

        ViewBag.Matches = await apiClient.GetUpcomingMatchesAsync(cancellationToken);

        return View(input);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reserve(
        Guid ticketId,
        Guid matchId,
        string holderReference,
        CancellationToken cancellationToken)
    {
        var result = await apiClient.ReserveTicketAsync(ticketId, holderReference, cancellationToken);

        TempData[result.Succeeded ? "Message" : "Error"] = result.Succeeded
            ? $"Seat {result.Value!.SeatNumber} reserved for {holderReference}."
            : result.Error;

        return RedirectToAction(nameof(Index), new { matchId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmSale(Guid ticketId, Guid matchId, CancellationToken cancellationToken)
    {
        var result = await apiClient.ConfirmSaleAsync(ticketId, cancellationToken);

        TempData[result.Succeeded ? "Message" : "Error"] = result.Succeeded
            ? $"Seat {result.Value!.SeatNumber} sold."
            : result.Error;

        return RedirectToAction(nameof(Index), new { matchId });
    }

    private void ApplyErrors(ApiResult<TicketSummary> result)
    {
        if (result.ValidationErrors is null)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "The request could not be completed.");

            return;
        }

        foreach (var (property, messages) in result.ValidationErrors)
        {
            foreach (var message in messages)
            {
                ModelState.AddModelError(property, message);
            }
        }
    }
}
