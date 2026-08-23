using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using StadiaPass.Application.Common.Authorization;

namespace StadiaPass.WebAPI.Authorization;

internal sealed partial class PermissionAuthorizationHandler(ILogger<PermissionAuthorizationHandler> logger)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        if (context.User.HasClaim(StadiaPassClaimTypes.Permission, requirement.Permission))
        {
            context.Succeed(requirement);
        }
        else
        {
            PermissionDenied(logger, requirement.Permission, context.User.Identity?.Name ?? "anonymous");
        }

        return Task.CompletedTask;
    }

    [LoggerMessage(
        EventId = 6000,
        Level = LogLevel.Warning,
        Message = "Permission {Permission} denied for {User}")]
    private static partial void PermissionDenied(ILogger logger, string permission, string user);
}
