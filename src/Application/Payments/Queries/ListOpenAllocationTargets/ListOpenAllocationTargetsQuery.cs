using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Payments.Allocation;
using ErpApp.Domain.Payments;
using MediatR;

namespace ErpApp.Application.Payments.Queries.ListOpenAllocationTargets;

/// <summary>
/// Phase 88 -- the documents a payment from <paramref name="ContactId"/> in <paramref name="CurrencyCode"/> may still
/// be allocated to (see <see cref="OpenAllocationTargets"/>). Backs the allocation picker on the Customer and Supplier
/// Payment forms and on both Allocate screens.
///
/// <para><b>Permission.</b> The view key of the target type -- <c>Sales.Invoice.View</c> for a receipt,
/// <c>Purchasing.PurchaseBill.View</c> for a supplier payment -- because those are the lists the picker was built from
/// until now: the same people see the same documents, now only the open ones. A list, so it narrows to the caller's
/// locations rather than refusing (<see cref="ILocationFilteredQuery"/>).</para>
/// </summary>
public sealed record ListOpenAllocationTargetsQuery(
    Guid OrganizationId, Guid ContactId, PaymentDirection Direction, string CurrencyCode)
    : IRequest<IReadOnlyList<OpenAllocationTargetDto>>, IRequirePermission, IOrganizationScoped, ILocationFilteredQuery
{
    public string PermissionKey => OpenAllocationTargets.ViewKeyFor(Direction);
}
