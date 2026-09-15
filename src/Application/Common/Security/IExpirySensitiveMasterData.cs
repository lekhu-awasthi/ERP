namespace ErpApp.Application.Common.Security;

/// <summary>
/// Phase 46 -- marks a master-data write that stops working once the tenant's subscription term has
/// ended. Phase 31 carried item #6, and it is a narrowing of that item rather than the whole of it.
///
/// <para><b>What phase 31 decided, and why this does not simply reverse it.</b>
/// <c>SubscriptionExpiryBehavior</c> has gated exactly <c>ILockDateSensitive</c> /
/// <c>ILockDateSensitiveDocument</c> since phase 31 -- "a create, update, approve or void of a
/// transactional document", which is what "the books" means. Configuration writes were excluded
/// deliberately, with a good reason: an expired tenant should not also be locked out of fixing a
/// wrong account before it renews. That reason is still good, and settings edits, every query and
/// <c>SetTenantSubscriptionCommand</c> all still work past expiry.</para>
///
/// <para><b>What phase 31 did not notice is that the hole is wider than the reason covers.</b>
/// Products, contacts, accounts, warehouses and billing locations are not documents, so none of them
/// implements either lock-date marker, so an expired tenant could build an entire chart of accounts
/// and product catalogue -- indefinitely, for free. "Fixing a wrong account" and "setting up a new
/// business" were the same permission, and only the first was ever argued for.</para>
///
/// <para><b>A third marker, not a merge</b> -- phase 32b's rule, for the third time. The lock-date
/// set is about the books and is the right set for a lock date; this set is about master data and is
/// the right set for an ended term. Merging them would freeze master data at a lock date, which is
/// nonsense: a lock date closes a period, and a product is not in a period.</para>
///
/// <para><b>Still derived, not observed.</b> No expired tenant has ever been visible on either
/// reference tenant -- Cadehi's trial had 7 days left when this was read on 2026-09-15. The vendor's
/// Terms price read-only access at 25% of the subscription fee, which says the natural end state is
/// read-only and that it is something they sell, but it says nothing about which writes stop. So
/// this remains an inference, recorded as one in docs/phase-46-status.md.</para>
/// </summary>
public interface IExpirySensitiveMasterData;
