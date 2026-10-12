using ErpApp.Application.Common.Currencies;
using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Payments;
using MediatR;

namespace ErpApp.Application.Payments.Queries.PreviewPaymentGlPosting;

/// <summary>Mirrors Accounting's PreviewGlPostingQuery -- the Payment form shows the computed GL
/// lines before Approve, confirmed live in the hands-on pass (erp-module-scan.md), for both
/// Customer and Supplier Payment.
///
/// <para>Phase 88 -- it carries the payment's currency, rate and allocations, because Approve's entry depends on all
/// three (the amount in base currency, the realised exchange difference, and phase 87's split of the control leg).
/// Both are built by <c>PaymentGlLines</c>. It stays location-agnostic: the payload is an unsaved form, and the
/// targets it names only steer which of this organization's control accounts the lines show.</para>
/// </summary>
public sealed record PreviewPaymentGlPostingQuery(
    Guid OrganizationId,
    Guid AccountId,
    decimal Amount,
    PaymentDirection Direction,
    string? CurrencyCode,
    decimal? ExchangeRate,
    IReadOnlyList<PaymentAllocationInput> Allocations)
    : IRequest<IReadOnlyList<GlLinePreviewDto>>, IRequirePermission, IOrganizationScoped, ILocationAgnosticRequest,
        ICurrencyBearingCommand
{
    public string PermissionKey => PermissionKeys.PaymentView;
}

public sealed record GlLinePreviewDto(Guid AccountId, decimal Debit, decimal Credit);
