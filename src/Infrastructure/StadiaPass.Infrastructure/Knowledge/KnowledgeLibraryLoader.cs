using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StadiaPass.Application.Knowledge.Commands.IndexKnowledgeDocument;
using StadiaPass.Application.Knowledge.Commands.RemoveWithdrawnKnowledgeDocuments;

namespace StadiaPass.Infrastructure.Knowledge;

/// <summary>
/// Loads every policy document in the documents folder when the API starts, the way the schema and the
/// search index are put in place at start-up.
/// </summary>
/// <remarks>
/// <para>
/// Retried, because two things it needs are not promised to be there yet: the schema, which
/// <c>DatabaseInitializer</c> is creating in parallel, and Ollama, which may still be loading the model.
/// A few attempts a few seconds apart cover both without a readiness handshake between services.
/// </para>
/// <para>
/// Failure is logged and swallowed. The policy assistant is a convenience laid over an API that sells
/// tickets without it, so a folder that is missing or a model that will not answer must cost exactly that
/// feature and not the start of the process.
/// </para>
/// </remarks>
internal sealed partial class KnowledgeLibraryLoader(
    IServiceScopeFactory scopeFactory,
    IOptions<KnowledgeOptions> options,
    ILogger<KnowledgeLibraryLoader> logger) : BackgroundService
{
    private const int Attempts = 12;

    private static readonly TimeSpan Backoff = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, options.Value.DocumentsPath);

        if (!Directory.Exists(directory))
        {
            FolderMissing(logger, directory);

            return;
        }

        var files = Directory.GetFiles(directory, "*.md").Order(StringComparer.Ordinal).ToArray();

        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            try
            {
                await LoadAsync(files, stoppingToken);

                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                if (attempt == Attempts)
                {
                    GaveUp(logger, exception, attempt);

                    return;
                }

                NotReadyYet(logger, attempt, exception.Message);
                await Task.Delay(Backoff, stoppingToken);
            }
        }
    }

    private async Task LoadAsync(string[] files, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var loaded = 0;
        var unchanged = 0;

        foreach (var file in files)
        {
            var document = Path.GetFileNameWithoutExtension(file);
            var markdown = await File.ReadAllTextAsync(file, cancellationToken);

            var result = await sender.Send(new IndexKnowledgeDocumentCommand(document, markdown), cancellationToken);

            if (result.Unchanged)
            {
                unchanged++;
            }
            else
            {
                loaded++;
            }
        }

        // After the documents that are here, the ones that are not: a policy whose file was deleted has
        // to leave the store too, or the assistant keeps quoting a rule that has been withdrawn.
        var current = files.Select(file => Path.GetFileNameWithoutExtension(file)).ToArray();
        var withdrawn = await sender.Send(new RemoveWithdrawnKnowledgeDocumentsCommand(current), cancellationToken);

        LibraryReady(logger, loaded, unchanged, withdrawn.Removed.Count);
    }

    [LoggerMessage(
        EventId = 9100,
        Level = LogLevel.Information,
        Message = "Knowledge library ready: {Loaded} document(s) embedded, {Unchanged} already up to date, {Withdrawn} withdrawn")]
    private static partial void LibraryReady(ILogger logger, int loaded, int unchanged, int withdrawn);

    [LoggerMessage(
        EventId = 9101,
        Level = LogLevel.Warning,
        Message = "Knowledge library not loaded on attempt {Attempt}; trying again shortly: {Reason}")]
    private static partial void NotReadyYet(ILogger logger, int attempt, string reason);

    [LoggerMessage(
        EventId = 9102,
        Level = LogLevel.Error,
        Message = "Knowledge library could not be loaded after {Attempts} attempts; the policy search will find nothing")]
    private static partial void GaveUp(ILogger logger, Exception exception, int attempts);

    [LoggerMessage(
        EventId = 9103,
        Level = LogLevel.Warning,
        Message = "Knowledge documents folder {Directory} does not exist; nothing loaded")]
    private static partial void FolderMissing(ILogger logger, string directory);
}
