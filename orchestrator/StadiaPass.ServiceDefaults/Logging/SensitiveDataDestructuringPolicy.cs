using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Serilog.Core;
using Serilog.Events;

namespace StadiaPass.ServiceDefaults.Logging;

/// <summary>
/// The MediatR pipeline destructures every request so a failed command can be read back in full. Some of
/// those commands carry a password or a client secret, and a log line is exactly the wrong place for one, so
/// members that look like a credential are replaced before the event is ever written.
/// </summary>
internal sealed class SensitiveDataDestructuringPolicy : IDestructuringPolicy
{
    public const string Mask = "***redacted***";

    private static readonly string[] SensitiveFragments =
        ["password", "secret", "token", "credential", "apikey", "accesscode"];

    public bool TryDestructure(
        object value,
        ILogEventPropertyValueFactory propertyValueFactory,
        [NotNullWhen(true)] out LogEventPropertyValue? result)
    {
        var type = value.GetType();

        if (!LooksLikeApplicationRequest(type))
        {
            result = null;

            return false;
        }

        var properties = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead && property.GetIndexParameters().Length is 0)
            .ToArray();

        if (!properties.Any(property => IsSensitive(property.Name)))
        {
            result = null;

            return false;
        }

        result = new StructureValue(
            properties.Select(property => new LogEventProperty(
                property.Name,
                IsSensitive(property.Name)
                    ? new ScalarValue(Mask)
                    : propertyValueFactory.CreatePropertyValue(ReadValue(property, value), destructureObjects: true))),
            type.Name);

        return true;
    }

    private static bool LooksLikeApplicationRequest(Type type) =>
        type.Namespace?.StartsWith("StadiaPass.", StringComparison.Ordinal) is true;

    private static bool IsSensitive(string memberName) =>
        SensitiveFragments.Any(fragment => memberName.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    private static object? ReadValue(PropertyInfo property, object instance)
    {
        try
        {
            return property.GetValue(instance);
        }
        catch (TargetInvocationException)
        {
            // A computed property that throws must not take the log line down with it.
            return null;
        }
    }
}
