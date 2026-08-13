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
    public Guid FromWarehouseId { get; private set; }
    public Guid ToWarehouseId { get; private set; }
    public string Code { get; private set; } = null!;
    public DateOnly Date { get; private set; }
    public string? Reference { get; private set; }
    public WarehouseTransferStatus Status { get; private set; }
    public Guid? ApprovedByUserId { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
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

    private void EnsureDraft()
    {
        if (Status != WarehouseTransferStatus.Draft)
        {
            throw new InvalidOperationException("This warehouse transfer is no longer in Draft status.");
        }
    }
}
