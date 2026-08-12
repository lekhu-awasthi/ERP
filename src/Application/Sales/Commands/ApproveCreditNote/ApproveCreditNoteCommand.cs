using ErpApp.Application.Common.Security;
using ErpApp.Domain.Sales;
using MediatR;

namespace ErpApp.Application.Sales.Commands.ApproveCreditNote;

public sealed record ApproveCreditNoteCommand(Guid OrganizationId, Guid Id)
    : IRequest<ApproveCreditNoteResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.CreditNoteApprove;
}

public sealed record ApproveCreditNoteResult(Guid Id, string Code, CreditNoteStatus Status, DateTimeOffset? ApprovedAt);
