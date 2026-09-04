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
    : IRequest<CreateQuotationResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitive, IAuditableRequest
{
    public string PermissionKey => PermissionKeys.QuotationCreate;
    public DocumentType AuditDocumentType => DocumentType.Quotation;
}

public sealed record CreateQuotationResult(Guid Id, string Code, QuotationStatus Status);
