using ErpApp.Domain.Common;

namespace ErpApp.Application.Common.Security;

/// <summary>
/// Phase 41 -- declares that this command consumes one unit of the tenant's
/// <b>transactions-per-year</b> allowance, so <c>SubscriptionQuotaBehavior</c> refuses it once the
/// allowance is spent.
///
/// <para><b>Marked on the Approve command, not on Create.</b> The vendor's own Terms define the
/// metered unit as a transaction "in which accounting entry are affected", and in this codebase that
/// is precisely Approve: a document number is assigned and a <c>GlJournalEntry</c> is posted there,
/// never at Create. Metering drafts would also make the ceiling unusable in the one way that matters
/// commercially -- a tenant would burn allowance on documents it abandoned.</para>
///
/// <para><b>Why a third marker rather than reusing the lock-date pair.</b> Phase 31's
/// <c>SubscriptionExpiryBehavior</c> reuses <c>ILockDateSensitiveDocument</c> because the set it
/// wanted -- create, update, approve or void of a transactional document -- happened to be exactly
/// that set. This set is not: it excludes every Create and Update, excludes Void (voiding a document
/// must not cost a second unit, and must stay possible at the ceiling or a tenant at its limit could
/// never correct a mistake), and excludes the four types that approve without posting. Phase 32b's
/// rule applies -- reuse a marker by reading it, not by merging it, when the member sets differ.</para>
///
/// <para>Kept honest in both directions by <c>MeteredTransactionSweepGuardTests</c> against
/// <see cref="DocumentMechanisms.MeteredTransactions"/>.</para>
/// </summary>
public interface IMeteredTransaction
{
    /// <summary>The type whose allowance this command consumes. Stated by the command rather than
    /// inferred from its name so the guard test compares two independently-written things.</summary>
    DocumentType MeteredDocumentType { get; }
}

/// <summary>
/// Phase 41 -- declares that this command creates a product, and so is refused once the tenant's
/// <b>product</b> ceiling is reached (Basic 1,000 service items, Standard 5,000, Professional
/// 10,000, plus Rs 1,000 per additional 1,000 on the published price list).
///
/// <para><b>One implementer, deliberately.</b> <c>CreateProductCommand</c> is the only way a product
/// comes into existence -- bulk import routes through the same command under the initiating user's
/// identity (phase 21a), so the importer is metered for free and cannot become a way around the
/// ceiling. An Update is not metered: editing a product creates nothing.</para>
///
/// <para><b>Variants count.</b> Phase 24 models a variant as a Product with a parent pointer, and the
/// price list sells "products", each of which carries its own SKU and its own price. Excluding
/// variants would let a tenant multiply its real catalogue without touching the ceiling.</para>
/// </summary>
public interface IMeteredProduct;
