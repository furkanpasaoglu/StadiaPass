using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;
using StadiaPass.Application.Common.Exceptions;
using StadiaPass.Domain.Common;

namespace StadiaPass.Application.Common.Behaviors;

internal sealed partial class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        var timestamp = Stopwatch.GetTimestamp();

        RequestStarted(logger, requestName);

        try
        {
            var response = await next(cancellationToken);
            var elapsedMs = Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds;
            RequestCompleted(logger, requestName, elapsedMs);
            return response;
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            // A rejected request is a normal outcome, not a defect: log why, at warning, without a stack
            // trace, so real failures stay visible in the logs.
            RequestRejected(logger, requestName, Describe(exception));
            throw;
        }
        catch (Exception exception)
        {
            RequestFailed(logger, requestName, exception);
            throw;
        }
    }

    private static bool IsExpected(Exception exception) =>
        exception is RequestValidationException or NotFoundException or ConflictException or DomainException;

    private static string Describe(Exception exception) => exception switch
    {
        RequestValidationException validation => string.Join(
            "; ",
            validation.Errors.Select(error => $"{error.Key}: {string.Join(", ", error.Value)}")),
        _ => exception.Message
    };

    [LoggerMessage(EventId = 1000, Level = LogLevel.Information, Message = "Handling {RequestName}")]
    private static partial void RequestStarted(ILogger logger, string requestName);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "Handled {RequestName} in {ElapsedMs} ms")]
    private static partial void RequestCompleted(ILogger logger, string requestName, double elapsedMs);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Error, Message = "{RequestName} failed")]
    private static partial void RequestFailed(ILogger logger, string requestName, Exception exception);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Warning, Message = "{RequestName} rejected: {Reason}")]
    private static partial void RequestRejected(ILogger logger, string requestName, string reason);
}
