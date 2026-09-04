using ErpApp.Application.Common.Currencies;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using MediatR;

namespace ErpApp.Application.Sales.Commands.UpdateQuotation;

public sealed record UpdateQuotationCommand(
    Guid OrganizationId, Guid Id, Guid ContactId, DateOnly Date, DateOnly? ExpiryDate, string? Reference,
    IReadOnlyList<QuotationLineInput> Lines, decimal DiscountPct = 0,
    // Phase 27b -- the "+ Add Terms and Conditions" block's text, pre-filled client-side from a
    // CustomTemplate and editable from there. Optional and trailing so no existing caller changes.
    string? Terms = null)
    : IRequest<UpdateQuotationResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitive, IAuditableRequestWithId, ICurrencyBearingCommand
{
    public string PermissionKey => PermissionKeys.QuotationEdit;

    /// <summary>Phase 28 (FR-2.5). Null means the base currency at rate 1 -- see
    /// <see cref="ICurrencyBearingCommand"/>.</summary>
    public string? CurrencyCode { get; init; }

    /// <inheritdoc cref="CurrencyCode"/>
    public decimal? ExchangeRate { get; init; }
    public DocumentType AuditDocumentType => DocumentType.Quotation;
    public Guid AuditDocumentId => Id;
}

public sealed record UpdateQuotationResult(Guid Id, string Code, QuotationStatus Status);
