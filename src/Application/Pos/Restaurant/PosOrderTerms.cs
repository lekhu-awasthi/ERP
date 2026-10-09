using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Pos;

namespace ErpApp.Application.Pos.Restaurant;

/// <summary>
/// Phase 72 -- what changing an order's terms (its discount, a line's discount, a waived service charge) checks
/// before the order may change.
///
/// <para><b>The order loads for an action at its own till</b>, as every order request does
/// (<see cref="PosRestaurant.LoadOrderForActionAsync"/>: <c>Sales.Invoice.Create</c> there).</para>
///
/// <para><b>And the caller may approve invoices there: <c>Sales.Invoice.Approve</c>.</b> A discount or a waived
/// charge lowers what the table pays, and a bill is approved the moment it is paid, so the review an ERP invoice
/// gets at Approve has to happen when its terms are set instead. A drawer count cannot catch a discount: the cash
/// matches the lowered bill. This is phase 61 Decision F's rule for credit applied to a price, and it is the
/// vendor's per-location <i>Can Give Discount</i> / <i>Can Remove Service Charge</i> switches in this codebase's
/// own keys (read live on 2026-10-09). Re-checked here, not in the pipeline, because the location is the loaded
/// order's: a missing order is the handler's 404, a real one outside the caller's grant a 403 naming the key
/// (phase 31's both-directions proof).</para>
/// </summary>
internal static class PosOrderTerms
{
    public static async Task<(PosOrder Order, IReadOnlyDictionary<Guid, decimal> Invoiced)> LoadForChangeAsync(
        IAppDbContext db, Guid organizationId, Guid userId, Guid orderId, CancellationToken cancellationToken)
    {
        var (order, _) = await PosRestaurant.LoadOrderForActionAsync(db, organizationId, userId, orderId, cancellationToken);

        await GrantedPermissionReader.EnsureGrantedAtLocationAsync(
            db, organizationId, userId, PermissionKeys.InvoiceApprove, order.BillingLocationId, cancellationToken);

        // What is billed fixes the terms: the Domain refuses a change once a part was priced with them.
        var invoiced = (await PosOrderBilling.LoadAsync(db, organizationId, order.Id, cancellationToken)).Invoiced;

        return (order, invoiced);
    }
}
