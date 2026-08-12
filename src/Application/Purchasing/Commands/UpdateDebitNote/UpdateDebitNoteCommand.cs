using ErpApp.Application.Common.Security;
using ErpApp.Domain.Purchasing;
using MediatR;

namespace ErpApp.Application.Purchasing.Commands.UpdateDebitNote;

public sealed record UpdateDebitNoteCommand(
    Guid OrganizationId, Guid Id, Guid ContactId, DateOnly Date, string? Reference,
    IReadOnlyList<DebitNoteLineInput> Lines)
    : IRequest<UpdateDebitNoteResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.DebitNoteEdit;
}

public sealed record UpdateDebitNoteResult(Guid Id, string Code, DebitNoteStatus Status);
