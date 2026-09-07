namespace ErpApp.Domain.Inventory;

/// <summary>
/// Corrects a Product's on-hand quantity/value in a Warehouse -- stock take, damage, write-off
/// (architecture-spec.md §3.5/roadmap Phase 7 task 7). Unlike WarehouseTransfer, this DOES post
/// GL on Approve -- a quantity/value correction is a real asset-value/P&amp;L event, not a location
/// move (see Application.Inventory.Posting.InventoryAdjustmentPostingRule's doc comment for the
/// net-balanced-effect design, worked out on paper first per phase-6-status.md bug #3's lesson
/// about reversal/paired-effect documents).
///
/// Same Draft->Approve/Code-at-DraftCode/encapsulated-Lines shape as every other transactional
/// aggregate here. Approve() stays stock-and-GL-ignorant (only assigns the real number and flips
/// Status) -- ApproveInventoryAdjustmentCommandHandler resolves each Decrease line's actual FIFO
/// cost via IStockLedgerService and builds the GL entry, since both need DB reads/writes Domain
/// can't perform.
/// </summary>
public sealed class InventoryAdjustment
{
    public const string DraftCode = "DRAFT";

    private readonly List<InventoryAdjustmentLine> _lines = [];

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
    public Guid WarehouseId { get; private set; }
    public string Code { get; private set; } = null!;
    public DateOnly Date { get; private set; }
    public string? Reference { get; private set; }
    public InventoryAdjustmentStatus Status { get; private set; }
    public Guid? ApprovedByUserId { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public Guid? VoidedByUserId { get; private set; }
    public DateTimeOffset? VoidedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = null!;

    public IReadOnlyList<InventoryAdjustmentLine> Lines => _lines;

    private InventoryAdjustment()
    {
    }

    public static InventoryAdjustment Create(Guid organizationId, Guid warehouseId, DateOnly date, string? reference)
    {
        return new InventoryAdjustment
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            WarehouseId = warehouseId,
            Code = DraftCode,
            Date = date,
            Reference = reference,
            Status = InventoryAdjustmentStatus.Draft,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void UpdateHeader(Guid warehouseId, DateOnly date, string? reference)
    {
        EnsureDraft();
        WarehouseId = warehouseId;
        Date = date;
        Reference = reference;
    }

    public void AddLine(Guid productId, InventoryAdjustmentDirection direction, decimal quantity, decimal unitCost)
    {
        EnsureDraft();

        if (quantity <= 0)
        {
            throw new InvalidOperationException("An inventory adjustment line needs a positive Quantity.");
        }

        if (direction == InventoryAdjustmentDirection.Increase && unitCost < 0)
        {
            throw new InvalidOperationException("An Increase line's Unit Cost cannot be negative.");
        }

        _lines.Add(InventoryAdjustmentLine.Create(Id, productId, direction, quantity, unitCost));
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
            throw new InvalidOperationException("An inventory adjustment needs at least one line to be approved.");
        }

        Status = InventoryAdjustmentStatus.Approved;
        ApprovedByUserId = approvedByUserId;
        ApprovedAt = DateTimeOffset.UtcNow;
        Code = code;
    }

    public void Void(Guid voidedByUserId)
    {
        EnsureApproved();
        Status = InventoryAdjustmentStatus.Void;
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
        if (Status != InventoryAdjustmentStatus.Draft)
        {
            throw new InvalidOperationException("This inventory adjustment is no longer in Draft status.");
        }
    }

    private void EnsureApproved()
    {
        if (Status != InventoryAdjustmentStatus.Approved)
        {
            throw new InvalidOperationException("Only an Approved inventory adjustment can be voided.");
        }
    }
}
