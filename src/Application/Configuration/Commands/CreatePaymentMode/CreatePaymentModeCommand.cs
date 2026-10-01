using ErpApp.Application.Common.Security;
using ErpApp.Domain.Configuration;
using MediatR;

namespace ErpApp.Application.Configuration.Commands.CreatePaymentMode;

/// <param name="Kind">Phase 60 -- the vendor's Cash | Card | E-Payment | Other. Defaults to Other,
/// which is what every mode created before the phase was backfilled to.</param>
/// <param name="AccountId">Phase 60 -- the cash or bank account a till tender in this mode posts to.
/// Optional; required only once the mode is linked to a POS location.</param>
public sealed record CreatePaymentModeCommand(
    Guid OrganizationId,
    string Name,
    bool RequiresChequeDetails = false,
    PaymentModeKind Kind = PaymentModeKind.Other,
    Guid? AccountId = null)
    : IRequest<CreatePaymentModeResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.PaymentModeManage;
}

public sealed record CreatePaymentModeResult(
    Guid Id, string Name, bool RequiresChequeDetails, PaymentModeKind Kind, Guid? AccountId);
