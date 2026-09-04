using ErpApp.Application.Common.Currencies;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using MediatR;

namespace ErpApp.Application.Accounting.Commands.CreateCashTransfer;

public sealed record CreateCashTransferCommand(
    Guid OrganizationId, DateOnly Date, string? Reference, Guid FromAccountId, IReadOnlyList<CashTransferLineInput> Lines)
    : IRequest<CreateCashTransferResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitive, IAuditableRequest, ICurrencyBearingCommand
{
    public string PermissionKey => PermissionKeys.CashTransferCreate;

    /// <summary>Phase 28 (FR-2.5). Null means the base currency at rate 1 -- see
    /// <see cref="ICurrencyBearingCommand"/>.</summary>
    public string? CurrencyCode { get; init; }

    /// <inheritdoc cref="CurrencyCode"/>
    public decimal? ExchangeRate { get; init; }
    public DocumentType AuditDocumentType => DocumentType.CashTransfer;
}

public sealed record CreateCashTransferResult(Guid Id, string Code, CashTransferStatus Status);
