using ErpApp.Application.Common.Security;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using MediatR;

namespace ErpApp.Application.Accounting.Commands.CreateJournalVoucher;

public sealed record CreateJournalVoucherCommand(
    Guid OrganizationId, DateOnly Date, string? Reference, IReadOnlyList<JournalVoucherLineInput> Lines)
    : IRequest<CreateJournalVoucherResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitive, IAuditableRequest
{
    public string PermissionKey => PermissionKeys.JournalVoucherCreate;
    public DocumentType AuditDocumentType => DocumentType.JournalVoucher;
}

public sealed record CreateJournalVoucherResult(Guid Id, string Code, JournalVoucherStatus Status);
