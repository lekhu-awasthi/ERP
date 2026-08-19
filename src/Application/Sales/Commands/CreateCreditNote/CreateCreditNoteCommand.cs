using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using MediatR;

namespace ErpApp.Application.Sales.Commands.CreateCreditNote;

public sealed record CreateCreditNoteCommand(
    Guid OrganizationId,
    Guid ContactId,
    DateOnly Date,
    string? Reference,
    IReadOnlyList<CreditNoteLineInput> Lines,
    DocumentType? ReferrerType = null,
    Guid? ReferrerId = null)
    : IRequest<CreateCreditNoteResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitive
{
    public string PermissionKey => PermissionKeys.CreditNoteCreate;
}

public sealed record CreateCreditNoteResult(Guid Id, string Code, CreditNoteStatus Status);
