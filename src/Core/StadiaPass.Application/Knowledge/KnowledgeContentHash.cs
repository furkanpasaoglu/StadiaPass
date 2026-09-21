using System.Security.Cryptography;
using System.Text;

namespace StadiaPass.Application.Knowledge;

/// <summary>
/// A fingerprint of a document's text, so "has it changed since it was embedded" is one string comparison.
/// </summary>
public static class KnowledgeContentHash
{
    public static string Of(string markdown) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(markdown)));
}
