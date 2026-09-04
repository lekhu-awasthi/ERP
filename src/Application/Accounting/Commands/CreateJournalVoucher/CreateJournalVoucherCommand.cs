using ErpApp.Application.Common.Currencies;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using MediatR;

namespace ErpApp.Application.Accounting.Commands.CreateJournalVoucher;

public sealed record CreateJournalVoucherCommand(
    Guid OrganizationId, DateOnly Date, string? Reference, IReadOnlyList<JournalVoucherLineInput> Lines)
    : IRequest<CreateJournalVoucherResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitive, IAuditableRequest, ICurrencyBearingCommand
{
    public string PermissionKey => PermissionKeys.JournalVoucherCreate;

    /// <summary>Phase 28 (FR-2.5). Null means the base currency at rate 1 -- see
    /// <see cref="ICurrencyBearingCommand"/>.</summary>
    public string? CurrencyCode { get; init; }

    /// <inheritdoc cref="CurrencyCode"/>
    public decimal? ExchangeRate { get; init; }
    public DocumentType AuditDocumentType => DocumentType.JournalVoucher;
}

public sealed record CreateJournalVoucherResult(Guid Id, string Code, JournalVoucherStatus Status);
