using ErpApp.Application.Common.Security;
using ErpApp.Domain.Crm;
using MediatR;

namespace ErpApp.Application.Crm.Commands.MarkDealWon;

public sealed record MarkDealWonCommand(Guid OrganizationId, Guid Id)
    : IRequest<MarkDealWonResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.DealManage;
}

public sealed record MarkDealWonResult(Guid Id, DealStatus Status, DateOnly? ClosingDate);
