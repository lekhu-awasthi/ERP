using ErpApp.Application.Common.Currencies;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using MediatR;

namespace ErpApp.Application.Sales.Commands.CreateQuotation;

public sealed record CreateQuotationCommand(
    Guid OrganizationId, Guid ContactId, DateOnly Date, DateOnly? ExpiryDate, string? Reference,
    IReadOnlyList<QuotationLineInput> Lines, decimal DiscountPct = 0,
    // Phase 27b -- the "+ Add Terms and Conditions" block's text, pre-filled client-side from a
    // CustomTemplate and editable from there. Optional and trailing so no existing caller changes.
    string? Terms = null)
    : IRequest<CreateQuotationResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitive, IAuditableRequest, ICurrencyBearingCommand
{
    public string PermissionKey => PermissionKeys.QuotationCreate;

    /// <summary>Phase 28 (FR-2.5). Null means the base currency at rate 1 -- see
    /// <see cref="ICurrencyBearingCommand"/>.</summary>
    public string? CurrencyCode { get; init; }

    /// <inheritdoc cref="CurrencyCode"/>
    public decimal? ExchangeRate { get; init; }
    public DocumentType AuditDocumentType => DocumentType.Quotation;
}

public sealed record CreateQuotationResult(Guid Id, string Code, QuotationStatus Status);
