using StadiaPass.Domain.Matches;

namespace StadiaPass.Application.Matches;

public sealed record MatchDto(
    Guid Id,
    string HomeTeam,
    string AwayTeam,
    string Stadium,
    DateTimeOffset KickOffUtc,
    int Capacity,
    int IssuedTicketCount,
    int RemainingCapacity,
    string Status);

internal static class MatchMappings
{
    public static MatchDto ToDto(this Match match) => new(
        match.Id,
        match.HomeTeam,
        match.AwayTeam,
        match.Stadium,
        match.KickOffUtc,
        match.Capacity,
        match.IssuedTicketCount,
        match.RemainingCapacity,
        match.Status.ToString());
}
