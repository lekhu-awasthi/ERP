namespace ErpApp.Domain.Inventory;

/// <summary>
/// Moves a Quantity of one or more Products from FromWarehouseId to ToWarehouseId
/// (architecture-spec.md §3.5/roadmap Phase 7 task 6) -- a pure location move, no value change, so
/// deliberately the one ApprovableTransaction in this codebase with no IGlPostingRule at all
/// (every other document type posts GL on Approve). Same Draft->Approve/Code-at-DraftCode/
/// encapsulated-Lines shape as every other transactional aggregate here (PurchaseOrder etc.) --
/// see that type's doc comment for the shared rationale.
///
/// Approve() itself stays stock-ignorant, same GL-ignorant split every other aggregate here
/// establishes (Invoice/PurchaseBill/JournalVoucher's Approve() doesn't touch GL either) -- it
/// only assigns the real number and flips Status. The Application-layer
/// ApproveWarehouseTransferCommandHandler calls IStockLedgerService.Consume against
/// FromWarehouseId then Increment into ToWarehouseId at the exact weighted-average cost Consume
/// returned, since that needs DB reads/writes Domain can't perform.
/// </summary>
public sealed class WarehouseTransfer
{
    public const string DraftCode = "DRAFT";

    private readonly List<WarehouseTransferLine> _lines = [];

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }

    /// <summary>
    /// Phase 32 (FR-2.3/FR-3.3). The billing location this document was raised from -- the
    /// borderless picker in the live document header, rendering "Name (Code)" and defaulting to
    /// HeadOffice (confirmed live 2026-09-07 on a location-enabled tenant).
    ///
    /// <para>Present on this type although the live default scope
    /// (<see cref="Domain.Tenancy.LocationScopeMode.SalesTransactionsOnly"/>) excludes it: the wider
    /// AllTransactions mode names "Sales, Purchase, Inventory, Accounting, etc.", and that mode is a
    /// runtime setting an Admin can flip at any moment, so the column has to exist before the switch
    /// does. <see cref="Domain.Tenancy.DocumentLocationScope"/> decides which types carry one for a
    /// given tenant; nothing here branches on it.</para>
    /// </summary>
    public Guid? LocationId { get; private set; }
    public Guid FromWarehouseId { get; private set; }
    public Guid ToWarehouseId { get; private set; }
    public string Code { get; private set; } = null!;
    public DateOnly Date { get; private set; }
    public string? Reference { get; private set; }
    public WarehouseTransferStatus Status { get; private set; }
    public Guid? ApprovedByUserId { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public Guid? VoidedByUserId { get; private set; }
    public DateTimeOffset? VoidedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = null!;

    public IReadOnlyList<WarehouseTransferLine> Lines => _lines;

    private WarehouseTransfer()
    {
    }

    public static WarehouseTransfer Create(
        Guid organizationId, Guid fromWarehouseId, Guid toWarehouseId, DateOnly date, string? reference)
    {
        if (fromWarehouseId == toWarehouseId)
        {
            throw new InvalidOperationException("A warehouse transfer's From and To warehouses must differ.");
        }

        return new WarehouseTransfer
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            FromWarehouseId = fromWarehouseId,
            ToWarehouseId = toWarehouseId,
            Code = DraftCode,
            Date = date,
            Reference = reference,
            Status = WarehouseTransferStatus.Draft,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void UpdateHeader(Guid fromWarehouseId, Guid toWarehouseId, DateOnly date, string? reference)
    {
        EnsureDraft();

        if (fromWarehouseId == toWarehouseId)
        {
            throw new InvalidOperationException("A warehouse transfer's From and To warehouses must differ.");
        }

        FromWarehouseId = fromWarehouseId;
        ToWarehouseId = toWarehouseId;
        Date = date;
        Reference = reference;
    }

    public void AddLine(Guid productId, decimal quantity)
    {
        EnsureDraft();

        if (quantity <= 0)
        {
            throw new InvalidOperationException("A warehouse transfer line needs a positive Quantity.");
        }

        _lines.Add(WarehouseTransferLine.Create(Id, productId, quantity));
    }

    public void ClearLines()
    {
        EnsureDraft();
        _lines.Clear();
    }

    public void Approve(Guid approvedByUserId, string code)
    {
        EnsureDraft();

        if (_lines.Count == 0)
        {
            throw new InvalidOperationException("A warehouse transfer needs at least one line to be approved.");
        }

        Status = WarehouseTransferStatus.Approved;
        ApprovedByUserId = approvedByUserId;
        ApprovedAt = DateTimeOffset.UtcNow;
        Code = code;
    }

    public void Void(Guid voidedByUserId)
    {
        EnsureApproved();
        Status = WarehouseTransferStatus.Void;
        VoidedByUserId = voidedByUserId;
        VoidedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Phase 32 -- sets the billing location this document is raised from. Draft-only: an Approved
    /// document's location is what its numbering pool was drawn from (see
    /// DocumentNumberingRule.LocationWiseNumbering) and what every location-filtered report has
    /// already counted it under, so moving it afterwards would silently restate both.
    /// </summary>
    public void SetLocation(Guid? locationId)
    {
        EnsureDraft();
        LocationId = locationId;
    }

    private void EnsureDraft()
    {
        if (Status != WarehouseTransferStatus.Draft)
        {
            throw new InvalidOperationException("This warehouse transfer is no longer in Draft status.");
        }
    }

    private void EnsureApproved()
    {
        if (Status != WarehouseTransferStatus.Approved)
        {
            throw new InvalidOperationException("Only an Approved warehouse transfer can be voided.");
        }
    }
}
