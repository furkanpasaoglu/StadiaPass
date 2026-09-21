using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using StadiaPass.Application.Knowledge;
using StadiaPass.Application.Knowledge.Commands.IndexKnowledgeDocument;
using StadiaPass.Application.Knowledge.Commands.RemoveKnowledgeDocument;
using StadiaPass.Application.Knowledge.Queries.SearchKnowledge;
using StadiaPass.SharedKernel.Authorization;

namespace StadiaPass.WebAPI.Endpoints;

/// <summary>
/// The policy documents: searching them is for the people behind the counter, changing them is for the
/// administrator.
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

        // PUT, not POST: the caller names the document, and sending the same one twice leaves one copy.
        group.MapPut("/documents/{document}", UploadAsync)
            .WithName("UploadKnowledgeDocument")
            .WithSummary("Uploads a policy document, or replaces one uploaded earlier under the same name.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(StadiaPassPermissions.Knowledge.Manage);

        group.MapDelete("/documents/{document}", RemoveAsync)
            .WithName("RemoveKnowledgeDocument")
            .WithSummary("Withdraws an uploaded policy document, so the assistant stops answering from it.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(StadiaPassPermissions.Knowledge.Manage);
    }

    private static async Task<Ok<KnowledgeSearchResultDto>> SearchAsync(
        string q,
        int? limit,
        ISender sender,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new SearchKnowledgeQuery(q, limit ?? 3), cancellationToken));

    private static async Task<Ok<IndexKnowledgeDocumentResultDto>> UploadAsync(
        string document,
        UploadKnowledgeDocumentRequest request,
        ISender sender,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(
            new IndexKnowledgeDocumentCommand(document, request.Markdown, KnowledgeOrigin.Uploaded),
            cancellationToken));

    private static async Task<NoContent> RemoveAsync(
        string document,
        ISender sender,
        CancellationToken cancellationToken)
    {
        await sender.Send(new RemoveKnowledgeDocumentCommand(document), cancellationToken);

        return TypedResults.NoContent();
    }
}

/// <param name="Markdown">The document: a '# ' title, then a '## ' heading for each rule with its text under it.</param>
public sealed record UploadKnowledgeDocumentRequest(string Markdown);
