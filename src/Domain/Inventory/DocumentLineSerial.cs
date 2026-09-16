namespace ErpApp.Domain.Inventory;

/// <summary>
/// Phase 51 -- the serial numbers a <b>document line</b> names, one row per physical unit.
///
/// <para><b>Why a polymorphic table rather than a child collection per line type.</b> A batch is one
/// value per line, so it is one nullable column on the line. A serial cannot be: a line of quantity
/// five names five serials, because the Serial Number tab is one row per physical unit. Giving each
/// line-bearing aggregate its own child collection would be four near-identical entities, four
/// configurations and four encapsulated-collection restatements in <c>TestAppDbContext</c> -- and
/// this codebase already settled the shape for exactly this situation: <c>CommentParentType</c>,
/// <c>TaskParentType</c> and <c>AttachmentParentType</c> are the same mechanism, and phase 18 set
/// the trigger for generalising as "only if/when a second parent type is actually needed". Four
/// were.</para>
///
/// <para><b>This is an input, not a fact.</b> After a document is approved the truth about where a
/// serial is lives in <see cref="StockLedgerEntry.SerialNo"/> and <see cref="StockMovement.SerialNo"/>
/// -- that is what both the Serial Number tab and the Product Serial No Report read. These rows
/// record what the user typed on the line, so a draft can be edited, a detail page can show it back,
/// and Approve knows which units to move. Nothing reports off them.</para>
/// </summary>
public sealed class DocumentLineSerial
{
    public const int SerialNoMaxLength = 100;

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public DocumentLineParentType ParentType { get; private set; }

    /// <summary>The <i>line's</i> id, not the document's -- a serial belongs to one line.</summary>
    public Guid ParentLineId { get; private set; }

    public string SerialNo { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }

    private DocumentLineSerial()
    {
    }

    public static DocumentLineSerial Create(
        Guid organizationId, DocumentLineParentType parentType, Guid parentLineId, string serialNo)
    {
        var trimmed = (serialNo ?? string.Empty).Trim();

        if (trimmed.Length == 0)
        {
            throw new InvalidOperationException("A serial number cannot be blank.");
        }

        if (trimmed.Length > SerialNoMaxLength)
        {
            throw new InvalidOperationException(
                $"A serial number cannot be longer than {SerialNoMaxLength} characters.");
        }

        return new DocumentLineSerial
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ParentType = parentType,
            ParentLineId = parentLineId,
            SerialNo = trimmed,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }
}
