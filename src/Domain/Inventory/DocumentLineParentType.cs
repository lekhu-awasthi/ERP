namespace ErpApp.Domain.Inventory;

/// <summary>
/// Phase 51 -- which line-bearing aggregate a <see cref="DocumentLineSerial"/> hangs off, mirroring
/// <c>CommentParentType</c> / <c>TaskParentType</c> / <c>AttachmentParentType</c>.
///
/// <para><b>The member set is the four stock-moving document types whose lines can name a serial</b>,
/// and that is a rule rather than a sample: the two the 2026-09-16 read shows the control on
/// (Invoice, Purchase Bill), plus the two that carry it through from a source line (Credit Note from
/// its Invoice, Debit Note from its Purchase Bill). Every other stock path either preserves what it
/// consumed (Warehouse Transfer) or refuses a tracked product outright with a named 409 -- see
/// <c>StockTrackingRules</c>, which enumerates the refusals with reasons and is guard-tested.</para>
///
/// <para>Never bridge this onto <c>DocumentType</c> by ordinal cast (CLAUDE.md, phase 27a): the two
/// enums overlap in meaning and not in ordering, so the bridge is by name with
/// <c>Enum.TryParse</c> and is pinned by a divergent-ordinal guard test.</para>
/// </summary>
public enum DocumentLineParentType
{
    InvoiceLine = 1,
    PurchaseBillLine = 2,
    CreditNoteLine = 3,
    DebitNoteLine = 4,
}
