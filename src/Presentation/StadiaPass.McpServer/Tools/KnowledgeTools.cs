using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using StadiaPass.McpServer.Api;

namespace StadiaPass.McpServer.Tools;

/// <summary>
/// The policy documents, for AI clients. Offered only when the server has a service account, because the
/// documents are staff-facing and the API says so.
/// </summary>
[McpServerToolType]
internal sealed class KnowledgeTools(ICatalogueApiClient catalogue)
{
    [McpServerTool(Name = "search_policies", ReadOnly = true)]
    [Description(
        "Finds the sections of the staff policy documents nearest to a question - refund rules, seat "
        + "holds, sales conditions, who may do what, stadium entry - under 'hits', nearest first, each "
        + "with the document, its title, the section heading, the section text and a similarity score. "
        + "There is always a nearest section: a low score or a section about a nearby topic means the "
        + "documents may not cover the question, so answer only from what the text actually says and "
        + "say when it does not. Staff-facing rules, not for quoting to customers verbatim.")]
    public async Task<PolicySearchResult> SearchPoliciesAsync(
        [Description("The question, in Turkish or English, as the member of staff asked it.")]
        string question,
        [Description("How many sections to bring back; three is what an answer is usually written from.")]
        int limit = 3,
        CancellationToken cancellationToken = default) =>
        await catalogue.SearchPoliciesAsync(question, limit, cancellationToken)
            ?? throw new McpException("The policy search returned an empty response.");
}
