using ErpApp.Domain.Catalog;

namespace ErpApp.Application.Sales;

public sealed record CreditNoteLineInput(Guid ProductId, decimal Quantity, decimal Rate, VatRate VatRate);
