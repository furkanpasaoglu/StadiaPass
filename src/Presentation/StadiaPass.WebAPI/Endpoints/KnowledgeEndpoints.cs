using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using StadiaPass.Application.Knowledge.Queries.SearchKnowledge;
using StadiaPass.SharedKernel.Authorization;

namespace StadiaPass.WebAPI.Endpoints;

/// <summary>
/// The policy documents, for the people behind the counter.
/// </summary>
internal sealed class KnowledgeEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder builder)
    {
        var group = builder
            .MapGroup("/api/v1/knowledge")
            .WithTags("Knowledge");

        group.MapGet("/search", SearchAsync)
            .WithName("SearchKnowledge")
            .WithSummary("Finds the sections of the policy documents nearest to a question, nearest first.")
            .ProducesValidationProblem()
            .RequireAuthorization(StadiaPassPermissions.Knowledge.Search);
    }

    private static async Task<Ok<KnowledgeSearchResultDto>> SearchAsync(
        string q,
        int? limit,
        ISender sender,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new SearchKnowledgeQuery(q, limit ?? 3), cancellationToken));
}
