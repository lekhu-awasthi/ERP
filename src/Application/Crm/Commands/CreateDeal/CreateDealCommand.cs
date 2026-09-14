using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
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
    : IRequest<CreateDealResult>, IRequirePermission, IOrganizationScoped, IAuditableRequest
{
    public string PermissionKey => PermissionKeys.DealManage;

    // Phase 43 (39 carried item #1) -- the Activity tab on the new detail page is the audit feed,
    // and AuditBehavior only writes a row for a request that declares itself auditable. Without
    // this the tab would render, work, and be permanently empty.
    public DocumentType AuditDocumentType => DocumentType.Deal;
}

public sealed record CreateDealResult(Guid Id, string Title, DealStatus Status, DateTimeOffset CreatedAt);
