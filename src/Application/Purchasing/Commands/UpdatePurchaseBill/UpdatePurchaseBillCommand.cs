using ErpApp.Application.Common.Currencies;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Purchasing;
using MediatR;

namespace ErpApp.Application.Purchasing.Commands.UpdatePurchaseBill;

public sealed record UpdatePurchaseBillCommand(
    Guid OrganizationId,
    Guid Id,
    Guid ContactId,
    Guid WarehouseId,
    DateOnly Date,
    string? Reference,
    string? SupplierInvoiceReference,
    bool IsImport,
    string? ImportCountry,
    DateOnly? ImportDate,
    string? ImportDocumentNo,
    Guid? TdsTypeId,
    IReadOnlyList<PurchaseBillLineInput> Lines,
    decimal DiscountPct = 0)
    : IRequest<UpdatePurchaseBillResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitive, IAuditableRequestWithId, ICurrencyBearingCommand
{
    public string PermissionKey => PermissionKeys.PurchaseBillEdit;

    /// <summary>Phase 28 (FR-2.5). Null means the base currency at rate 1 -- see
    /// <see cref="ICurrencyBearingCommand"/>.</summary>
    public string? CurrencyCode { get; init; }

    /// <inheritdoc cref="CurrencyCode"/>
    public decimal? ExchangeRate { get; init; }
    public DocumentType AuditDocumentType => DocumentType.PurchaseBill;
    public Guid AuditDocumentId => Id;
}

public sealed record UpdatePurchaseBillResult(Guid Id, string Code, PurchaseBillStatus Status);
