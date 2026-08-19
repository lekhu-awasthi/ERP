using ErpApp.Domain.Catalog;
using ErpApp.Domain.Purchasing;

namespace ErpApp.Application.Purchasing;

public sealed record PurchaseBillLineInput(
    Guid ProductId, decimal Quantity, decimal Rate, VatRate VatRate, ExpenditureClassification ExpenditureClassification,
    decimal DiscountPct = 0);
