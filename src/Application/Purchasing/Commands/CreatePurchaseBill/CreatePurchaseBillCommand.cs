using ErpApp.Application.Common.Currencies;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Purchasing;
using MediatR;

namespace ErpApp.Application.Purchasing.Commands.CreatePurchaseBill;

public sealed record CreatePurchaseBillCommand(
    Guid OrganizationId,
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
    DocumentType? ReferrerType = null,
    Guid? ReferrerId = null,
    decimal DiscountPct = 0)
    : IRequest<CreatePurchaseBillResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitive, IAuditableRequest, ICurrencyBearingCommand
{
    public string PermissionKey => PermissionKeys.PurchaseBillCreate;

    /// <summary>Phase 28 (FR-2.5). Null means the base currency at rate 1 -- see
    /// <see cref="ICurrencyBearingCommand"/>.</summary>
    public string? CurrencyCode { get; init; }

    /// <inheritdoc cref="CurrencyCode"/>
    public decimal? ExchangeRate { get; init; }

    /// <summary>Phase 29 (FR-6.15) -- the Additional Cost section's rows. Init-only rather than a
    /// trailing positional parameter, the same shape phase 28's currency pair took, so no existing
    /// caller's argument list changes.</summary>
    public IReadOnlyList<PurchaseBillAdditionalCostInput>? AdditionalCosts { get; init; }

    /// <inheritdoc cref="Domain.Purchasing.PurchaseBill.IsProductWiseAdditionalCost"/>
    public bool IsProductWiseAdditionalCost { get; init; }

    public DocumentType AuditDocumentType => DocumentType.PurchaseBill;
}

public sealed record CreatePurchaseBillResult(Guid Id, string Code, PurchaseBillStatus Status);
