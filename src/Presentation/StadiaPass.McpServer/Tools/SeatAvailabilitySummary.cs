using StadiaPass.McpServer.Api;

namespace StadiaPass.McpServer.Tools;

/// <summary>
/// The seat map folded down to what a language model can actually use. The full map carries every seat in
/// the stadium - tens of thousands of rows of JSON for a big venue - which no model needs to answer "is
/// there room and what does it cost". Counts and price ranges per block say that in a few hundred tokens;
/// picking one concrete seat stays in the UI, where it belongs.
/// </summary>
public sealed record SeatAvailabilitySummary(
    Guid MatchId,
    string HomeTeam,
    string AwayTeam,
    string VenueName,
    DateTimeOffset KickOffUtc,
    string Status,
    int Capacity,
    int AvailableSeatCount,
    decimal? CheapestAvailablePrice,
    string? Currency,
    IReadOnlyList<BlockAvailability> Blocks)
{
    public static SeatAvailabilitySummary FromSeatMap(SeatMap map)
    {
        var blocks = map.Blocks
            .Select(BlockAvailability.FromBlock)
            .ToList();

        var cheapest = blocks
            .Where(block => block.MinAvailablePrice is not null)
            .Min(block => block.MinAvailablePrice);

        var currency = map.Blocks
            .SelectMany(block => block.Rows)
            .SelectMany(row => row.Seats)
            .Select(seat => seat.Currency)
            .FirstOrDefault();

        return new SeatAvailabilitySummary(
            map.MatchId,
            map.HomeTeam,
            map.AwayTeam,
            map.VenueName,
            map.KickOffUtc,
            map.Status,
            map.Capacity,
            map.AvailableSeatCount,
            cheapest,
            currency,
            blocks);
    }
}

public sealed record BlockAvailability(
    string Block,
    int AvailableSeatCount,
    decimal? MinAvailablePrice,
    decimal? MaxAvailablePrice)
{
    internal static BlockAvailability FromBlock(SeatBlock block)
    {
        var availablePrices = block.Rows
            .SelectMany(row => row.Seats)
            .Where(seat => string.Equals(seat.Status, "Available", StringComparison.OrdinalIgnoreCase))
            .Select(seat => (decimal?)seat.Price)
            .ToList();

        return new BlockAvailability(
            block.Block,
            block.AvailableSeatCount,
            availablePrices.Min(),
            availablePrices.Max());
    }
}
