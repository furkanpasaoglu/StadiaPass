using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

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
        catch (Exception exception)
        {
            RequestFailed(logger, requestName, exception);
            throw;
        }
    }

    [LoggerMessage(EventId = 1000, Level = LogLevel.Information, Message = "Handling {RequestName}")]
    private static partial void RequestStarted(ILogger logger, string requestName);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "Handled {RequestName} in {ElapsedMs} ms")]
    private static partial void RequestCompleted(ILogger logger, string requestName, double elapsedMs);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Error, Message = "{RequestName} failed")]
    private static partial void RequestFailed(ILogger logger, string requestName, Exception exception);
}
