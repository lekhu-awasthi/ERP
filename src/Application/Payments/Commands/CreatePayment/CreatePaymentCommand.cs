using ErpApp.Application.Common.Security;
using ErpApp.Domain.Payments;
using MediatR;

namespace ErpApp.Application.Payments.Commands.CreatePayment;

/// <summary>Always Direction=Received (Customer Payment) this phase -- Supplier Payment
/// (Direction=Paid) is Phase 6's reuse, not exposed here yet.</summary>
public sealed record CreatePaymentCommand(
    Guid OrganizationId, Guid ContactId, DateOnly Date, Guid? PaymentModeId, Guid AccountId, decimal Amount,
    string? Reference, IReadOnlyList<PaymentAllocationInput> Allocations)
    : IRequest<CreatePaymentResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.PaymentCreate;
}

public sealed record CreatePaymentResult(Guid Id, string Code, PaymentStatus Status);
