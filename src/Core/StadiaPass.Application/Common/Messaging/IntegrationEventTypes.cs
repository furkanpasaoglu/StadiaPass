using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using StadiaPass.Application.Payments.Events;
using StadiaPass.Application.Tickets.Events;

namespace StadiaPass.Application.Common.Messaging;

/// <summary>
/// Every message this system is willing to put on the wire - written to the outbox on the way out, or
/// recorded in the inbox on the way in - and read back off it.
/// </summary>
/// <remarks>
/// A row in a database is data, and turning the name in that row into a type by asking the runtime for
/// whatever it happens to be called would mean deserializing whatever the row says. Rows only get there
/// through code in this solution today, and that is exactly the assumption worth not building on. This list
/// is the whole set: a name that is not in it is refused on the way in and never deserialized on the way out.
/// </remarks>
public static class IntegrationEventTypes
{
    private static readonly FrozenDictionary<string, Type> ByName =
        new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            [typeof(TicketPurchasedEvent).FullName!] = typeof(TicketPurchasedEvent),
            [typeof(PaymentSucceeded).FullName!] = typeof(PaymentSucceeded),
            [typeof(PaymentDisputed).FullName!] = typeof(PaymentDisputed),
            [typeof(PaymentRefunded).FullName!] = typeof(PaymentRefunded),
            [typeof(RefundOwedEvent).FullName!] = typeof(RefundOwedEvent)
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>
    /// The full type name, which is also what MassTransit derives its exchange name from - so renaming or
    /// moving one of these types is a change to the wire contract, not a refactor.
    /// </summary>
    public static string NameOf(object message) => message.GetType().FullName!;

    public static bool IsKnown(object message) => ByName.ContainsKey(NameOf(message));

    public static bool TryResolve(string name, [NotNullWhen(true)] out Type? messageType) =>
        ByName.TryGetValue(name, out messageType);
}
