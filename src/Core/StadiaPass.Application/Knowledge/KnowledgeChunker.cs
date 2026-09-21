using System.Text;

namespace StadiaPass.Application.Knowledge;

/// <summary>
/// Cuts a Markdown policy document into the pieces the model is handed one at a time.
/// </summary>
/// <remarks>
/// <para>
/// One chunk per second-level heading, and that is the whole strategy. These documents are written a
/// section per rule, so a heading is already the boundary between "this is about cancelled matches" and
/// "this is about changing your mind" - which is exactly the boundary retrieval has to respect. Cutting by
/// character count instead would split a rule from its exception, and a chunk that ends mid-sentence is
/// a chunk the model completes from imagination.
/// </para>
/// <para>
/// No Markdown library, on purpose. The documents are ours, they use a title and section headings and
/// nothing else, and a parser that understood tables and nested lists would be a parser nobody here can
/// explain in an interview.
/// </para>
/// </remarks>
public static class KnowledgeChunker
{
    private const string TitleMarker = "# ";

    private const string HeadingMarker = "## ";

    public static IReadOnlyList<KnowledgeChunk> Chunk(string document, string markdown)
    {
        var title = document;
        var heading = string.Empty;
        var body = new StringBuilder();
        var chunks = new List<KnowledgeChunk>();

        foreach (var rawLine in markdown.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');

            if (line.StartsWith(HeadingMarker, StringComparison.Ordinal))
            {
                Flush(chunks, document, title, heading, body);
                heading = line[HeadingMarker.Length..].Trim();
                continue;
            }

            if (line.StartsWith(TitleMarker, StringComparison.Ordinal))
            {
                title = line[TitleMarker.Length..].Trim();
                continue;
            }

            body.AppendLine(line);
        }

        Flush(chunks, document, title, heading, body);

        return chunks;
    }

    /// <summary>Turns what has been collected into a chunk, unless nothing but whitespace was collected.</summary>
    private static void Flush(
        List<KnowledgeChunk> chunks,
        string document,
        string title,
        string heading,
        StringBuilder body)
    {
        var text = body.ToString().Trim();
        body.Clear();

        if (text.Length is 0)
        {
            return;
        }

        var context = heading.Length is 0 ? title : $"{title} > {heading}";

        chunks.Add(new KnowledgeChunk(
            document,
            title,
            heading,
            $"{context}{Environment.NewLine}{Environment.NewLine}{text}",
            chunks.Count));
    }
}
