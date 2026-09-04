namespace ErpApp.Domain.Common;

/// <summary>
/// Phase 27a -- the single source of truth for <b>which cross-cutting mechanism applies to which
/// document type</b>, and the thing every 27a guard test reads.
///
/// <para>Phases 19, 20a and 20b each wired one mechanism onto two document types and left the rest
/// as "mechanical follow-up", recorded only in an <c>ArgumentOutOfRangeException</c> message and a
/// paragraph in a status doc. That is exactly the shape that rots: the failure mode of a sweep phase
/// is not getting one wrong today, it is a later phase adding a document type and silently getting
/// none of the four mechanisms -- no compiler error, no failing test, just a screen missing its
/// Custom Fields block that nobody notices for a year.</para>
///
/// <para>So the applicability lists live here, once, and <c>DocumentMechanismSweepGuardTests</c>
/// fails the build if any <see cref="DocumentType"/> is neither classified as transactional nor
/// given a written reason in <see cref="NotApplicableReasons"/>. Adding an enum member is therefore
/// a deliberate, reviewed act rather than silent drift -- the same discipline as phase-23's
/// <c>sweep-guard.spec.ts</c> and phase-24's <c>ProductVariantSweepGuardTests</c>.</para>
///
/// <para>Every list below was live-confirmed against the Tigg UAT tenant on 2026-09-03; the pass is
/// written up in docs/phase-27a-status.md. Where a list is narrower than "all transactional types"
/// that is a confirmed live fact, not an omission.</para>
/// </summary>
public static class DocumentMechanisms
{
    /// <summary>
    /// The 15 real transactional document types -- everything with a Draft/Approve lifecycle, a
    /// document number and a detail page. The other nine <see cref="DocumentType"/> members are
    /// numbering-pool stubs, non-numbered posting sources, or non-documents; see
    /// <see cref="NotApplicableReasons"/>.
    /// </summary>
    public static readonly IReadOnlyList<DocumentType> Transactional =
    [
        DocumentType.Quotation,
        DocumentType.SalesOrder,
        DocumentType.Invoice,
        DocumentType.CreditNote,
        DocumentType.Payment,
        DocumentType.PurchaseOrder,
        DocumentType.PurchaseBill,
        DocumentType.Expense,
        DocumentType.DebitNote,
        DocumentType.JournalVoucher,
        DocumentType.CashTransfer,
        DocumentType.WarehouseTransfer,
        DocumentType.InventoryAdjustment,
        DocumentType.ProductionOrder,
        DocumentType.ProductionJournal,
    ];

    /// <summary>
    /// Types carrying a Custom Fields block on their own create/edit form (Phase 20a's editor).
    ///
    /// <para><b>13, not 15.</b> Configurations &gt; Custom Fields renders one section per applicable
    /// document type and shows exactly 16 live: Sales Invoice, Quotation, Sales Order, Credit Note,
    /// Customer Payment, Quick Receipt, Purchase Order, Purchase Bill, Expense, Debit Note, Supplier
    /// Payment, Quick Payment, Journal Voucher, Cash Transfer, Production Order, Production Journal.
    /// The four live payment kinds all collapse onto this codebase's single
    /// <see cref="DocumentType.Payment"/>, giving 13. <b>Warehouse Transfer and Inventory Adjustment
    /// have no section at all</b> -- so this list is deliberately narrower than
    /// <see cref="ReportingTags"/>, which those two do carry. The roadmap's "remaining 15" was
    /// arithmetic over this codebase's own enum, not a live count.</para>
    /// </summary>
    public static readonly IReadOnlyList<DocumentType> CustomFields =
    [
        DocumentType.Quotation,
        DocumentType.SalesOrder,
        DocumentType.Invoice,
        DocumentType.CreditNote,
        DocumentType.Payment,
        DocumentType.PurchaseOrder,
        DocumentType.PurchaseBill,
        DocumentType.Expense,
        DocumentType.DebitNote,
        DocumentType.JournalVoucher,
        DocumentType.CashTransfer,
        DocumentType.ProductionOrder,
        DocumentType.ProductionJournal,
    ];

    /// <summary>
    /// Types carrying a per-row custom-status picker in their <b>list grid</b> (Phase 20b's third
    /// shape -- no detail-page presence at all).
    ///
    /// <para>Configurations &gt; Custom Status has five sections; four are wired here. Sales Order's
    /// grid column is labelled STAGE and Production Order's STATUS, but both are the same control
    /// over the same lookup. <b>Cheque is excluded, not deferred</b> -- its custom-status values are
    /// the exact five members of the native <c>ChequeStatus</c> enum and its grid column appears to
    /// drive that lifecycle rather than sit orthogonal to it (phase-20b's finding, unchanged).
    /// Production <i>Journal</i> has no such column: its grid is Date/Code/Reference/Product/
    /// Quantity only.</para>
    /// </summary>
    public static readonly IReadOnlyList<DocumentType> CustomStatus =
    [
        DocumentType.Quotation,
        DocumentType.SalesOrder,
        DocumentType.PurchaseOrder,
        DocumentType.ProductionOrder,
    ];

    /// <summary>
    /// Types carrying Reporting Tags (Phase 19's <c>TransactionReportingTag</c>).
    ///
    /// <para><b>All 15 transactional types plus both opening-balance kinds = 17.</b> The detail-page
    /// chrome is uniform -- a REPORTING TAGS block with an Add/Edit action in the left profile panel
    /// -- and was sampled live across three different modules (Invoice, Journal Voucher, Warehouse
    /// Transfer, the last carrying six real tags). Warehouse Transfer proves the point that this
    /// list is wider than <see cref="CustomFields"/>.</para>
    ///
    /// <para>Both Opening Balances tabs carry an inline "Add Reporting Tags" link in the row form,
    /// saved with that row's own SAVE CHANGES -- so <see cref="DocumentType.OpeningStock"/> is
    /// taggable too, which the roadmap's "plus Opening Balances" did not anticipate. These two are
    /// tagged per <i>row</i> (one per account, one per product+warehouse), keyed by the line's own
    /// Id -- the same identity <c>GlJournalEntry.SourceDocumentId</c> already uses for them.</para>
    /// </summary>
    public static readonly IReadOnlyList<DocumentType> ReportingTags =
    [
        .. Transactional,
        DocumentType.OpeningBalance,
        DocumentType.OpeningStock,
    ];

    /// <summary>
    /// Types whose detail page carries the Tasks / Documents / Activity tabs alongside Overview.
    ///
    /// <para>Live-confirmed as exactly four tabs -- Overview / Tasks / Documents / Activity --
    /// identical on Invoice, Journal Voucher and Warehouse Transfer. There is <b>no top-level
    /// Comments tab</b>: the comment composer and feed live inside Activity, whose sub-tabs are
    /// Comments / Activities / Emails (three, unlike the Contact tab's four -- a document has no SMS
    /// History).</para>
    /// </summary>
    public static readonly IReadOnlyList<DocumentType> DetailTabs = Transactional;

    /// <summary>
    /// Types whose detail page offers "View Print Preview" -- Phase 20d built the pipeline for six
    /// of them, Phase 27b wired the remaining nine.
    ///
    /// <para><b>All 15, with no exceptions.</b> The 2026-09-03 confirm-live pass opened every one of
    /// the nine unwired types on the reference tenant and found the action present on all of them,
    /// including both production documents -- which the roadmap had flagged as genuinely unknown.
    /// Print is not gated by module or by document richness: a Warehouse Transfer, which carries no
    /// money at all, prints exactly as an Invoice does. <c>Send Email</c> is the narrower action of
    /// the two, appearing only on Invoice, Credit Note and Payment; it is Phase 30's concern, not
    /// this list's.</para>
    /// </summary>
    public static readonly IReadOnlyList<DocumentType> Printable = Transactional;

    /// <summary>
    /// Types carrying the "+ Add Terms and Conditions" block on their create/edit form, seeded from
    /// a <c>CustomTemplate</c> of type <c>TermsAndConditions</c> (Phase 27b).
    ///
    /// <para><b>Five, and the roadmap said two.</b> The roadmap scoped this as "Quotation/Invoice";
    /// the live pass opened all eight line-item add forms and found the block on Quotation, Sales
    /// Order, Invoice, Credit Note <i>and</i> Purchase Order, and absent from Purchase Bill, Expense
    /// and Debit Note. The dividing line is what the document <i>is</i>: the five that carry terms
    /// are offers and agreements this organization issues, the three that do not are records of
    /// something already agreed elsewhere. Same shape of correction as Phase 27a's Custom Fields
    /// count -- the roadmap's number was arithmetic, this one is a count of real screens.</para>
    /// </summary>
    public static readonly IReadOnlyList<DocumentType> TermsAndConditions =
    [
        DocumentType.Quotation,
        DocumentType.SalesOrder,
        DocumentType.Invoice,
        DocumentType.CreditNote,
        DocumentType.PurchaseOrder,
    ];

    /// <summary>
    /// Every <see cref="DocumentType"/> that is deliberately outside every sweep, with the
    /// reason. The guard test requires that this dictionary plus <see cref="Transactional"/> cover
    /// the enum exactly -- so a new member added by a later phase fails the build until someone
    /// decides which side it falls on.
    /// </summary>
    public static readonly IReadOnlyDictionary<DocumentType, string> NotApplicableReasons =
        new Dictionary<DocumentType, string>
        {
            [DocumentType.Account] =
                "A numbering pool for Chart-of-Accounts codes, not a document. An Account has no " +
                "detail page of the document shape and no Custom Fields section live.",
            [DocumentType.Contact] =
                "A numbering pool for Contact codes. The Contact record does have Tasks/Documents/" +
                "Activity tabs, but it got them in Phase 18 through its own Contact-scoped " +
                "components -- it is not swept as a document here.",
            [DocumentType.Product] =
                "A numbering pool for Item codes; same reasoning as Contact, and Products carry no " +
                "Custom Fields section live.",
            [DocumentType.OpeningBalance] =
                "Not transactional -- no lifecycle, no number, no detail page. It is nonetheless in " +
                "ReportingTags: its row form carries an inline Add Reporting Tags link.",
            [DocumentType.OpeningStock] =
                "Same as OpeningBalance, and likewise in ReportingTags via the Opening Balances " +
                "Product tab's row form.",
            [DocumentType.DataExport] =
                "An audit-attribution marker for a data-egress action (Phase 21b). Nothing numbers " +
                "it, nothing posts it, and there is no record to attach anything to.",
            [DocumentType.MigratedSalesEntry] =
                "A migrated register row imported at cutover (Phase 21c) -- deliberately not a " +
                "document, with no lifecycle and no page of its own.",
            [DocumentType.MigratedPurchaseEntry] =
                "The Purchase Book counterpart of MigratedSalesEntry, same reasoning.",
            [DocumentType.DocumentExtraction] =
                "An audit-attribution marker for one AI extraction run (Phase 22), not a document.",
        };
}
