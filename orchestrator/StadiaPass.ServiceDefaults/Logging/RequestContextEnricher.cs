using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Serilog.Core;
using Serilog.Events;

namespace StadiaPass.ServiceDefaults.Logging;

/// <summary>
/// Stamps every log event raised while an HTTP request is in flight with the identity and the correlation
/// key needed to follow that request across the API, the MVC front end and Keycloak.
/// </summary>
internal sealed class RequestContextEnricher(IHttpContextAccessor httpContextAccessor) : ILogEventEnricher
{
    /// <summary>Honoured when a caller supplies one, so a trace can start outside StadiaPass.</summary>
    public const string CorrelationHeader = "X-Correlation-ID";

    private const string SubjectClaim = "sub";

    private const string PreferredUsernameClaim = "preferred_username";

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        if (httpContextAccessor.HttpContext is not { } httpContext)
        {
            return;
        }

        Add(logEvent, propertyFactory, "CorrelationId", ResolveCorrelationId(httpContext));

        if (httpContext.User.Identity?.IsAuthenticated is not true)
        {
            return;
        }

        // Resolved from the Keycloak token: the subject is the stable identifier, the username is for humans.
        Add(logEvent, propertyFactory, "UserId", httpContext.User.FindFirstValue(SubjectClaim));
        Add(logEvent, propertyFactory, "UserName", httpContext.User.FindFirstValue(PreferredUsernameClaim));
    }

    private static string ResolveCorrelationId(HttpContext httpContext) =>
        httpContext.Request.Headers.TryGetValue(CorrelationHeader, out var supplied)
        && supplied.ToString() is { Length: > 0 } value
            ? value
            : System.Diagnostics.Activity.Current?.TraceId.ToString() ?? httpContext.TraceIdentifier;

    private static void Add(
        LogEvent logEvent,
        ILogEventPropertyFactory propertyFactory,
        string name,
        string? value)
    {
        if (value is { Length: > 0 })
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(name, value));
        }
    }
}
