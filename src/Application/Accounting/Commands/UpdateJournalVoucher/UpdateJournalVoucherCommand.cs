using ErpApp.Application.Common.Currencies;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using MediatR;

namespace ErpApp.Application.Accounting.Commands.UpdateJournalVoucher;

public sealed record UpdateJournalVoucherCommand(
    Guid OrganizationId, Guid Id, DateOnly Date, string? Reference, IReadOnlyList<JournalVoucherLineInput> Lines)
    : IRequest<UpdateJournalVoucherResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitive, IAuditableRequestWithId, ICurrencyBearingCommand
{
    public string PermissionKey => PermissionKeys.JournalVoucherEdit;

    /// <summary>Phase 28 (FR-2.5). Null means the base currency at rate 1 -- see
    /// <see cref="ICurrencyBearingCommand"/>.</summary>
    public string? CurrencyCode { get; init; }

    /// <inheritdoc cref="CurrencyCode"/>
    public decimal? ExchangeRate { get; init; }
    public DocumentType AuditDocumentType => DocumentType.JournalVoucher;
    public Guid AuditDocumentId => Id;
}

public sealed record UpdateJournalVoucherResult(Guid Id, string Code, JournalVoucherStatus Status);
