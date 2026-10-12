using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Payments.Allocation;
using MediatR;

namespace ErpApp.Application.Payments.Queries.GetDefaultPaymentAllocations;

/// <summary>
/// FIFO-oldest-due-first: the amount fills the contact's open documents in the order the picker lists them, each up
/// to what is left of it.
///
/// <para><b>Phase 88 -- one reader.</b> This handler used to net each document itself (phase 11's fix of phase 9's
/// gap), and drifted the way phase 36 found the ageing reports had: it counted payment-sourced allocations only, so
/// an invoice settled by a journal voucher was still suggested, and it ignored the payment's currency. It now fills
/// exactly the rows <see cref="OpenAllocationTargets"/> lists, which are the rows the ageing reports call outstanding,
/// in the reference product's order (due date first, confirmed live on 2026-10-12, where this handler ordered by
/// document date).</para>
/// </summary>
public sealed class GetDefaultPaymentAllocationsQueryHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<GetDefaultPaymentAllocationsQuery, IReadOnlyList<PaymentAllocationInput>>
{
    public async Task<IReadOnlyList<PaymentAllocationInput>> Handle(
        GetDefaultPaymentAllocationsQuery request, CancellationToken cancellationToken)
    {
        var targets = await OpenAllocationTargets.LoadAsync(
            db, currentUser, request.OrganizationId, request.ContactId, request.Direction, request.CurrencyCode,
            cancellationToken);

        var suggestions = new List<PaymentAllocationInput>();
        var remaining = request.Amount;

        foreach (var target in targets)
        {
            if (remaining <= 0)
            {
                break;
            }

            var take = Math.Min(remaining, target.Balance);
            suggestions.Add(new PaymentAllocationInput(target.TargetDocumentType, target.Id, take));
            remaining -= take;
        }

        return suggestions;
    }
}
