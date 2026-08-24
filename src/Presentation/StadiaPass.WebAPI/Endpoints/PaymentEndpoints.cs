using Microsoft.AspNetCore.Http.HttpResults;
using StadiaPass.Application.Infrastructure.Abstractions;

namespace StadiaPass.WebAPI.Endpoints;

/// <summary>
/// Where the payment provider talks back.
/// </summary>
/// <remarks>
/// Everything else in this API is behind a permission. This one cannot be: Stripe has no account here and
/// nothing to present. Its security is the signature over the body, checked before a single byte is believed,
/// and that is why the raw text is read by hand instead of being bound to a model - a signature is over the
/// bytes that were sent, and anything that reshapes them on the way in destroys it.
/// </remarks>
internal sealed class PaymentEndpoints : IEndpoint
{
    private const string SignatureHeader = "Stripe-Signature";

    public void MapEndpoint(IEndpointRouteBuilder builder)
    {
        builder.MapPost("/api/v1/payments/webhook", ReceiveAsync)
            .WithTags("Payments")
            .WithName("ReceivePaymentWebhook")
            .WithSummary("Receives a signed event from the payment provider.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .AllowAnonymous()
            // Nothing here is for a person to read, and publishing it invites people to try it.
            .ExcludeFromDescription();
    }

    /// <summary>
    /// Verify, write it down, say yes. Nothing else: the provider is waiting, and it retries anything that
    /// takes too long. The work the event causes happens on the other side of the inbox, where nobody is
    /// holding a connection open.
    /// </summary>
    private static async Task<Results<Ok, BadRequest<string>>> ReceiveAsync(
        HttpRequest request,
        IPaymentWebhookReader reader,
        IInbox inbox,
        CancellationToken cancellationToken)
    {
        using var body = new StreamReader(request.Body);
        var payload = await body.ReadToEndAsync(cancellationToken);

        var signature = request.Headers[SignatureHeader].ToString();

        if (!reader.TryRead(payload, signature, out var webhookEvent))
        {
            // Deliberately terse. An unsigned caller learns that it failed and nothing else about why.
            return TypedResults.BadRequest("The signature could not be verified.");
        }

        if (webhookEvent.Message is not null)
        {
            // A false answer means this event has been here before, which is not a problem: a provider that
            // guarantees at-least-once delivery is doing exactly what it promised, and the right reply is the
            // same 200 the first copy got.
            await inbox.TryRecordAsync(
                webhookEvent.ProviderEventId,
                webhookEvent.ProviderEventType,
                webhookEvent.Message,
                cancellationToken);
        }

        return TypedResults.Ok();
    }
}
