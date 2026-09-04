using ErpApp.Application.Common.Currencies;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using MediatR;

namespace ErpApp.Application.Accounting.Commands.UpdateCashTransfer;

public sealed record UpdateCashTransferCommand(
    Guid OrganizationId, Guid Id, DateOnly Date, string? Reference, Guid FromAccountId, IReadOnlyList<CashTransferLineInput> Lines)
    : IRequest<UpdateCashTransferResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitive, IAuditableRequestWithId, ICurrencyBearingCommand
{
    public string PermissionKey => PermissionKeys.CashTransferEdit;

    /// <summary>Phase 28 (FR-2.5). Null means the base currency at rate 1 -- see
    /// <see cref="ICurrencyBearingCommand"/>.</summary>
    public string? CurrencyCode { get; init; }

    /// <inheritdoc cref="CurrencyCode"/>
    public decimal? ExchangeRate { get; init; }
    public DocumentType AuditDocumentType => DocumentType.CashTransfer;
    public Guid AuditDocumentId => Id;
}

public sealed record UpdateCashTransferResult(Guid Id, string Code, CashTransferStatus Status);
