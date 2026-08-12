using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using MediatR;

namespace ErpApp.Application.Sales.Queries.GetCreditNoteConversionTemplate;

/// <summary>Same architecture-spec.md §3.3 pattern as GetInvoiceConversionTemplateQuery, source
/// document is an Approved Invoice instead of a Quotation.</summary>
public sealed record GetCreditNoteConversionTemplateQuery(Guid OrganizationId, Guid InvoiceId)
    : IRequest<CreditNoteConversionTemplateDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.InvoiceView;
}

public sealed record CreditNoteConversionTemplateDto(
    Guid ContactId, DateOnly Date, string? Reference, DocumentType ReferrerType, Guid ReferrerId,
    IReadOnlyList<CreditNoteLineInput> Lines);
