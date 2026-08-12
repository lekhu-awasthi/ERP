using MediatR;

namespace ErpApp.Application.Payments.Queries.GetDefaultPaymentAllocations;

/// <summary>
/// architecture-spec.md §4.6's "Default allocation is FIFO-ordered (oldest outstanding target
/// first) but manually overridable" -- confirmed live via the scan's Allocate Payment screen.
/// Returns a suggestion only; the client still submits its own (possibly edited) Allocations[] on
/// Create/UpdatePayment. Only Invoice targets exist this phase.
/// </summary>
public sealed record GetDefaultPaymentAllocationsQuery(Guid OrganizationId, Guid ContactId, decimal Amount)
    : IRequest<IReadOnlyList<PaymentAllocationInput>>;
