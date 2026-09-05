namespace ErpApp.Domain.Communications;

/// <summary>
/// What an <see cref="EmailSendLog"/> hangs off — the fourth polymorphic parent enum in this
/// codebase, after <c>TaskParentType</c>, <c>AttachmentParentType</c> and <c>CommentParentType</c>.
///
/// <para><b>Why a fourth rather than reusing <c>AttachmentParentType</c></b>, whose Contact + 15
/// document members are a strict superset of these: because the applicable set genuinely differs,
/// and that is exactly the fact phase 27a built <c>DocumentMechanisms</c> to record. Send Email
/// exists on **7 of the 15** transactional types, live-confirmed one document at a time
/// (docs/phase-30-status.md, Step 1.2) — reusing a wider enum would let a caller construct a log
/// row against a Purchase Bill, which has no Send Email action and no email template context, and
/// nothing would catch it. See <c>DocumentMechanisms.Emailable</c>, which is the classification,
/// and <c>DocumentMechanismSweepGuardTests</c>, which fails the build if the two disagree.</para>
///
/// <para>Contact is first, matching <c>AttachmentParentType</c>/<c>CommentParentType</c>. Document
/// members are named identically to their <see cref="ErpApp.Domain.Common.DocumentType"/>
/// counterparts so <c>DocumentParentTypes</c>' by-name bridge works — never an ordinal cast, which
/// could not work here even in principle, since these seven members and DocumentType's twenty-plus
/// share no ordinal order at all.</para>
/// </summary>
public enum EmailParentType
{
    Contact,

    Quotation,
    SalesOrder,
    Invoice,
    CreditNote,
    Payment,
    PurchaseOrder,
}
