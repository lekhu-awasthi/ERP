using ErpApp.Application.Common.Security;
using ErpApp.Domain.Crm;
using MediatR;

namespace ErpApp.Application.Crm.Commands.UpdateDeal;

public sealed record UpdateDealCommand(
    Guid OrganizationId,
    Guid Id,
    string Title,
    IReadOnlyList<Guid> AssigneeUserIds,
    Guid? LeadSourceId,
    string? Description,
    decimal ExpectedRevenue,
    DateOnly? ExpectedClosingDate,
    bool IsPrivate)
    : IRequest<UpdateDealResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.DealManage;
}

public sealed record UpdateDealResult(Guid Id, string Title, DealStatus Status);
