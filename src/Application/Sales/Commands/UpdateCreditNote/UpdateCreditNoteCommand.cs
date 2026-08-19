using ErpApp.Application.Common.Security;
using ErpApp.Domain.Sales;
using MediatR;

namespace ErpApp.Application.Sales.Commands.UpdateCreditNote;

public sealed record UpdateCreditNoteCommand(
    Guid OrganizationId, Guid Id, Guid ContactId, DateOnly Date, string? Reference, IReadOnlyList<CreditNoteLineInput> Lines)
    : IRequest<UpdateCreditNoteResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitive
{
    public string PermissionKey => PermissionKeys.CreditNoteEdit;
}

public sealed record UpdateCreditNoteResult(Guid Id, string Code, CreditNoteStatus Status);
