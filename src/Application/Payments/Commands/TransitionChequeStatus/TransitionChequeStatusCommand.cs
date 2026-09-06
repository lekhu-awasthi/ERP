using ErpApp.Application.Common.Security;
using ErpApp.Domain.Payments;
using MediatR;

namespace ErpApp.Application.Payments.Commands.TransitionChequeStatus;

/// <summary>Phase 17 (docs/phase-17-status.md decisions #4/#5) -- moves a Cheque along its status
/// lifecycle (Cheque.TransitionStatus enforces the allowed-transition table). No ILockDateSensitive*:
/// a Cheque carries no user-editable transaction Date of its own that a lock date would guard.
///
/// <para><b>Phase 31 gave Bounced a GL side effect</b>, replacing decision #4's "no automatic
/// reversal" placeholder: it voids the linked Payment and posts the mirroring entry. Every other
/// transition still has none. The permission key here is the one the pipeline checks; bouncing an
/// approved payment additionally requires <c>PaymentVoid</c>, re-checked inside the handler because
/// it depends on rows the pipeline has not read -- see the handler's doc comment.</para>
/// </summary>
public sealed record TransitionChequeStatusCommand(Guid OrganizationId, Guid Id, ChequeStatus NewStatus)
    : IRequest<TransitionChequeStatusResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.ChequeManage;
}

/// <param name="VoidedPaymentCode">Phase 31 -- the code of the Payment this transition voided, or
/// null when it voided none. Returned rather than left silent so the client can tell the user that
/// a bounce unwound a receipt, which is a consequence they would otherwise only find in the
/// ledger.</param>
public sealed record TransitionChequeStatusResult(Guid Id, ChequeStatus Status, string? VoidedPaymentCode = null);
