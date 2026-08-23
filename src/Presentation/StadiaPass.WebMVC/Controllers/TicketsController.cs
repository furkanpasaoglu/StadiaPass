using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StadiaPass.SharedKernel.Authorization;
using StadiaPass.WebMVC.Services;

namespace StadiaPass.WebMVC.Controllers;

[Authorize(Policy = StadiaPassPermissions.Tickets.View)]
public sealed class TicketsController(IStadiaPassApiClient apiClient) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(await apiClient.GetMyTicketsAsync(cancellationToken));
}
