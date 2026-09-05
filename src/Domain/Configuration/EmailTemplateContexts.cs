using ErpApp.Domain.Common;
using ErpApp.Domain.Communications;
using ErpApp.Domain.Payments;

namespace ErpApp.Domain.Configuration;

/// <summary>
/// Phase 30 — maps this codebase's document vocabulary onto <see cref="EmailTemplateContext"/>, and
/// back.
///
/// <para>This exists because the two vocabularies do not line up one-to-one, and the place where
/// they do not is load-bearing: <see cref="DocumentType.Payment"/> is one type here and <i>two</i>
/// contexts live, split by <see cref="PaymentDirection"/>. A by-name bridge like
/// <c>DocumentParentTypes</c>' therefore cannot serve — <c>Payment</c> has no counterpart member —
/// so this is an explicit switch, and the ordinal-cast trap phase 26a named is avoided by there
/// being no cast anywhere in the file.</para>
///
/// <para><c>DocumentMechanismSweepGuardTests</c> asserts that <see cref="DocumentMechanisms.Emailable"/>
/// and the document-bearing members of <see cref="EmailTemplateContext"/> describe the same set in
/// both directions, so neither can drift.</para>
/// </summary>
public static class EmailTemplateContexts
{
    /// <summary>
    /// The context an email about this document is written for. <paramref name="paymentDirection"/>
    /// is required for <see cref="DocumentType.Payment"/> and ignored otherwise.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The type has no Send Email action — see
    /// <see cref="DocumentMechanisms.Emailable"/>. Throwing rather than returning null is
    /// deliberate: every caller has already been through that list, so a miss is a wiring bug, not
    /// user input.</exception>
    public static EmailTemplateContext For(DocumentType documentType, PaymentDirection? paymentDirection = null)
    {
        return documentType switch
        {
            DocumentType.Quotation => EmailTemplateContext.Quotation,
            DocumentType.SalesOrder => EmailTemplateContext.SalesOrder,
            DocumentType.Invoice => EmailTemplateContext.Invoice,
            DocumentType.CreditNote => EmailTemplateContext.CreditNote,
            DocumentType.PurchaseOrder => EmailTemplateContext.PurchaseOrder,
            DocumentType.Payment => paymentDirection switch
            {
                PaymentDirection.Received => EmailTemplateContext.CustomerPayment,
                PaymentDirection.Paid => EmailTemplateContext.SupplierPayment,
                _ => throw new ArgumentNullException(
                    nameof(paymentDirection),
                    "A Payment's email context depends on its direction -- Customer Payment and "
                        + "Supplier Payment are different contexts with different templates."),
            },
            _ => throw new ArgumentOutOfRangeException(
                nameof(documentType),
                documentType,
                $"{documentType} has no Send Email action. See DocumentMechanisms.Emailable."),
        };
    }

    /// <summary>The document type a context is about, or null for the two contexts that are not
    /// about a document (<see cref="EmailTemplateContext.General"/>,
    /// <see cref="EmailTemplateContext.BalanceConfirmation"/>).</summary>
    public static DocumentType? DocumentTypeFor(EmailTemplateContext context) => context switch
    {
        EmailTemplateContext.Quotation => DocumentType.Quotation,
        EmailTemplateContext.SalesOrder => DocumentType.SalesOrder,
        EmailTemplateContext.Invoice => DocumentType.Invoice,
        EmailTemplateContext.CreditNote => DocumentType.CreditNote,
        EmailTemplateContext.CustomerPayment => DocumentType.Payment,
        EmailTemplateContext.SupplierPayment => DocumentType.Payment,
        EmailTemplateContext.PurchaseOrder => DocumentType.PurchaseOrder,
        _ => null,
    };

    /// <summary>The log parent an email in this context hangs off.
    /// <see cref="EmailTemplateContext.General"/> and
    /// <see cref="EmailTemplateContext.BalanceConfirmation"/> are both about a Contact rather than a
    /// document.</summary>
    public static EmailParentType ParentTypeFor(EmailTemplateContext context)
    {
        var documentType = DocumentTypeFor(context);

        return documentType is null
            ? EmailParentType.Contact
            : DocumentParentTypes.For<EmailParentType>(documentType.Value);
    }
}
