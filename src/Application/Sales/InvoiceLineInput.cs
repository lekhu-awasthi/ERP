using ErpApp.Domain.Catalog;

namespace ErpApp.Application.Sales;

public sealed record InvoiceLineInput(Guid ProductId, decimal Quantity, decimal Rate, VatRate VatRate, decimal DiscountPct = 0);
