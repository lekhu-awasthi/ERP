using ErpApp.Application.Common.Security;
using ErpApp.Domain.Payments;
using MediatR;

namespace ErpApp.Application.Payments.Commands.CreatePayment;

/// <summary>
/// Direction is client-supplied (Customer Payment screens send Received, Supplier Payment screens
/// send Paid) rather than hardcoded -- safe because Payments.Payment.Create is a single permission
/// shared across both directions (see phase-6-status.md's scope decision on why the permission
/// matrix wasn't split Sales.Payment/Purchasing.Payment: the aggregate, lifecycle, and
/// maker-checker story are identical, only the GL direction and the Contact/allocation-target type
/// differ), so a client can't escalate privilege by choosing one direction over the other.
/// </summary>
public sealed record CreatePaymentCommand(
    Guid OrganizationId, Guid ContactId, PaymentDirection Direction, DateOnly Date, Guid? PaymentModeId, Guid AccountId,
    decimal Amount, string? Reference, IReadOnlyList<PaymentAllocationInput> Allocations)
    : IRequest<CreatePaymentResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitive
{
    public string PermissionKey => PermissionKeys.PaymentCreate;
}

public sealed record CreatePaymentResult(Guid Id, string Code, PaymentStatus Status);
