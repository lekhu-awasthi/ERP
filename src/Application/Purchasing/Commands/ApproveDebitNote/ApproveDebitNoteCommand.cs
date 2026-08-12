using ErpApp.Application.Common.Security;
using ErpApp.Domain.Purchasing;
using MediatR;

namespace ErpApp.Application.Purchasing.Commands.ApproveDebitNote;

public sealed record ApproveDebitNoteCommand(Guid OrganizationId, Guid Id)
    : IRequest<ApproveDebitNoteResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.DebitNoteApprove;
}

public sealed record ApproveDebitNoteResult(Guid Id, string Code, DebitNoteStatus Status, DateTimeOffset? ApprovedAt);
