using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Purchasing;
using MediatR;

namespace ErpApp.Application.Purchasing.Commands.CreateDebitNote;

public sealed record CreateDebitNoteCommand(
    Guid OrganizationId,
    Guid ContactId,
    DateOnly Date,
    string? Reference,
    Guid? TdsTypeId,
    IReadOnlyList<DebitNoteLineInput> Lines,
    DocumentType? ReferrerType = null,
    Guid? ReferrerId = null,
    decimal DiscountPct = 0)
    : IRequest<CreateDebitNoteResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitive
{
    public string PermissionKey => PermissionKeys.DebitNoteCreate;
}

public sealed record CreateDebitNoteResult(Guid Id, string Code, DebitNoteStatus Status);
