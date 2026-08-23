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
}
