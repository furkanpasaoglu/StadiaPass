using FluentValidation.Results;

namespace StadiaPass.Application.Common.Exceptions;

public sealed class NotFoundException(string resource, object key)
    : Exception($"{resource} with identifier '{key}' was not found.")
{
    public string Resource { get; } = resource;

    public object Key { get; } = key;
}

public sealed class ConflictException(string message) : Exception(message);

public sealed class RequestValidationException : Exception
{
    public RequestValidationException(IEnumerable<ValidationFailure> failures)
        : base("One or more validation failures occurred.") =>
        Errors = failures
            .GroupBy(failure => failure.PropertyName, failure => failure.ErrorMessage)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}
