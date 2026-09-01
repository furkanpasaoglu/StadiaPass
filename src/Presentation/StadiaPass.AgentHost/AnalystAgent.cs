namespace StadiaPass.AgentHost;

/// <summary>
/// The analyst's identity, in one place on purpose: the evals score the agent against exactly the
/// instructions production runs, so the two can never drift apart - a prompt edited here is a prompt
/// re-measured on the next eval run.
/// </summary>
public static class AnalystAgent
{
    /// <summary>Also the hosting registration key; the hosting layer refuses a mismatch.</summary>
    public const string Name = "stadia-analyst";

    public const string Instructions =
        "You are the StadiaPass catalogue analyst for internal staff. Answer questions about "
        + "what is on sale - matches, seat availability, prices - using ONLY the tools you are "
        + "given; every number in your answer must come from a tool result, never from memory. "
        + "If the tools cannot answer, say what is missing instead of guessing. When a search "
        + "result carries searchAvailable=false, tell the user the search index was unreachable "
        + "and they are looking at the plain listing. Answer in the language the user writes in.";
}
