using ErpApp.Application.Common.Security;
using ErpApp.Application.Trade;
using MediatR;

namespace ErpApp.Application.Accounting.Queries.VatSummaryReport;

/// <summary>
/// Phase 89 -- the VAT return, a BS fiscal year at a time: twelve month columns, Shrawan first and Asar last, and one row
/// per line of the IRD return (sales, exports, exempt sales, purchases, imports, capital purchases, exempt purchases,
/// each one's returns, then the month's VAT, the credit brought forward and what is payable).
///
/// <para><b>Read live first (2026-10-12, abcagro1).</b> Phase 8c built this report as three VAT-rate buckets over any date
/// range, designed blind and flagged as such ("reconcile against the live screen if it is ever opened"). The vendor's
/// screen is the return: a fiscal-year picker, twelve BS months, the return's lines in the IRD form's Nepali wording, a
/// month's credit carried into the next. A VAT period is a BS month (VAT Rules, Rule 26), so a date range that can
/// straddle two periods was never the right input. The user chose the return (2026-10-12), labelled in English with the
/// form's Nepali beside it.</para>
///
/// <para><b>It agrees with the registers by construction.</b> Every figure comes from the four shared readers the Sales
/// and Purchase Registers read (<c>SalesBookReader</c>, <c>SalesReturnReader</c>, <c>PurchaseBookReader</c>,
/// <c>PurchaseReturnReader</c>), so a month's sales lines equal the Sales Register over that month and its purchase
/// lines equal the Purchase Register. That is phase 36's rule: two reports agree only through one reader.</para>
///
/// <para><b>No location filter</b>: an IRD return is filed once per PAN (<c>ReportLocationSweepGuardTests</c>' exemption,
/// from the 2026-09-10 census).</para>
/// </summary>
public sealed record VatSummaryReportQuery(Guid OrganizationId, int FiscalYear)
    : IRequest<VatSummaryReportDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.VatSummaryView;
}

/// <summary>The return's five blocks, in the form's order.</summary>
public enum VatReturnSection
{
    Sales,
    SalesReturns,
    Purchases,
    PurchaseReturns,
    Settlement,
}

/// <summary>
/// One line of the return. <paramref name="Monthly"/> has one entry per column of <see cref="VatSummaryReportDto.Columns"/>,
/// in the same order. <paramref name="Total"/> is the year's sum for a flow line and null for the two balance lines (the
/// credit brought forward and the payable), whose months do not add up to anything.
/// </summary>
/// <param name="Key">A stable name for the line (<c>taxableSales</c>, …), for a client or a test to find it by.</param>
/// <param name="Label">The line in English.</param>
/// <param name="LabelNepali">The IRD form's own wording for the line, as the vendor's screen prints it.</param>
/// <param name="IsTotal">A block's total line, which the screen and the sheet print bold.</param>
public sealed record VatReturnLineDto(
    string Key,
    VatReturnSection Section,
    string Label,
    string LabelNepali,
    bool IsTotal,
    IReadOnlyList<decimal> Monthly,
    decimal? Total);

/// <param name="Lines">Every line of the return in the form's order, settlement last.</param>
public sealed record VatSummaryReportDto(
    int FiscalYear,
    DateOnly FromDate,
    DateOnly ToDate,
    IReadOnlyList<TradeMonthlyColumnDto> Columns,
    IReadOnlyList<VatReturnLineDto> Lines)
{
    /// <summary>The year's VAT for the months, payable when positive and a credit when negative.</summary>
    public decimal NetVat => Lines.Single(x => x.Key == VatReturnLines.MonthVat).Total ?? 0;
}
