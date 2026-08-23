using FluentValidation;
using MediatR;
using StadiaPass.Application.Common.Exceptions;

namespace StadiaPass.Application.Common.Behaviors;

internal sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!validators.Any())
        {
            return await next(cancellationToken);
        }

        var context = new ValidationContext<TRequest>(request);

        var results = await Task.WhenAll(
            validators.Select(validator => validator.ValidateAsync(context, cancellationToken)));

        var failures = results.SelectMany(result => result.Errors).Where(failure => failure is not null).ToArray();

        return failures.Length is 0
            ? await next(cancellationToken)
            : throw new RequestValidationException(failures);
    }
}
