namespace ErpApp.Domain.Pos;

/// <summary>
/// Phase 61 -- one cashier's shift at one till: a cash drawer that posts (phase 59 Decision H).
///
/// <para><b>One open session per user and location</b>, as the vendor has it, enforced by a filtered
/// unique index rather than a read-then-write check, so two tabs opening at once cannot both win.</para>
///
/// <para><b>The drawer is one general-ledger account</b>, fixed when the session opens
/// (<see cref="CashAccountId"/>): the account of the Cash-kind payment mode the location's till
/// offers. Every cash tender, every change given and every cash movement moves that account, so the
/// figure a close compares the count against and the figure the ledger holds are the same
/// movements, and the over/short posts against the same account.</para>
///
/// <para><b>The opening float posts nothing.</b> It is a count of what is already in the drawer, and
/// that cash is already in the drawer's account -- it was left there by the last close, or put there
/// by a cash transfer. A float that arrives from somewhere else during the shift is a Cash In naming
/// where it came from.</para>
///
/// <para><b>What a session does not store:</b> its sales, tenders, change and expected cash while
/// open. Those are read from the invoices that name it, by one shared reader that the day report
/// reads too (phase 36's rule, against the vendor's 610.20-versus-611.00, defect 7). Only the close
/// freezes the expected figure, because that is the number the over/short was posted against.</para>
/// </summary>
public sealed class PosSession
{
    public const string CodePrefix = "SES";
    public const int MaxNoteLength = 500;

    private readonly List<PosCashMovement> _cashMovements = [];

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }

    /// <summary><c>SES0001</c>, from the tenant's own counter -- so a session has a name a GL report
    /// row can show, the way every other source of a posting does.</summary>
    public string Code { get; private set; } = null!;

    public Guid BillingLocationId { get; private set; }
    public Guid UserId { get; private set; }
    public Guid CashAccountId { get; private set; }
    public PosSessionStatus Status { get; private set; }
    public DateTimeOffset OpenedAt { get; private set; }

    public decimal OpeningFloat { get; private set; }

    /// <summary>The opening count note by note, or null when the float was entered as one amount.</summary>
    public CashCount? OpeningCount { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    /// <summary>What the drawer should have held at the close: frozen then, because it is what the
    /// over/short was posted against. A void after the close is refused, so it cannot go stale.</summary>
    public decimal? ExpectedCash { get; private set; }

    public decimal? CountedCash { get; private set; }
    public CashCount? ClosingCount { get; private set; }

    /// <summary>Counted minus expected: positive is over, negative is short. The vendor's review shows
    /// "Drawer is short by Rs. 9.00" and posts nothing; this posts it.</summary>
    public decimal? CashDifference { get; private set; }

    public string? ClosingNote { get; private set; }

    /// <summary>Touched by every sale and cash movement. With <see cref="RowVersion"/> it makes a sale
    /// and a close of the same session serial: whichever commits second fails its concurrency check,
    /// so no sale can land in a session after its expected cash was frozen.</summary>
    public DateTimeOffset LastActivityAt { get; private set; }

    public byte[] RowVersion { get; private set; } = null!;

    public IReadOnlyList<PosCashMovement> CashMovements => _cashMovements;

    private PosSession()
    {
    }

    public static PosSession Open(
        Guid organizationId,
        Guid billingLocationId,
        Guid userId,
        Guid cashAccountId,
        string code,
        decimal openingFloat,
        CashCount? openingCount)
    {
        if (billingLocationId == Guid.Empty || userId == Guid.Empty || cashAccountId == Guid.Empty)
        {
            throw new InvalidOperationException("A session names its location, its cashier and its drawer account.");
        }

        EnsureCashAmount(openingFloat, "The opening float");

        if (openingCount is not null && openingCount.Total != openingFloat)
        {
            throw new InvalidOperationException(
                $"The opening float ({openingFloat:0.00}) is not what the counted notes add up to ({openingCount.Total:0.00}).");
        }

        var now = DateTimeOffset.UtcNow;

        return new PosSession
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Code = code,
            BillingLocationId = billingLocationId,
            UserId = userId,
            CashAccountId = cashAccountId,
            Status = PosSessionStatus.Open,
            OpenedAt = now,
            OpeningFloat = openingFloat,
            OpeningCount = openingCount,
            LastActivityAt = now,
        };
    }

    /// <summary>Marks a sale (or a void of one) against this session. Refused once closed, so the guard
    /// a sale relies on is the session's own -- and it changes the row, so the rowversion makes the
    /// sale and a concurrent close serial.</summary>
    public void RecordActivity()
    {
        EnsureOpen();
        LastActivityAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Records cash into or out of the drawer and returns the row, for the caller to add through its
    /// own set (phase 24: a child appended to a tracked parent's collection is otherwise seen as
    /// Modified, not Added).
    /// </summary>
    public PosCashMovement RecordCashMovement(
        PosCashMovementDirection direction, decimal amount, Guid accountId, string? note, Guid userId)
    {
        EnsureOpen();

        if (!Enum.IsDefined(direction))
        {
            throw new InvalidOperationException($"'{direction}' is not a cash movement direction.");
        }

        if (amount <= 0m)
        {
            throw new InvalidOperationException("A cash movement must be a positive amount.");
        }

        EnsureCashAmount(amount, "A cash movement");

        if (accountId == Guid.Empty)
        {
            throw new InvalidOperationException("A cash movement names the account the cash came from or went to.");
        }

        // Moving the drawer's cash into the drawer's own account would post a debit and a credit to
        // one account: an entry that says nothing, and a movement the count would still see.
        if (accountId == CashAccountId)
        {
            throw new InvalidOperationException(
                "A cash movement names where the cash came from or went to, which cannot be the drawer's own account.");
        }

        if (note is { Length: > MaxNoteLength })
        {
            throw new InvalidOperationException($"A note is at most {MaxNoteLength} characters.");
        }

        var movement = PosCashMovement.Create(Id, direction, amount, accountId, note, userId);
        _cashMovements.Add(movement);
        LastActivityAt = movement.CreatedAt;
        return movement;
    }

    /// <summary>
    /// Closes the drawer against what the shared reader says it should hold. A difference needs a
    /// note -- the vendor's review asks for one when the drawer is short -- because it is about to
    /// become a posting someone will ask about.
    /// </summary>
    public void Close(decimal expectedCash, decimal countedCash, CashCount? closingCount, string? note)
    {
        EnsureOpen();
        EnsureCashAmount(countedCash, "The counted cash");

        if (closingCount is not null && closingCount.Total != countedCash)
        {
            throw new InvalidOperationException(
                $"The counted cash ({countedCash:0.00}) is not what the counted notes add up to ({closingCount.Total:0.00}).");
        }

        var difference = countedCash - expectedCash;
        var trimmed = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

        if (difference != 0m && trimmed is null)
        {
            throw new InvalidOperationException(
                $"The drawer is {(difference < 0m ? "short" : "over")} by {Math.Abs(difference):0.00}. "
                + "Add a note saying why before closing.");
        }

        if (trimmed is { Length: > MaxNoteLength })
        {
            throw new InvalidOperationException($"A note is at most {MaxNoteLength} characters.");
        }

        Status = PosSessionStatus.Closed;
        ClosedAt = DateTimeOffset.UtcNow;
        ExpectedCash = expectedCash;
        CountedCash = countedCash;
        ClosingCount = closingCount;
        CashDifference = difference;
        ClosingNote = trimmed;
        LastActivityAt = ClosedAt.Value;
    }

    private void EnsureOpen()
    {
        if (Status != PosSessionStatus.Open)
        {
            throw new InvalidOperationException("This session is closed.");
        }
    }

    private static void EnsureCashAmount(decimal amount, string what)
    {
        if (amount < 0m)
        {
            throw new InvalidOperationException($"{what} cannot be negative.");
        }

        if (decimal.Round(amount, 2) != amount)
        {
            throw new InvalidOperationException($"{what} is whole paisa (at most 2 decimal places).");
        }
    }
}
