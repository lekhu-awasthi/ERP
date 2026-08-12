using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using MediatR;

namespace ErpApp.Application.Sales.Queries.GetInvoiceConversionTemplate;

/// <summary>
/// architecture-spec.md §3.3's document-conversion pattern -- server-computes a pre-filled
/// CreateInvoiceCommand-shaped DTO from an Approved Quotation. Not its own domain command: the
/// Angular client still POSTs a normal CreateInvoiceCommand afterward (see SalesEndpoints'
/// "Convert to Invoice" flow's doc comment) -- this query has no side effects and no audit trail
/// of its own.
/// </summary>
public sealed record GetInvoiceConversionTemplateQuery(Guid OrganizationId, Guid QuotationId)
    : IRequest<InvoiceConversionTemplateDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.QuotationView;
}

public sealed record InvoiceConversionTemplateDto(
    Guid ContactId,
    DateOnly Date,
    string? Reference,
    DocumentType ReferrerType,
    Guid ReferrerId,
    IReadOnlyList<InvoiceLineInput> Lines);
