using ErpApp.Application.Common.Security;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using MediatR;

namespace ErpApp.Application.Accounting.Commands.ApproveJournalVoucher;

public sealed record ApproveJournalVoucherCommand(Guid OrganizationId, Guid Id)
    : IRequest<ApproveJournalVoucherResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitiveDocument
{
    public string PermissionKey => PermissionKeys.JournalVoucherApprove;
    public DocumentType LockDateDocumentType => DocumentType.JournalVoucher;
    public Guid LockDateDocumentId => Id;
}

public sealed record ApproveJournalVoucherResult(Guid Id, string Code, JournalVoucherStatus Status, DateTimeOffset? ApprovedAt);
