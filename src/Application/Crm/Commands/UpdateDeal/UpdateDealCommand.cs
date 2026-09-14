using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
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
    : IRequest<UpdateDealResult>, IRequirePermission, IOrganizationScoped, IAuditableRequestWithId
{
    public string PermissionKey => PermissionKeys.DealManage;

    // Phase 43 (39 carried item #1) -- the Activity tab on the new detail page is the audit feed,
    // and AuditBehavior only writes a row for a request that declares itself auditable. Without
    // this the tab would render, work, and be permanently empty.
    public DocumentType AuditDocumentType => DocumentType.Deal;
    public Guid AuditDocumentId => Id;
}

public sealed record UpdateDealResult(Guid Id, string Title, DealStatus Status);
