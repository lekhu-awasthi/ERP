using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Trade.Queries.SalesSummaryReport;

/// <summary>
/// Sales Summary Report -- read live on 2026-09-03. Keyed by a BS fiscal year with a
/// <b>Select Mode</b> picker offering <c>Date</c> and <c>Month</c>, and a subtitle reading
/// "For fiscal year 2083 / 2084". Columns: Date, Sub Total, Discount, <i>Service Charge</i>,
/// Non Taxable Sales, Taxable Sales, VAT, Total. <b>No footer total row</b>, live -- and none here.
///
/// <para><b>Only periods with activity appear.</b> The live Month run returned two rows on a
/// three-year tenant, not twelve, and the Date run one row per day that had movement. That is the
/// opposite convention from the Monthly crosstabs, which always render all twelve columns, and it
/// is deliberate on both sides: a crosstab's columns are a fixed axis, a summary's rows are its
/// data.</para>
///
/// <para><b>Figures are net of returns</b> -- the live report prints negative rows for days whose
/// credit notes exceeded their invoices.</para>
///
/// <para><b>Service Charge is omitted, not zero-filled.</b> The live column is driven by a
/// product-level <c>service_charge_applicable</c> flag this codebase does not model, and it printed
/// "-" on every row of both modes even on the reference tenant. A column of hard zeroes would look
/// like an answer; its absence is the honest report. Recorded in docs/phase-26b-status.md as a
/// named gap with the flag that would have to exist first.</para>
/// </summary>
public sealed record SalesSummaryReportQuery(
    Guid OrganizationId,
    int FiscalYear,
    SalesSummaryMode Mode = SalesSummaryMode.Month,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize,
    bool ExportAll = false)
    : IRequest<SalesSummaryReportDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.SalesSummaryReportView;
}

/// <summary>The live "Select Mode" control's two options, in its own words.</summary>
public enum SalesSummaryMode
{
    Date,
    Month,
}

/// <summary>
/// One period. <paramref name="Label"/> is what the live screen prints in the Date column --
/// "Shrawan, 2083" in Month mode; in Date mode the DTO leaves it null and carries
/// <paramref name="Date"/> instead, so the client renders it through the user's own AD/BS
/// preference rather than having the server pick one (phase-23's rule: dates travel as AD, the
/// edge converts).
///
/// <para><paramref name="NonTaxableSales"/> and <paramref name="TaxableSales"/> split
/// <paramref name="SubTotal"/> less <paramref name="Discount"/> by whether the line's product
/// carries 13% VAT; <paramref name="Total"/> is those two plus <paramref name="Vat"/>.</para>
/// </summary>
public sealed record SalesSummaryRowDto(
    DateOnly? Date,
    string? Label,
    decimal SubTotal,
    decimal Discount,
    decimal NonTaxableSales,
    decimal TaxableSales,
    decimal Vat,
    decimal Total);

public sealed record SalesSummaryReportDto(
    int FiscalYear,
    SalesSummaryMode Mode,
    DateOnly FromDate,
    DateOnly ToDate,
    IReadOnlyList<SalesSummaryRowDto> Rows,
    int Page,
    int PageSize,
    int TotalCount);
