namespace StadiaPass.Application.Knowledge;

/// <summary>
/// Where a policy document came from, which is also who owns it.
/// </summary>
/// <remarks>
/// A library document is owned by its file: the loader puts it in at start-up, replaces it when the file
/// changes and removes it when the file goes. An uploaded document is owned by the API: it came in through
/// the upload endpoint and only leaves through the delete endpoint. The start-up clean-up has to tell the
/// two apart, or it would remove every uploaded document for having no file - which none of them ever had.
/// </remarks>
public enum KnowledgeOrigin
{
    Library,
    Uploaded
}
