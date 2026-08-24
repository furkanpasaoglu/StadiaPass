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

/// <summary>
/// The payment provider declined the charge. The seat is left exactly as it was - still held for the caller
/// until the hold runs out - so they can try again with another card.
/// </summary>
public sealed class PaymentFailedException(string code, string message) : Exception(message)
{
    /// <summary>The provider's decline code, e.g. <c>insufficient_funds</c>.</summary>
    public string Code { get; } = code;
}

/// <summary>
/// A row changed between the moment this request read it and the moment it tried to write it back, so the
/// write was refused rather than allowed to overwrite somebody else's change. Thrown by the persistence layer
/// in place of EF Core's own <c>DbUpdateConcurrencyException</c>, which this layer cannot see.
/// </summary>
public sealed class ConcurrencyConflictException(string message, Exception? innerException = null)
    : Exception(message, innerException);
