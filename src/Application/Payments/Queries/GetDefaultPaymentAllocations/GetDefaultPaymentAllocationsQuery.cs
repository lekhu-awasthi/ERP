using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Payments.Allocation;
using ErpApp.Domain.Payments;
using MediatR;

namespace ErpApp.Application.Payments.Queries.GetDefaultPaymentAllocations;

/// <summary>
/// architecture-spec.md §4.6's "Default allocation is FIFO-ordered (oldest outstanding target
/// first) but manually overridable" -- confirmed live via the scan's Allocate Payment screen.
/// Returns a suggestion only; the client still submits its own (possibly edited) Allocations[] on
/// Create/UpdatePayment. Direction picks the target document type: Received suggests against the
/// Contact's outstanding Approved Invoices, Paid against its outstanding Approved PurchaseBills
/// (confirmed live: "Allocate Supplier Payments... Type column includes more variety").
///
/// <para><b>Phase 88 -- it had no membership check at all.</b> From phase 5 to phase 87 this request was neither
/// <see cref="IOrganizationScoped"/> nor <see cref="IRequirePermission"/>, so <c>AuthorizationBehavior</c> never ran
/// for it and any signed-in user could read another organization's documents and balances by naming its ids. It now
/// carries the same key and location narrowing as the list it suggests from (<see cref="OpenAllocationTargets"/>), and
/// <c>OrganizationScopeSweepGuardTests</c> fails the build for the next request that names an organization without
/// being scoped to it.</para>
/// </summary>
/// <param name="CurrencyCode">Phase 88 -- the payment's currency. A payment only settles documents in its own (phase
/// 28 Decision F), so suggesting the others was suggesting what Approve refuses.</param>
public sealed record GetDefaultPaymentAllocationsQuery(
    Guid OrganizationId, Guid ContactId, decimal Amount, PaymentDirection Direction, string CurrencyCode)
    : IRequest<IReadOnlyList<PaymentAllocationInput>>, IRequirePermission, IOrganizationScoped, ILocationFilteredQuery
{
    public string PermissionKey => OpenAllocationTargets.ViewKeyFor(Direction);
}
