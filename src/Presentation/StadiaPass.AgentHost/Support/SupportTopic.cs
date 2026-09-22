namespace StadiaPass.AgentHost.Support;

/// <summary>Where a staff question belongs.</summary>
public enum SupportTopic
{
    /// <summary>Facts about specific fixtures: what is on, seats left, prices, what a match has taken.</summary>
    Catalogue,

    /// <summary>The rules: refunds, seat holds, payments, who may do what, stadium entry.</summary>
    Policy,

    /// <summary>Neither: a greeting, a thank-you, or something this host does not cover.</summary>
    Other,

    /// <summary>
    /// Both at once: a fact about a match and a rule in one question. Sending it to either assistant alone
    /// answers half of it and says nothing about the other half, so it goes to both.
    /// </summary>
    Mixed
}
