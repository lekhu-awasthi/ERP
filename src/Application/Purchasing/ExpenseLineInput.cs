using ErpApp.Domain.Catalog;

namespace ErpApp.Application.Purchasing;

public sealed record ExpenseLineInput(Guid AccountId, decimal Amount, VatRate VatRate);
