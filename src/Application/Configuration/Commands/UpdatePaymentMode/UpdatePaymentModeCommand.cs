using ErpApp.Application.Common.Security;
using ErpApp.Domain.Configuration;
using MediatR;

namespace ErpApp.Application.Configuration.Commands.UpdatePaymentMode;

/// <param name="Kind">Phase 60. See CreatePaymentModeCommand.</param>
/// <param name="AccountId">Phase 60. Refused as null (409) while the mode is linked to a POS
/// location: a till tab whose tender cannot post is worse than no tab.</param>
public sealed record UpdatePaymentModeCommand(
    Guid OrganizationId,
    Guid Id,
    string Name,
    bool IsActive,
    bool RequiresChequeDetails,
    PaymentModeKind Kind = PaymentModeKind.Other,
    Guid? AccountId = null)
    : IRequest<UpdatePaymentModeResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.PaymentModeManage;
}

public sealed record UpdatePaymentModeResult(
    Guid Id, string Name, bool IsActive, bool RequiresChequeDetails, PaymentModeKind Kind, Guid? AccountId);
