using MediatR;

namespace StadiaPass.Application.Identity.Roles.Queries.GetRoles;

/// <summary>Business roles plus the permission catalogue the role editor renders as a checklist.</summary>
public sealed record GetRolesQuery : IRequest<RoleListDto>;
