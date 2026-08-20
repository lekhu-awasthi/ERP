using ErpApp.Application.Common.Security;
using ErpApp.Domain.Payments;
using MediatR;

namespace ErpApp.Application.Payments.Commands.TransitionChequeStatus;

/// <summary>Phase 17 (docs/phase-17-status.md decisions #4/#5) -- moves a Cheque along its status
/// lifecycle (Cheque.TransitionStatus enforces the allowed-transition table). No GL side effect on
/// any transition, including Bounced -- decision #4's safe default. No ILockDateSensitive*: a
/// Cheque carries no user-editable transaction Date of its own that a lock date would guard.
/// </summary>
public sealed record TransitionChequeStatusCommand(Guid OrganizationId, Guid Id, ChequeStatus NewStatus)
    : IRequest<TransitionChequeStatusResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.ChequeManage;
}

public sealed record TransitionChequeStatusResult(Guid Id, ChequeStatus Status);
