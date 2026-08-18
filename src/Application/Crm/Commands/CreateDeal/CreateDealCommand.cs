using ErpApp.Application.Common.Security;
using ErpApp.Domain.Crm;
using MediatR;

namespace ErpApp.Application.Crm.Commands.CreateDeal;

public sealed record CreateDealCommand(
    Guid OrganizationId,
    Guid ContactId,
    string Title,
    IReadOnlyList<Guid> AssigneeUserIds,
    Guid? LeadSourceId,
    string? Description,
    decimal ExpectedRevenue,
    DateOnly? ExpectedClosingDate,
    bool IsPrivate)
    : IRequest<CreateDealResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.DealManage;
}

public sealed record CreateDealResult(Guid Id, string Title, DealStatus Status, DateTimeOffset CreatedAt);
