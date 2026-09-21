namespace StadiaPass.Application.Knowledge;

/// <summary>
/// One section of a policy document, as the model will read it.
/// </summary>
/// <param name="Document">The document it came from, by the name it was loaded under.</param>
/// <param name="Title">The document's own title, as written at the top of it.</param>
/// <param name="Heading">The section heading. Empty for text that came before the first heading.</param>
/// <param name="Text">
/// The section, with a first line naming the document and the heading. That line is for the embedding as
/// much as for the reader: "İade yapılmaz" on its own sits as close to "iade" as the paragraph saying the
/// opposite, and "İade Politikası > Müşterinin vazgeçmesi" in front of it is what tells them apart.
/// </param>
/// <param name="Position">Where in the document the section sits, counting from zero.</param>
public sealed record KnowledgeChunk(
    string Document,
    string Title,
    string Heading,
    string Text,
    int Position);
