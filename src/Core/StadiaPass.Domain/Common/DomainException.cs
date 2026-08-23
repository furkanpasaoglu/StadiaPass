namespace StadiaPass.Domain.Common;

public class DomainException(string message) : Exception(message);

public sealed class DomainRuleViolationException(string rule, string message)
    : DomainException(message)
{
    public string Rule { get; } = rule;
}
