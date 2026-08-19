using ErpApp.Application.Common.Security;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using MediatR;

namespace ErpApp.Application.Accounting.Commands.VoidJournalVoucher;

public sealed record VoidJournalVoucherCommand(Guid OrganizationId, Guid Id)
    : IRequest<VoidJournalVoucherResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitiveDocument
{
    public string PermissionKey => PermissionKeys.JournalVoucherVoid;
    public DocumentType LockDateDocumentType => DocumentType.JournalVoucher;
    public Guid LockDateDocumentId => Id;
}

public sealed record VoidJournalVoucherResult(Guid Id, string Code, JournalVoucherStatus Status, DateTimeOffset? VoidedAt);
