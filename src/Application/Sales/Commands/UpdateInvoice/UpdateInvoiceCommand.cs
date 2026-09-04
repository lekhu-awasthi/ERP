using ErpApp.Application.Common.Currencies;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using MediatR;

namespace ErpApp.Application.Sales.Commands.UpdateInvoice;

public sealed record UpdateInvoiceCommand(
    Guid OrganizationId, Guid Id, Guid ContactId, Guid WarehouseId, DateOnly Date, string? Reference,
    IReadOnlyList<InvoiceLineInput> Lines, decimal DiscountPct = 0,
    bool IsExport = false, string? ExportCountry = null, string? ExportDeclarationNo = null,
    DateOnly? ExportDeclarationDate = null,
    // Phase 27b -- the "+ Add Terms and Conditions" block's text, pre-filled client-side from a
    // CustomTemplate and editable from there. Optional and trailing so no existing caller changes.
    string? Terms = null)
    : IRequest<UpdateInvoiceResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitive, IAuditableRequestWithId, ICurrencyBearingCommand
{
    public string PermissionKey => PermissionKeys.InvoiceEdit;

    /// <summary>Phase 28 (FR-2.5). Null means the base currency at rate 1 -- see
    /// <see cref="ICurrencyBearingCommand"/>.</summary>
    public string? CurrencyCode { get; init; }

    /// <inheritdoc cref="CurrencyCode"/>
    public decimal? ExchangeRate { get; init; }
    public DocumentType AuditDocumentType => DocumentType.Invoice;
    public Guid AuditDocumentId => Id;
}

public sealed record UpdateInvoiceResult(Guid Id, string Code, InvoiceStatus Status);
