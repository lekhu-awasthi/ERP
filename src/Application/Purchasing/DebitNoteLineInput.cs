using ErpApp.Domain.Catalog;

namespace ErpApp.Application.Purchasing;

public sealed record DebitNoteLineInput(Guid ProductId, decimal Quantity, decimal Rate, VatRate VatRate);
