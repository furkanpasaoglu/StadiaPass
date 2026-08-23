using MediatR;

namespace StadiaPass.Application.Identity.Users.Queries.GetUsers;

public sealed record GetUsersQuery(string? Search = null, int Page = 1, int PageSize = 20)
    : IRequest<UserListDto>;
