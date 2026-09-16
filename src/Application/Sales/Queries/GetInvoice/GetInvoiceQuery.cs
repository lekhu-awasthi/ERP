using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using MediatR;

namespace ErpApp.Application.Sales.Queries.GetInvoice;

public sealed record GetInvoiceQuery(Guid OrganizationId, Guid Id)
    : IRequest<InvoiceDetailDto>, IRequirePermission, IOrganizationScoped, ILocationScopedDocument
{
    public string PermissionKey => PermissionKeys.InvoiceView;

    /// <summary>Phase 32b -- a location-scoped caller must hold the key at this document location.</summary>
    public Guid LocationDocumentId => Id;
}

/// <param name="BatchNo">Phase 51 -- the batch this line names, by number, or null. Read back as a
/// number rather than an id because that is what the form's one Item Batch control holds and what
/// the client would have to resolve otherwise. Phase 35a's rule is the reason this is here at all:
/// a detail DTO that drops a field the write path stored means the form can never show it, and that
/// phase found 14 of 15 detail reads doing exactly that.</param>
/// <param name="ManufactureDate">Phase 51 -- the named batch's own dates, so the form can show them
/// beside the number without a second round trip. Null when the line names no batch.</param>
/// <param name="ExpiryDate">See <paramref name="ManufactureDate"/>.</param>
/// <param name="SerialNumbers">Phase 51 -- the serial numbers this line names, one per physical
/// unit. Empty for every line of every product that is not serial-tracked.</param>
public sealed record InvoiceLineDto(
    Guid Id, Guid ProductId, decimal Quantity, decimal Rate, VatRate VatRate, decimal DiscountPct, decimal Amount,
    decimal VatAmount,
    string? BatchNo,
    DateOnly? ManufactureDate,
    DateOnly? ExpiryDate,
    IReadOnlyList<string> SerialNumbers);

public sealed record PostedGlLineDto(Guid Id, Guid AccountId, decimal Debit, decimal Credit);

public sealed record InvoiceDetailDto(
    Guid Id,
    Guid OrganizationId,
    Guid ContactId,
    Guid WarehouseId,
    // Phase 32 -- the billing location this invoice was raised from, null when the tenant's
    // LocationScopeMode excludes Invoice. Unlike the list (which returns the aggregate itself, so the
    // field came for free), this detail query projects an explicit DTO -- so omitting it here would
    // leave the header picker unable to show the stored value on an existing invoice while the write
    // path worked perfectly. Caught by the phase-32 E2E, not by any test.
    Guid? LocationId,
    string Code,
    DateOnly Date,
    // Phase 31 -- the stored Due Date, so the detail page and its form can round-trip it.
    DateOnly DueDate,
    string? Reference,
    bool IsExport,
    string? ExportCountry,
    string? ExportDeclarationNo,
    DateOnly? ExportDeclarationDate,
    InvoiceStatus Status,
    Guid? ApprovedByUserId,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset CreatedAt,
    DocumentType? ReferrerType,
    Guid? ReferrerId,
    decimal DiscountPct,
    decimal GrandTotal,
    string? Terms,
    IReadOnlyList<InvoiceLineDto> Lines,
    IReadOnlyList<PostedGlLineDto>? GlLines,
    // Phase 28 (FR-2.5) -- the document's own currency and its rate to the base currency.
    // Every amount above is denominated in CurrencyCode; the general ledger figures under
    // GlLines are in the base currency, already converted at ExchangeRate.
    string CurrencyCode,
    decimal ExchangeRate);
