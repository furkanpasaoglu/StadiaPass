namespace StadiaPass.Domain.Common.ValueObjects;

public sealed record SeatNumber
{
    private SeatNumber(string block, int row, int number)
    {
        Block = block;
        Row = row;
        Number = number;
    }

    public string Block { get; }

    public int Row { get; }

    public int Number { get; }

    public static SeatNumber Create(string block, int row, int number)
    {
        if (string.IsNullOrWhiteSpace(block) || block.Length > 10)
        {
            throw new DomainRuleViolationException(
                "SeatNumber.InvalidBlock", "Seat block must be between 1 and 10 characters.");
        }

        if (row is <= 0 or > 500)
        {
            throw new DomainRuleViolationException(
                "SeatNumber.InvalidRow", "Seat row must be between 1 and 500.");
        }

        if (number is <= 0 or > 500)
        {
            throw new DomainRuleViolationException(
                "SeatNumber.InvalidNumber", "Seat number must be between 1 and 500.");
        }

        return new SeatNumber(block.Trim().ToUpperInvariant(), row, number);
    }

    public static SeatNumber Parse(string value)
    {
        var segments = value.Split('-', StringSplitOptions.TrimEntries);

        return segments is [var block, var row, var number]
               && int.TryParse(row, out var parsedRow)
               && int.TryParse(number, out var parsedNumber)
            ? Create(block, parsedRow, parsedNumber)
            : throw new DomainRuleViolationException(
                "SeatNumber.InvalidFormat", $"'{value}' is not a valid seat number (expected BLOCK-ROW-NUMBER).");
    }

    public override string ToString() => $"{Block}-{Row}-{Number}";
}
