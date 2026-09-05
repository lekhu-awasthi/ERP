namespace ErpApp.Domain.Configuration;

/// <summary>
/// What an <see cref="EmailTemplate"/> is written for. Live-confirmed 2026-09-05 off the reference
/// product's own Template Type picker (docs/phase-30-status.md, Step 1.1), which offers eight
/// options; <see cref="General"/> is the ninth, read off an existing row rather than the picker.
///
/// <para><b>This is not <see cref="CustomTemplateType"/> and must never be folded into it.</b> The
/// two vocabularies are disjoint — that enum names four kinds of *letter*, this one names the
/// *document* an email is about — and the reference product serves them from two different
/// resources (`/erp/email-templates` versus `/erp/custom-templates?type=…`). Phase 27b added
/// `CustomTemplateType.Email` on the strength of the two sharing a Configurations panel; the panel
/// is a UI grouping and the data model underneath is a different shape. See Decision B.</para>
///
/// <para><b>Why CustomerPayment and SupplierPayment are separate members</b> when this codebase has
/// one <c>DocumentType.Payment</c> discriminated by <c>PaymentDirection</c>: because the reference
/// product ships genuinely different templates for the two ("Thank You for Payment" versus
/// "Payment Confirmation"), and a template written to thank a customer must never be offered on a
/// payment to a supplier. So the mapping into this enum is
/// (<c>DocumentType</c>, <c>PaymentDirection</c>) rather than <c>DocumentType</c> alone — see
/// <c>EmailTemplateContexts.For</c>. This is phase-27a's "the four live payment kinds collapse onto
/// one <c>DocumentType</c>" observation running the other way for once: here the live distinction
/// carries real meaning and is worth keeping.</para>
/// </summary>
public enum EmailTemplateContext
{
    /// <summary>Not about any document — the Contact detail page's own Send Email action. Live, its
    /// template picker offers exactly the General-context templates (docs/phase-30-status.md,
    /// Step 1.3).</summary>
    General,

    Quotation,
    SalesOrder,
    Invoice,
    CreditNote,

    /// <summary><c>DocumentType.Payment</c> with <c>PaymentDirection.Received</c>.</summary>
    CustomerPayment,

    /// <summary><c>DocumentType.Payment</c> with <c>PaymentDirection.Paid</c>.</summary>
    SupplierPayment,

    PurchaseOrder,

    /// <summary>The Customer/Supplier Balance Confirmation letter phase 27b built, not a document.
    /// Present in the live picker; this codebase has no Send Email action that uses it yet, so it
    /// is offered on the template screen and consumed by nothing. See "Follow-ups".</summary>
    BalanceConfirmation,
}
