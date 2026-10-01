namespace ErpApp.Domain.Pos;

public enum PosSessionStatus
{
    Open = 1,
    Closed = 2,
}

public enum PosCashMovementDirection
{
    In = 1,
    Out = 2,
}

/// <summary>
/// Phase 61 -- cash put into or taken out of a till's drawer during a session, other than by a sale:
/// a float top-up, a supplier paid from the till, a cash drop to the safe.
///
/// <para><b>It names an account, and it posts</b> (phase 59 Decision H). The vendor's Cash Out is an
/// amount and a note, stored on the session and never reaching the ledger (defect 6): its Cash In
/// Hand moved 349 that day while its drawer moved 240. Here a Cash Out to <i>Vegetables</i> debits
/// that account and credits the drawer's, so the general ledger and the drawer move together.</para>
/// </summary>
public sealed class PosCashMovement
{
    public Guid Id { get; private set; }
    public Guid PosSessionId { get; private set; }
    public PosCashMovementDirection Direction { get; private set; }
    public decimal Amount { get; private set; }

    /// <summary>The other side of the drawer: where cash came from (In) or went to (Out).</summary>
    public Guid AccountId { get; private set; }

    public string? Note { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private PosCashMovement()
    {
    }

    internal static PosCashMovement Create(
        Guid posSessionId, PosCashMovementDirection direction, decimal amount, Guid accountId, string? note,
        Guid userId)
    {
        return new PosCashMovement
        {
            Id = Guid.NewGuid(),
            PosSessionId = posSessionId,
            Direction = direction,
            Amount = amount,
            AccountId = accountId,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            CreatedByUserId = userId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>What this movement did to the drawer: positive in, negative out.</summary>
    public decimal SignedAmount => Direction == PosCashMovementDirection.In ? Amount : -Amount;
}
