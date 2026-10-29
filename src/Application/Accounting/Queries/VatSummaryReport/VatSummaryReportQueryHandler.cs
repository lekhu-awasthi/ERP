using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Purchasing.Reports;
using ErpApp.Application.Sales.Reports;
using ErpApp.Application.Trade;
using ErpApp.Domain.Common;
using ErpApp.Domain.Purchasing;
using MediatR;

namespace ErpApp.Application.Accounting.Queries.VatSummaryReport;

public sealed class VatSummaryReportQueryHandler(IAppDbContext db)
    : IRequestHandler<VatSummaryReportQuery, VatSummaryReportDto>
{
    public async Task<VatSummaryReportDto> Handle(VatSummaryReportQuery request, CancellationToken cancellationToken)
    {
        var months = TradeMonthlyCrosstab.Columns(request.FiscalYear)
            ?? throw new NotFoundException($"Fiscal year {request.FiscalYear} is outside the Bikram Sambat calendar this app supports.");
        var yearFrom = months[0].FromDate;
        var yearTo = months[^1].ToDate;

        // From the beginning of the books, not from Shrawan: an unused input credit carries forward across the fiscal
        // year's boundary (the user's ruling, 2026-10-12), so Shrawan's brought-forward is what every earlier month left.
        // One read per reader covers both the history and the year. Whole organization, every location and channel: the
        // return is filed once per PAN.
        var invoices = await SalesBookReader.LoadAsync(db, request.OrganizationId, DateOnly.MinValue, yearTo, null, cancellationToken);
        var creditNotes = await SalesReturnReader.LoadAsync(db, request.OrganizationId, DateOnly.MinValue, yearTo, null, cancellationToken);
        var purchases = await PurchaseBookReader.LoadAsync(db, request.OrganizationId, DateOnly.MinValue, yearTo, null, cancellationToken);
        var debitNotes = await PurchaseReturnReader.LoadAsync(db, request.OrganizationId, DateOnly.MinValue, yearTo, null, cancellationToken);

        var year = new Figures(months.Count);
        var history = new SortedDictionary<(int Year, int Month), Figures>();

        Figures? SlotFor(DateOnly date, out int column)
        {
            column = -1;
            if (date >= yearFrom)
            {
                for (var i = 0; i < months.Count; i++)
                {
                    if (date <= months[i].ToDate)
                    {
                        column = i;
                        return year;
                    }
                }

                return null;
            }

            // Before the year: bucketed by its own BS month for the carry. A date the BS table cannot express (before
            // 2000 BS) has no VAT period to belong to and carries nothing.
            if (BsCalendar.FromGregorian(date) is not { } bs)
            {
                return null;
            }

            column = 0;
            if (!history.TryGetValue((bs.Year, bs.Month), out var month))
            {
                month = new Figures(1);
                history[(bs.Year, bs.Month)] = month;
            }

            return month;
        }

        foreach (var invoice in invoices)
        {
            if (SlotFor(invoice.Date, out var c) is not { } f)
            {
                continue;
            }

            var b = invoice.Buckets;
            f.Add(VatReturnLines.TaxableSales, c, b.Taxable);
            f.Add(VatReturnLines.TaxableSalesVat, c, b.Vat);
            // An export's lines are zero-rated, so its value is in TaxExempt; it is reported once, as an export (SalesBookReader's
            // summary). Phase 97 -- a domestic invoice's zero-VAT value splits by its rate: a zero-rated supply is not an
            // exempt one under Nepali law and is never listed with exempt sales, where it would read as a supply that limits
            // the input credit (the tax advisor, 2026-10-21). It has its own line beside the exports.
            f.Add(VatReturnLines.ExportSales, c, invoice.ExportValue);
            f.Add(VatReturnLines.ZeroRatedLocalSales, c, invoice.IsExport ? 0 : b.ZeroRated);
            f.Add(VatReturnLines.LocalExemptSales, c, invoice.IsExport ? 0 : b.TaxExempt - b.ZeroRated);
        }

        foreach (var note in creditNotes)
        {
            if (SlotFor(note.Date, out var c) is not { } f)
            {
                continue;
            }

            var b = note.Buckets;
            f.Add(VatReturnLines.LocalExemptSalesReturns, c, b.TaxExempt - b.ZeroRated);
            f.Add(VatReturnLines.ZeroRatedSalesReturns, c, b.ZeroRated);
            f.Add(VatReturnLines.TaxableSalesReturns, c, b.Taxable);
            f.Add(VatReturnLines.TaxableSalesReturnsVat, c, b.Vat);
        }

        foreach (var purchase in purchases)
        {
            if (SlotFor(purchase.Date, out var c) is not { } f)
            {
                continue;
            }

            var b = purchase.Buckets;
            f.Add(VatReturnLines.TaxablePurchases, c, b.NonCapitalLocalValue);
            f.Add(VatReturnLines.TaxablePurchasesVat, c, b.NonCapitalLocalVat);
            f.Add(VatReturnLines.TaxableImports, c, b.NonCapitalImportValue);
            f.Add(VatReturnLines.TaxableImportsVat, c, b.NonCapitalImportVat);
            f.Add(VatReturnLines.CapitalPurchases, c, b.CapitalValue);
            f.Add(VatReturnLines.CapitalPurchasesVat, c, b.CapitalVat);
            f.Add(VatReturnLines.ExemptPurchases, c, b.TaxExempt);

            // Phase 99 -- the input VAT that is not credit (Rules 40 and 41), each document's from its own lines' terms.
            f.Add(VatReturnLines.ExemptUseVat, c, purchase.Credit.ExemptUseVat);
            f.Add(VatReturnLines.BlockedVat, c, purchase.Credit.BlockedVat);
            f.Add(VatReturnLines.SharedVat, c, purchase.Credit.SharedVat);
        }

        foreach (var note in debitNotes)
        {
            if (SlotFor(note.Date, out var c) is not { } f)
            {
                continue;
            }

            var b = note.Buckets;
            f.Add(VatReturnLines.ExemptPurchaseReturns, c, b.TaxExempt);
            f.Add(VatReturnLines.TaxablePurchaseReturns, c, b.NonCapitalLocalValue);
            f.Add(VatReturnLines.TaxablePurchaseReturnsVat, c, b.NonCapitalLocalVat);
            f.Add(VatReturnLines.TaxableImportReturns, c, b.NonCapitalImportValue);
            f.Add(VatReturnLines.TaxableImportReturnsVat, c, b.NonCapitalImportVat);
            f.Add(VatReturnLines.CapitalReturns, c, b.CapitalValue);
            f.Add(VatReturnLines.CapitalReturnsVat, c, b.CapitalVat);

            // Phase 99 -- a returned line gives back its disallowance with its VAT, in the month of the return, so each
            // credit line is the month's net, as the VAT lines above are once the returns' VAT is taken off.
            f.Add(VatReturnLines.ExemptUseVat, c, -note.Credit.ExemptUseVat);
            f.Add(VatReturnLines.BlockedVat, c, -note.Credit.BlockedVat);
            f.Add(VatReturnLines.SharedVat, c, -note.Credit.SharedVat);
        }

        // The credit carried into Shrawan: every earlier month in order, a month's net VAT first using up the credit it
        // inherited and any excess credit carrying on. A payable month pays; it does not reduce a later month's VAT.
        var carried = 0m;
        foreach (var month in history.Values)
        {
            var net = carried - month.MonthVat(0);
            carried = net > 0 ? net : 0;
        }

        var monthVat = new decimal[months.Count];
        var broughtForward = new decimal[months.Count];
        var payable = new decimal[months.Count];
        for (var i = 0; i < months.Count; i++)
        {
            monthVat[i] = year.MonthVat(i);
            broughtForward[i] = carried;
            payable[i] = monthVat[i] - carried;
            // Strictly negative only: negating a zero gives decimal's signed -0, which a sheet prints as "(0.00)"
            // (phase-26c bug #1).
            carried = payable[i] < 0 ? -payable[i] : 0;
        }

        var lines = VatReturnLines.All.Select(definition =>
        {
            var monthly = definition.Key switch
            {
                VatReturnLines.MonthVat => monthVat,
                VatReturnLines.CreditBroughtForward => broughtForward,
                VatReturnLines.Payable => payable,
                _ => year.Line(definition.Key),
            };
            // Phase 99 -- a month's proportion adds up to nothing either.
            decimal? total = definition.Key is VatReturnLines.CreditBroughtForward or VatReturnLines.Payable
                or VatReturnLines.TaxableShare
                ? null
                : monthly.Sum();

            return new VatReturnLineDto(
                definition.Key, definition.Section, definition.Label, definition.LabelNepali, definition.IsTotal,
                monthly, total, definition.IsPercent);
        }).ToList();

        return new VatSummaryReportDto(
            request.FiscalYear, yearFrom, yearTo, [.. months.Select(TradeMonthlyColumnDto.From)], lines);
    }

    /// <summary>The flow lines' figures for a run of month columns. A block's total line is derived from its parts, never
    /// accumulated separately, so it cannot disagree with them.</summary>
    private sealed class Figures(int columns)
    {
        private readonly Dictionary<string, decimal[]> _lines = [];

        public void Add(string key, int column, decimal value)
        {
            if (value == 0)
            {
                return;
            }

            if (!_lines.TryGetValue(key, out var monthly))
            {
                monthly = new decimal[columns];
                _lines[key] = monthly;
            }

            monthly[column] += value;
        }

        public decimal Get(string key, int column) => _lines.TryGetValue(key, out var monthly) ? monthly[column] : 0;

        public decimal[] Line(string key) =>
        [
            .. Enumerable.Range(0, columns).Select(c => key switch
            {
                VatReturnLines.TotalSales =>
                    Get(VatReturnLines.TaxableSales, c) + Get(VatReturnLines.ExportSales, c)
                    + Get(VatReturnLines.ZeroRatedLocalSales, c) + Get(VatReturnLines.LocalExemptSales, c),
                VatReturnLines.TotalSalesReturns =>
                    Get(VatReturnLines.LocalExemptSalesReturns, c) + Get(VatReturnLines.ZeroRatedSalesReturns, c)
                    + Get(VatReturnLines.TaxableSalesReturns, c),
                VatReturnLines.TotalPurchases =>
                    Get(VatReturnLines.TaxablePurchases, c) + Get(VatReturnLines.TaxableImports, c)
                    + Get(VatReturnLines.CapitalPurchases, c) + Get(VatReturnLines.ExemptPurchases, c),
                VatReturnLines.TotalPurchaseReturns =>
                    Get(VatReturnLines.ExemptPurchaseReturns, c) + Get(VatReturnLines.TaxablePurchaseReturns, c)
                    + Get(VatReturnLines.TaxableImportReturns, c) + Get(VatReturnLines.CapitalReturns, c),
                // Shown as a percent to two places; the arithmetic uses the unrounded share. It is the share applied, so a
                // month with no sales shows 100% (its shared purchases keep their whole credit, VatCredit.TaxableShare) and
                // a month whose every sale is exempt shows 0%, which is a figure and not a nil.
                VatReturnLines.TaxableShare => Math.Round(TaxableShare(c) * 100, 2, MidpointRounding.AwayFromZero),
                VatReturnLines.SharedVatDisallowed => SharedVatDisallowed(c),
                VatReturnLines.DisallowedInputVat => DisallowedInputVat(c),
                _ => Get(key, c),
            }),
        ];

        /// <summary>Phase 99 -- Rule 40(4)'s proportion for the month: taxable sales (exports and zero-rated sales
        /// included) over total sales, each net of its returns.</summary>
        public decimal TaxableShare(int column) => VatCredit.TaxableShare(TaxableSalesNet(column), TotalSalesNet(column));

        private decimal TaxableSalesNet(int column) =>
            Get(VatReturnLines.TaxableSales, column) + Get(VatReturnLines.ExportSales, column)
            + Get(VatReturnLines.ZeroRatedLocalSales, column)
            - Get(VatReturnLines.TaxableSalesReturns, column) - Get(VatReturnLines.ZeroRatedSalesReturns, column);

        private decimal TotalSalesNet(int column) =>
            TaxableSalesNet(column)
            + Get(VatReturnLines.LocalExemptSales, column) - Get(VatReturnLines.LocalExemptSalesReturns, column);

        /// <summary>Phase 99 -- the part of the month's shared-purchase VAT not in the taxable proportion.</summary>
        public decimal SharedVatDisallowed(int column) =>
            VatCredit.SharedDisallowed(Get(VatReturnLines.SharedVat, column), TaxableShare(column));

        /// <summary>Phase 99 -- all the month's input VAT that is not credit: exempt use and Rule 41, which the approvals
        /// already posted away from VAT Receivable, and the shared part, which the accountant posts by journal.</summary>
        public decimal DisallowedInputVat(int column) =>
            Get(VatReturnLines.ExemptUseVat, column) + Get(VatReturnLines.BlockedVat, column) + SharedVatDisallowed(column);

        /// <summary>Output VAT net of the sales returns' VAT, less input VAT (local, import and capital) net of the
        /// purchase returns' VAT and of the part that is not credit (phase 99). Positive is payable; negative is a
        /// credit, so the credit carried forward is the allowed credit only.</summary>
        public decimal MonthVat(int column) =>
            Get(VatReturnLines.TaxableSalesVat, column) - Get(VatReturnLines.TaxableSalesReturnsVat, column)
            - (Get(VatReturnLines.TaxablePurchasesVat, column) + Get(VatReturnLines.TaxableImportsVat, column)
                + Get(VatReturnLines.CapitalPurchasesVat, column)
                - Get(VatReturnLines.TaxablePurchaseReturnsVat, column) - Get(VatReturnLines.TaxableImportReturnsVat, column)
                - Get(VatReturnLines.CapitalReturnsVat, column)
                - DisallowedInputVat(column));
    }
}

/// <summary>
/// The return's lines, in the IRD form's order, with the form's Nepali wording as the vendor's screen prints it (read
/// 2026-10-12; its two typos, "पूपूंजीगत" and "बिक्रि", are not copied).
/// </summary>
public static class VatReturnLines
{
    public const string TaxableSales = "taxableSales";
    public const string TaxableSalesVat = "taxableSalesVat";
    public const string ExportSales = "exportSales";
    public const string ZeroRatedLocalSales = "zeroRatedLocalSales";
    public const string LocalExemptSales = "localExemptSales";
    public const string TotalSales = "totalSales";
    public const string LocalExemptSalesReturns = "localExemptSalesReturns";
    public const string ZeroRatedSalesReturns = "zeroRatedSalesReturns";
    public const string TaxableSalesReturns = "taxableSalesReturns";
    public const string TaxableSalesReturnsVat = "taxableSalesReturnsVat";
    public const string TotalSalesReturns = "totalSalesReturns";
    public const string TaxablePurchases = "taxablePurchases";
    public const string TaxablePurchasesVat = "taxablePurchasesVat";
    public const string TaxableImports = "taxableImports";
    public const string TaxableImportsVat = "taxableImportsVat";
    public const string CapitalPurchases = "capitalPurchases";
    public const string CapitalPurchasesVat = "capitalPurchasesVat";
    public const string ExemptPurchases = "exemptPurchases";
    public const string TotalPurchases = "totalPurchases";
    public const string ExemptPurchaseReturns = "exemptPurchaseReturns";
    public const string TaxablePurchaseReturns = "taxablePurchaseReturns";
    public const string TaxablePurchaseReturnsVat = "taxablePurchaseReturnsVat";
    public const string TaxableImportReturns = "taxableImportReturns";
    public const string TaxableImportReturnsVat = "taxableImportReturnsVat";
    public const string CapitalReturns = "capitalReturns";
    public const string CapitalReturnsVat = "capitalReturnsVat";
    public const string TotalPurchaseReturns = "totalPurchaseReturns";
    public const string ExemptUseVat = "exemptUseVat";
    public const string BlockedVat = "blockedVat";
    public const string SharedVat = "sharedVat";
    public const string TaxableShare = "taxableShare";
    public const string SharedVatDisallowed = "sharedVatDisallowed";
    public const string DisallowedInputVat = "disallowedInputVat";
    public const string MonthVat = "monthVat";
    public const string CreditBroughtForward = "creditBroughtForward";
    public const string Payable = "payable";

    public sealed record Definition(
        string Key, VatReturnSection Section, string Label, string LabelNepali, bool IsTotal = false, bool IsPercent = false);

    public static IReadOnlyList<Definition> All { get; } =
    [
        new(TaxableSales, VatReturnSection.Sales, "Taxable sales", "करयोग्य बिक्री"),
        new(TaxableSalesVat, VatReturnSection.Sales, "VAT on taxable sales", "करयोग्य बिक्रीको कर"),
        new(ExportSales, VatReturnSection.Sales, "Exports", "निकासी गरेको वस्तु वा सेवा"),
        // Phase 97 -- the two zero-rated lines' Nepali is ours, in the form's own words (शून्य दर, the zero rate): the vendor's
        // screen has no such line to copy.
        new(ZeroRatedLocalSales, VatReturnSection.Sales, "Zero-rated local sales", "शून्य दरको स्थानीय बिक्री"),
        new(LocalExemptSales, VatReturnSection.Sales, "Local exempt sales", "स्थानीय कर छुटको बिक्री"),
        new(TotalSales, VatReturnSection.Sales, "Total sales and exports", "जम्मा बिक्री / निकासी", IsTotal: true),
        new(LocalExemptSalesReturns, VatReturnSection.SalesReturns, "Local exempt sales returns", "स्थानीय कर छुटको फिर्ता"),
        new(ZeroRatedSalesReturns, VatReturnSection.SalesReturns, "Zero-rated sales returns", "शून्य दरको बिक्री फिर्ता"),
        new(TaxableSalesReturns, VatReturnSection.SalesReturns, "Taxable sales returns", "करयोग्य फिर्ता"),
        new(TaxableSalesReturnsVat, VatReturnSection.SalesReturns, "VAT on taxable sales returns", "करयोग्य फिर्ताको कर"),
        new(TotalSalesReturns, VatReturnSection.SalesReturns, "Total sales and export returns", "जम्मा बिक्री / निकासी फिर्ता", IsTotal: true),
        new(TaxablePurchases, VatReturnSection.Purchases, "Taxable purchases", "करयोग्य खरिद"),
        new(TaxablePurchasesVat, VatReturnSection.Purchases, "VAT on taxable purchases", "करयोग्य खरिदको कर"),
        new(TaxableImports, VatReturnSection.Purchases, "Taxable imports", "करयोग्य पैठारी"),
        new(TaxableImportsVat, VatReturnSection.Purchases, "VAT on taxable imports", "करयोग्य पैठारीको कर"),
        new(CapitalPurchases, VatReturnSection.Purchases, "Capital purchases and imports", "पूंजीगत करयोग्य खरिद / पैठारी"),
        new(CapitalPurchasesVat, VatReturnSection.Purchases, "VAT on capital purchases and imports", "पूंजीगत करयोग्य खरिद / पैठारीको कर"),
        new(ExemptPurchases, VatReturnSection.Purchases, "Exempt purchases and imports", "कर छुट हुने वस्तु वा सेवाको खरिद / पैठारी"),
        new(TotalPurchases, VatReturnSection.Purchases, "Total purchases and imports", "जम्मा खरिद / पैठारी", IsTotal: true),
        new(ExemptPurchaseReturns, VatReturnSection.PurchaseReturns, "Exempt purchase returns", "कर छुट हुने वस्तु वा सेवाको फिर्ता"),
        new(TaxablePurchaseReturns, VatReturnSection.PurchaseReturns, "Taxable purchase returns", "करयोग्य फिर्ता"),
        new(TaxablePurchaseReturnsVat, VatReturnSection.PurchaseReturns, "VAT on taxable purchase returns", "करयोग्य फिर्ताको कर"),
        new(TaxableImportReturns, VatReturnSection.PurchaseReturns, "Taxable import returns", "करयोग्य पैठारी फिर्ता"),
        new(TaxableImportReturnsVat, VatReturnSection.PurchaseReturns, "VAT on taxable import returns", "करयोग्य पैठारी फिर्ताको कर"),
        new(CapitalReturns, VatReturnSection.PurchaseReturns, "Capital returns", "पूंजीगत करयोग्य फिर्ता"),
        new(CapitalReturnsVat, VatReturnSection.PurchaseReturns, "VAT on capital returns", "पूंजीगत करयोग्य फिर्ताको कर"),
        new(TotalPurchaseReturns, VatReturnSection.PurchaseReturns, "Total purchase and import returns", "जम्मा खरिद / पैठारी फिर्ता", IsTotal: true),
        // Phase 99 -- the working of the input credit (VAT Rules 40 and 41). The IRD form has no such lines, so the Nepali
        // is ours, in the form's own words (कट्टी, the credit taken; करयोग्य and कर छुट as above). Each is the month's net of
        // its returns; the percent is the month's, so it has no year total.
        new(ExemptUseVat, VatReturnSection.InputCredit, "VAT on purchases for exempt sales (Rule 40(3))", "कर छुट बिक्रीका लागि गरिएको खरिदको कर"),
        new(BlockedVat, VatReturnSection.InputCredit, "VAT not claimable under Rule 41", "नियम ४१ अनुसार कट्टी गर्न नपाइने कर"),
        new(SharedVat, VatReturnSection.InputCredit, "VAT on purchases for both kinds of sales (Rule 40(4))", "दुवै प्रकारको बिक्रीका लागि गरिएको खरिदको कर"),
        new(TaxableShare, VatReturnSection.InputCredit, "Taxable sales ÷ total sales (%)", "जम्मा बिक्रीमा करयोग्य बिक्रीको अनुपात (%)", IsPercent: true),
        new(SharedVatDisallowed, VatReturnSection.InputCredit, "Shared VAT not claimable", "दुवैका लागि गरिएको खरिदमा कट्टी गर्न नपाइने कर"),
        new(DisallowedInputVat, VatReturnSection.InputCredit, "Input VAT not claimable", "कट्टी गर्न नपाइने जम्मा खरिद कर", IsTotal: true),
        new(MonthVat, VatReturnSection.Settlement, "VAT for the month, payable / (credit)", "महिनाको कर क्रेडिट / (डेबिट)"),
        new(CreditBroughtForward, VatReturnSection.Settlement, "Credit brought forward", "गत महिनाको मिलन गर्न बाँकी क्रेडिट"),
        new(Payable, VatReturnSection.Settlement, "VAT payable / (credit carried forward)", "तिर्नुपर्ने कर / (बाँकी क्रेडिट)", IsTotal: true),
    ];
}
