using System.Diagnostics.Metrics;

namespace StadiaPass.AgentHost.Guardrails;

/// <summary>
/// How often the guardrail actually had something to remove, published for Prometheus to scrape.
/// </summary>
/// <remarks>
/// <para>
/// A control nobody can see is a control nobody can trust. This counter is the difference between "we
/// redact personal data" and a number an operator can put on a dashboard beside the token counts that are
/// already there - and, the first week it is switched on, the answer to whether staff are pasting customer
/// details into the analyst at all.
/// </para>
/// <para>
/// It rides the <c>StadiaPass.Agent</c> meter the agent's own telemetry already uses, so nothing had to be
/// registered for it to be collected. The tags carry the kind and the direction and never the value: a
/// metric label is the last place a redacted datum should reappear.
/// </para>
/// </remarks>
internal sealed class GuardrailMetrics : IDisposable
{
    public const string MeterName = "StadiaPass.Agent";

    /// <summary>On its way to the model - what a member of staff typed, or a tool answered.</summary>
    public const string Inbound = "inbound";

    /// <summary>On its way back to whoever asked - what the model said.</summary>
    public const string Outbound = "outbound";

    private readonly Meter _meter;
    private readonly Counter<long> _redactions;

    public GuardrailMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(MeterName);

        _redactions = _meter.CreateCounter<long>(
            "stadiapass.agent.personal_data_redactions",
            unit: "{redaction}",
            description: "Personal data removed from agent text, by kind and by which way it was going.");
    }

    public void Record(PersonalDataKind kind, string direction) =>
        _redactions.Add(
            1,
            new KeyValuePair<string, object?>("kind", Label(kind)),
            new KeyValuePair<string, object?>("direction", direction));

    public void Dispose() => _meter.Dispose();

    // Spelled out rather than taken from the enum's name: these are dashboard labels, and a query somebody
    // saved should not break because a type was renamed in a refactor.
    private static string Label(PersonalDataKind kind) => kind switch
    {
        PersonalDataKind.EmailAddress => "email_address",
        PersonalDataKind.PhoneNumber => "phone_number",
        PersonalDataKind.PaymentCard => "payment_card",
        PersonalDataKind.BankAccount => "bank_account",
        PersonalDataKind.NationalId => "national_id",
        _ => "unknown"
    };
}
