using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Purchasing.Queries.PreviewPurchaseBillGlPosting;

/// <summary>Mirrors Sales' PreviewInvoiceGlPostingQuery -- lets the Angular PurchaseBill form show
/// the computed GL lines before the user clicks Approve, reusing PurchaseBillAccountResolver +
/// PurchaseBillPostingRule exactly as ApprovePurchaseBillCommandHandler does.</summary>
public sealed record PreviewPurchaseBillGlPostingQuery(
    Guid OrganizationId, IReadOnlyList<PurchaseBillLineInput> Lines, Guid? TdsTypeId)
    : IRequest<IReadOnlyList<GlLinePreviewDto>>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.PurchaseBillView;
}

public sealed record GlLinePreviewDto(Guid AccountId, decimal Debit, decimal Credit);
