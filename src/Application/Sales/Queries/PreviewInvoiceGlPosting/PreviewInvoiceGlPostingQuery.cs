using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Sales.Queries.PreviewInvoiceGlPosting;

/// <summary>Mirrors Accounting's PreviewGlPostingQuery -- lets the Angular Invoice form show the
/// computed GL lines before the user clicks Approve, reusing InvoiceAccountResolver +
/// InvoicePostingRule exactly as ApproveInvoiceCommandHandler does (no duplicated math).</summary>
public sealed record PreviewInvoiceGlPostingQuery(Guid OrganizationId, IReadOnlyList<InvoiceLineInput> Lines)
    : IRequest<IReadOnlyList<GlLinePreviewDto>>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.InvoiceView;
}

public sealed record GlLinePreviewDto(Guid AccountId, decimal Debit, decimal Credit);
