using Microsoft.AspNetCore.Authorization;

namespace StadiaPass.WebAPI.Authorization;

internal sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;

    public override string ToString() => $"Permission: {Permission}";
}
