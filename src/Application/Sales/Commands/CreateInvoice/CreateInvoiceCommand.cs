using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using MediatR;

namespace ErpApp.Application.Sales.Commands.CreateInvoice;

public sealed record CreateInvoiceCommand(
    Guid OrganizationId,
    Guid ContactId,
    Guid WarehouseId,
    DateOnly Date,
    string? Reference,
    IReadOnlyList<InvoiceLineInput> Lines,
    DocumentType? ReferrerType = null,
    Guid? ReferrerId = null,
    decimal DiscountPct = 0,
    // FR-5.8. Optional even when IsExport is set -- the live reference product marks none of the
    // three with a required asterisk (unlike PurchaseBill's import block). Note the caller's
    // per-line VatRate is ignored for an export sale: Invoice.AddLine zero-rates every line.
    bool IsExport = false,
    string? ExportCountry = null,
    string? ExportDeclarationNo = null,
    DateOnly? ExportDeclarationDate = null)
    : IRequest<CreateInvoiceResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitive, IAuditableRequest
{
    public string PermissionKey => PermissionKeys.InvoiceCreate;
    public DocumentType AuditDocumentType => DocumentType.Invoice;
}

public sealed record CreateInvoiceResult(Guid Id, string Code, InvoiceStatus Status);
