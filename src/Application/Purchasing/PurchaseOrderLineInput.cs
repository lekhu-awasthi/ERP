using ErpApp.Domain.Catalog;

namespace ErpApp.Application.Purchasing;

public sealed record PurchaseOrderLineInput(Guid ProductId, decimal Quantity, decimal Rate, VatRate VatRate);
