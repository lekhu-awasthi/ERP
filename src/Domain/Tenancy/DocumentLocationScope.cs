using ErpApp.Domain.Common;

namespace ErpApp.Domain.Tenancy;

/// <summary>
/// The single place that answers "does a document of this type carry a billing location, for a tenant
/// configured this way?". One rule, one function -- phase-30's lesson applied deliberately: <i>a list
/// sampled from a few screens becomes a wrong list; find the rule.</i> Here the reference product
/// states the rule itself, in the label of the control that switches it, so this class transcribes a
/// rule rather than enumerating observations.
///
/// <para>Live 2026-09-07, Organization &gt; Features &gt; Billing Location &gt; Advanced:</para>
/// <list type="bullet">
/// <item><b>Enable Location in Sales Transactions Only</b> (badged Default) -- <i>"Use locations only
/// in sales-related transactions. <b>(Invoice, Sales Order, POS, Credit Note)</b>"</i></item>
/// <item><b>Enable Location in All Transactions</b> -- <i>"Apply location tracking across all
/// transaction modules. (Sales, Purchase, Inventory, Accounting, etc.)"</i></item>
/// </list>
///
/// <para><b>The sales-only set is the label's own parenthesis and nothing else.</b> POS is not built
/// here (architecture-spec.md §6 defers it), which leaves exactly Invoice, SalesOrder and CreditNote.
/// <see cref="DocumentType.Quotation"/> is deliberately <b>absent</b>: the label does not name it, and
/// the confirm-live tenant runs in AllTransactions mode, so its Quotation form showing a picker would
/// prove nothing about the narrow mode. Guessing it in would be exactly the over-broad list phase 30
/// warns against. Recorded as the phase's one unresolved live question in
/// docs/phase-32-status.md; adding it later is a one-line change plus a test.</para>
///
/// <para><b>Why this is a predicate and not a column set.</b> Every transactional aggregate carries a
/// nullable <c>LocationId</c> regardless of the setting (see <see cref="LocationScopeMode"/>), because
/// an Admin can widen the scope at any moment and the schema cannot lag the switch. This predicate is
/// what decides where the picker renders, where a location is required, and which lists show a
/// LOCATION column -- so all of those agree by construction instead of each re-deriving the rule.</para>
/// </summary>
public static class DocumentLocationScope
{
    // Both lists live in DocumentMechanisms, phase 27a's single source of truth for "which
    // cross-cutting mechanism applies to which document type", and are only *selected between*
    // here. Keeping a private copy would be the exact drift 27a exists to prevent -- and it is
    // DocumentMechanismSweepGuardTests, reading that file, that fails the build when a later phase
    // adds a DocumentType member without deciding whether it carries a location.
    private static readonly HashSet<DocumentType> SalesSide = [.. DocumentMechanisms.LocationBearingSalesOnly];

    private static readonly HashSet<DocumentType> Transactional = [.. DocumentMechanisms.LocationBearing];

    /// <summary>Does a document of this type carry a billing location under this tenant's setting?</summary>
    public static bool AppliesTo(DocumentType documentType, LocationScopeMode mode) =>
        mode == LocationScopeMode.AllTransactions
            ? Transactional.Contains(documentType)
            : SalesSide.Contains(documentType);

    /// <summary>Every type that carries a location under this tenant's setting, for the screens and
    /// tests that need the whole set rather than one answer.</summary>
    public static IReadOnlyCollection<DocumentType> For(LocationScopeMode mode) =>
        mode == LocationScopeMode.AllTransactions ? Transactional : SalesSide;

    /// <summary>Every type that carries a <c>LocationId</c> column, in any mode. This is what the
    /// schema is sized for and what phase 32's migration backfills -- never
    /// <see cref="For"/>, which is a runtime answer.</summary>
    public static IReadOnlyCollection<DocumentType> AllLocationBearingTypes => Transactional;
}
