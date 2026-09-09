using ErpApp.Application.Common.Currencies;
using ErpApp.Application.Common.Locations;
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
    string? Terms = null,
    // Phase 31 -- the stored Due Date. Optional and trailing: null means "the document's own
    // date", which is both the live form's default and the exact backfill applied to every
    // pre-phase-31 row, so an unaware caller behaves identically to before. Carried on the Api's
    // request record too (phase-27b's Terms gotcha).
    DateOnly? DueDate = null)
    : IRequest<UpdateInvoiceResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitive, IAuditableRequestWithId, ICurrencyBearingCommand, ILocationBearingCommand, ILocationScopedDocument
{
    public string PermissionKey => PermissionKeys.InvoiceEdit;

    /// <summary>Phase 32b -- a location-scoped caller must hold the key at this document location.</summary>
    public Guid LocationDocumentId => Id;

    /// <summary>Phase 28 (FR-2.5). Null means the base currency at rate 1 -- see
    /// <see cref="ICurrencyBearingCommand"/>.</summary>
    public string? CurrencyCode { get; init; }

    /// <inheritdoc cref="CurrencyCode"/>
    public decimal? ExchangeRate { get; init; }

    /// <summary>Phase 32 (FR-2.3/FR-3.3). The billing location this document is raised from. Null
    /// means "the tenant's default", which <see cref="LocationResolver"/> resolves to HeadOffice --
    /// or to a real null when this document type is out of the tenant's LocationScopeMode. See
    /// <see cref="ILocationBearingCommand"/>.</summary>
    public Guid? LocationId { get; init; }
    public DocumentType AuditDocumentType => DocumentType.Invoice;
    public Guid AuditDocumentId => Id;
}

public sealed record UpdateInvoiceResult(Guid Id, string Code, InvoiceStatus Status);
