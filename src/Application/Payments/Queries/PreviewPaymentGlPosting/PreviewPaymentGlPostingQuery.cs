using ErpApp.Application.Common.Security;
using ErpApp.Domain.Payments;
using MediatR;

namespace ErpApp.Application.Payments.Queries.PreviewPaymentGlPosting;

/// <summary>Mirrors Accounting's PreviewGlPostingQuery -- the Payment form shows the computed GL
/// lines before Approve, confirmed live in the hands-on pass (erp-module-scan.md), for both
/// Customer and Supplier Payment.</summary>
public sealed record PreviewPaymentGlPostingQuery(Guid OrganizationId, Guid AccountId, decimal Amount, PaymentDirection Direction)
    : IRequest<IReadOnlyList<GlLinePreviewDto>>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.PaymentView;
}

public sealed record GlLinePreviewDto(Guid AccountId, decimal Debit, decimal Credit);
