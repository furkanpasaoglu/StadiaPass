using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using StadiaPass.Application.Common.Exceptions;
using StadiaPass.Domain.Common;
using StadiaPass.Infrastructure.Identity;

namespace StadiaPass.WebAPI.Extensions;

internal sealed partial class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var problemDetails = Map(exception);

        httpContext.Response.StatusCode = problemDetails.Status ?? StatusCodes.Status500InternalServerError;

        if (httpContext.Response.StatusCode >= StatusCodes.Status500InternalServerError)
        {
            UnhandledException(logger, exception);
        }

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problemDetails
        });
    }

    private static ProblemDetails Map(Exception exception) => exception switch
    {
        RequestValidationException validation => new ValidationProblemDetails(validation.Errors.ToDictionary())
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "One or more validation errors occurred.",
            Type = "https://tools.ietf.org/html/rfc9110#section-15.5.1"
        },
        NotFoundException notFound => new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = "Resource not found",
            Detail = notFound.Message,
            Type = "https://tools.ietf.org/html/rfc9110#section-15.5.5"
        },
        KeycloakAdminException { StatusCode: System.Net.HttpStatusCode.Conflict } keycloakConflict => new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "Conflict",
            Detail = "The identity provider rejected the change because it conflicts with existing data.",
            Type = "https://tools.ietf.org/html/rfc9110#section-15.5.10",
            Extensions = { ["providerMessage"] = keycloakConflict.Message }
        },
        KeycloakAdminException => new ProblemDetails
        {
            Status = StatusCodes.Status502BadGateway,
            Title = "Identity provider unavailable",
            Detail = "The Keycloak Admin API could not complete the request.",
            Type = "https://tools.ietf.org/html/rfc9110#section-15.6.3"
        },
        PaymentFailedException payment => new ProblemDetails
        {
            Status = StatusCodes.Status422UnprocessableEntity,
            Title = "Payment declined",
            Detail = payment.Message,
            Type = "https://tools.ietf.org/html/rfc9110#section-15.5.21",
            Extensions = { ["paymentFailureCode"] = payment.Code }
        },
        ConflictException conflict => new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "Conflict",
            Detail = conflict.Message,
            Type = "https://tools.ietf.org/html/rfc9110#section-15.5.10"
        },
        DomainException domain => new ProblemDetails
        {
            Status = StatusCodes.Status422UnprocessableEntity,
            Title = "Business rule violation",
            Detail = domain.Message,
            Type = "https://tools.ietf.org/html/rfc9110#section-15.5.21"
        },
        _ => new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "An unexpected error occurred.",
            Type = "https://tools.ietf.org/html/rfc9110#section-15.6.1"
        }
    };

    [LoggerMessage(EventId = 5000, Level = LogLevel.Error, Message = "Unhandled exception while processing request")]
    private static partial void UnhandledException(ILogger logger, Exception exception);
}
