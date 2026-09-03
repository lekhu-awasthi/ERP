using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Accounting.Queries.NetTradingAssets;

/// <summary>
/// The Analytics Report group's <b>Net Trading Assets</b> (phase 26c, slug
/// <c>net-trading-assets</c>). Generated live on 2026-09-03: filters Period, <b>Compare</b> and
/// <b>Exclude Advance</b>; two columns, Particulars and Balance; four top-level rows, two of them
/// with two children:
/// <code>
/// Receivables            = Receivables from Customers + Advance to Suppliers
/// Payables               = Payable to Suppliers      + Advance from Customers
/// Inventory Items
/// Net Trading Assets     = Receivables - Payables + Inventory Items
/// </code>
/// Both identities were verified against the live figures to the last decimal.
///
/// <para><b>Every figure is a closing balance, so Compare is the as-of rule, not the range one.</b>
/// The header says "for the period", but nothing here is a period measure -- a receivable is what is
/// owed on a date. Phase-26a's <c>ComparePeriod</c> already settled that an as-of report compares
/// against the same calendar date one year earlier (a range report has a length to reuse; an as-of
/// report does not), and the window actually used is echoed on the response so the screen and the
/// .xlsx label the column with a real date rather than the word "prior".</para>
///
/// <para><b>Agreement with three other reports is by construction, not coincidence.</b> The four
/// contact figures come from <c>ContactLedgerReader</c> -- so Receivables from Customers equals
/// Customer Receivable Summary's positive closing balances, and Payable to Suppliers equals
/// Supplier Payable Summary's -- and Inventory Items comes from
/// <c>Inventory.Reports.StockFactReader</c>, so it equals Inventory Position's Amount total. That is
/// phase-26b's rule, and it is why this report loads nothing of its own.</para>
///
/// <para><b>The live leaves drill down; these do not.</b> Every leaf row on the live screen has an
/// expand triangle opening per-contact or per-item detail. That detail already exists here as four
/// shipped reports -- Customer Receivable Summary, Supplier Payable Summary and Inventory Position --
/// which read the same readers and therefore agree with these totals. Duplicating them inside this
/// response would be a second way to ask the same question; recorded as a deliberate omission.</para>
/// </summary>
public sealed record NetTradingAssetsQuery(
    Guid OrganizationId,
    DateOnly FromDate,
    DateOnly ToDate,
    bool Compare = false,
    bool ExcludeAdvance = false)
    : IRequest<NetTradingAssetsDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.NetTradingAssetsView;
}

/// <summary>
/// One line of the report. <paramref name="Children"/> is empty for a leaf; the two grouped rows
/// carry theirs so the screen renders the same indented shape the live report does.
/// <paramref name="CompareBalance"/> is null unless Compare was asked for.
/// </summary>
public sealed record NetTradingAssetsRowDto(
    string Particulars,
    decimal Balance,
    decimal? CompareBalance,
    IReadOnlyList<NetTradingAssetsRowDto> Children);

public sealed record NetTradingAssetsDto(
    DateOnly FromDate,
    DateOnly ToDate,
    bool ExcludeAdvance,
    /// <summary>The date the Compare column was computed at, echoed so it can be labelled. Null
    /// when Compare was not requested.</summary>
    DateOnly? CompareAsOfDate,
    IReadOnlyList<NetTradingAssetsRowDto> Rows);
