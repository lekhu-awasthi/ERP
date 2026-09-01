using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;

namespace ErpApp.Application.Workflow;

/// <summary>
/// The document types an inbox scan can be converted into, and the Create permission each one
/// exercises. Modelled on <c>PrintDocumentPermissions</c> (Phase 20d), the existing precedent in
/// this codebase for "a request whose permission key depends on which DocumentType it names".
///
/// <para><b>Why exactly these four</b> (docs/phase-22-status.md, Decision D): the reference product
/// offers sixteen "+ ADD AS" targets and marks exactly four of them AI-assisted --
/// erp-module-scan.md line 110's four sparkle entries are Quick Payment, Invoice, Expenses and
/// Purchase Bill, which are precisely the four FR-10.3 names. The other twelve are a purely
/// additive seam, not this phase's work.</para>
///
/// <para><b>What a fifth costs:</b> one member in <see cref="Supported"/>, one arm in
/// <see cref="CreatePermissionFor"/>, and the target page reading the prefill it already receives
/// in a target-agnostic shape (<c>ExtractedDocumentData</c>). No new table, no new command, no new
/// permission key. Deliberately a <see cref="DocumentType"/> allow-list rather than a bespoke enum:
/// the linked-transaction pair on <c>UploadedDocument</c> has to be a DocumentType anyway (that is
/// what every other cross-context document reference in this tree is), and a parallel enum would
/// need a mapping nobody could keep honest.</para>
///
/// <para><see cref="DocumentType.Payment"/> covers Quick Payment: Phase 17 built that screen as a
/// thin variant of the ordinary Payment aggregate with <c>Allocations = []</c>, not as its own
/// document type.</para>
/// </summary>
public static class InboxConversionTargets
{
    public static readonly IReadOnlyList<DocumentType> Supported =
    [
        DocumentType.Invoice,
        DocumentType.PurchaseBill,
        DocumentType.Expense,
        DocumentType.Payment,
    ];

    public static bool IsSupported(DocumentType documentType) => Supported.Contains(documentType);

    /// <summary>
    /// The permission a user must already hold to obtain a prefill for -- and therefore to convert
    /// into -- <paramref name="documentType"/>. This is not a second, weaker gate in front of the
    /// real one: it is deliberately the *same* key the ordinary Create command checks a moment
    /// later, so the inbox can never become a side door into creating a document type a user is not
    /// permitted to create.
    /// </summary>
    public static string CreatePermissionFor(DocumentType documentType) => documentType switch
    {
        DocumentType.Invoice => PermissionKeys.InvoiceCreate,
        DocumentType.PurchaseBill => PermissionKeys.PurchaseBillCreate,
        DocumentType.Expense => PermissionKeys.ExpenseCreate,
        DocumentType.Payment => PermissionKeys.PaymentCreate,
        _ => throw new ArgumentOutOfRangeException(
            nameof(documentType), documentType, "Not a supported Document inbox conversion target."),
    };
}
