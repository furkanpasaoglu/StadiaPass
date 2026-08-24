namespace StadiaPass.Application.Common.Abstractions;

/// <summary>
/// The signed-in caller. Handlers take the seat holder from here instead of from the request body, so a
/// customer can never reserve or buy a seat in somebody else's name.
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    /// <summary>Stable reference used as the seat holder, typically the Keycloak subject.</summary>
    string Reference { get; }

    string DisplayName { get; }

    /// <summary>
    /// Where to write to them, when there is anywhere. Null when the account carries no address or the token
    /// was issued without the <c>email</c> scope, so anything that sends must decide what to do about that
    /// rather than assume.
    /// </summary>
    string? Email { get; }

    /// <summary>
    /// Permission strings come from <c>StadiaPassPermissions</c>; a handler asks this when the endpoint
    /// policy alone cannot decide, such as reading a ticket that may or may not belong to the caller.
    /// </summary>
    bool HasPermission(string permission);
}
