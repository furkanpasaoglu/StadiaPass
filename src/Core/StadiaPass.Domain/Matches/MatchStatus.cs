namespace StadiaPass.Domain.Matches;

/// <summary>
/// What a fixture is doing about selling seats. Three values, and every one of them is reached by code.
/// </summary>
/// <remarks>
/// <para>
/// This enum used to carry <c>Scheduled</c>, <c>Postponed</c> and <c>Played</c> as well. None of them was
/// ever assigned: no command, no handler and no worker set them, so no fixture in this system could hold
/// one. They read like features to anybody scanning the model, and the seat map still carried a message for
/// each that no visitor could ever be shown.
/// </para>
/// <para>
/// What they were reaching for is covered without them. A fixture that has been played is a fixture whose
/// kick-off has passed, which the clock answers without anything having to run around marking rows - see
/// <see cref="Match.KickOffUtc"/> and the sales window. A fixture not yet on sale would need a sale-opening
/// date the model does not have, and inventing a status would not have given it one.
/// </para>
/// <para>
/// Stored as text rather than as its ordinal, so these numbers carry no meaning in the database and adding
/// or removing a value cannot silently re-label existing rows.
/// </para>
/// </remarks>
public enum MatchStatus
{
    /// <summary>Seats may be held and bought.</summary>
    OnSale = 0,

    /// <summary>Every seat is sold. Set and cleared by the aggregate as seats move, never by a caller.</summary>
    SoldOut = 1,

    /// <summary>The fixture will not be played. Nothing more can be sold; what was sold is given back.</summary>
    Cancelled = 2
}
