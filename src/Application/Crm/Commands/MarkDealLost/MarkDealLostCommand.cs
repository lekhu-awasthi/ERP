using ErpApp.Application.Common.Security;
using ErpApp.Domain.Crm;
using MediatR;

namespace ErpApp.Application.Crm.Commands.MarkDealLost;

public sealed record MarkDealLostCommand(Guid OrganizationId, Guid Id)
    : IRequest<MarkDealLostResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.DealManage;
}

public sealed record MarkDealLostResult(Guid Id, DealStatus Status, DateOnly? ClosingDate);
