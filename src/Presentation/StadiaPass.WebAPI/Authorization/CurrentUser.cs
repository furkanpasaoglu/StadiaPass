using System.Security.Claims;
using StadiaPass.Application.Common.Abstractions;

namespace StadiaPass.WebAPI.Authorization;

internal sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private const string SubjectClaim = "sub";
    private const string PreferredUsernameClaim = "preferred_username";

    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated is true;

    public string Reference =>
        Principal?.FindFirstValue(SubjectClaim)
        ?? throw new InvalidOperationException("The current request is not authenticated.");

    public string DisplayName => Principal?.FindFirstValue(PreferredUsernameClaim) ?? Reference;
}
