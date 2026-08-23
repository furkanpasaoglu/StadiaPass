using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace StadiaPass.WebMVC.Authentication;

/// <summary>
/// Copies the access token obtained during the OIDC login onto every outgoing API call, so the API
/// authorizes the end user rather than the web application itself.
/// </summary>
internal sealed class TokenBearerHandler(IHttpContextAccessor httpContextAccessor) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (httpContextAccessor.HttpContext is { User.Identity.IsAuthenticated: true } httpContext
            && await httpContext.GetTokenAsync(OpenIdConnectParameterNames.AccessToken) is { Length: > 0 } accessToken)
        {
            request.Headers.Authorization =
                new AuthenticationHeaderValue(JwtBearerDefaults.AuthenticationScheme, accessToken);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
