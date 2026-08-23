using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Crm.Commands.AdjustSmsCredit;

/// <summary>
/// Manual credit seed/correction (docs/phase-18-status.md decision #6) -- the same "settable
/// starting number, not a payment flow" shape as Phase 17's Opening Balances. ChangeAmount can be
/// negative (a correction downward), unlike Opening Balance's always-positive framing, since a
/// credit ledger's whole point is a running signed total.
/// </summary>
public sealed record AdjustSmsCreditCommand(Guid OrganizationId, int ChangeAmount, string? Reason)
    : IRequest<SmsCreditAdjustmentResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.SmsCreditLedgerAdjust;
}

public sealed record SmsCreditAdjustmentResult(Guid Id, int ChangeAmount, int NewBalance);
