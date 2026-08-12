using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using MediatR;

namespace ErpApp.Application.Purchasing.Queries.GetPurchaseBillConversionTemplate;

/// <summary>
/// architecture-spec.md §3.3's document-conversion pattern, confirmed a second time on the
/// Purchase side (erp-module-scan.md's hands-on pass item 8: "same ?form_data= mechanism,
/// referrer_type: 'PurchaseOrder'") -- architecturally identical to
/// GetInvoiceConversionTemplateQuery, not a new pattern to invent.
/// </summary>
public sealed record GetPurchaseBillConversionTemplateQuery(Guid OrganizationId, Guid PurchaseOrderId)
    : IRequest<PurchaseBillConversionTemplateDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.PurchaseOrderView;
}

public sealed record PurchaseBillConversionTemplateDto(
    Guid ContactId,
    DateOnly Date,
    string? Reference,
    DocumentType ReferrerType,
    Guid ReferrerId,
    IReadOnlyList<PurchaseBillLineInput> Lines);
