using ErpApp.Domain.Catalog;

namespace ErpApp.Application.Sales;

/// <summary>Shared line-input shape for Create/UpdateQuotation -- the client always resubmits its
/// whole current multi-line-editable-table state, same JournalVoucherLineInput precedent.</summary>
public sealed record QuotationLineInput(Guid ProductId, decimal Quantity, decimal Rate, VatRate VatRate, decimal DiscountPct = 0);
