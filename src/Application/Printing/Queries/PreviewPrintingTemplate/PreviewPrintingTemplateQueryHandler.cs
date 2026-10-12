using ErpApp.Application.Common.Formatting;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Storage;
using ErpApp.Application.Printing.Queries.PrintDocument;
using ErpApp.Domain.Common;
using ErpApp.Domain.Configuration;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Printing.Queries.PreviewPrintingTemplate;

/// <summary>
/// Phase 73 -- a sample document of the requested type with every element its type offers filled in, then the
/// unsaved settings applied by <see cref="PrintTemplateApplier"/>, the same function a real print goes through. So
/// what the editor shows hidden is exactly what a bill will hide.
/// </summary>
public sealed class PreviewPrintingTemplateQueryHandler(IAppDbContext db, IFileStorage storage)
    : IRequestHandler<PreviewPrintingTemplateQuery, PrintableDocumentDto>
{
    public async Task<PrintableDocumentDto> Handle(PreviewPrintingTemplateQuery request, CancellationToken cancellationToken)
    {
        // The validator has refused a null for each of these.
        var type = request.DocumentType!.Value;
        var hidden = request.HiddenElements!;
        var customFieldIds = request.PrintedCustomFieldIds!;

        await PrintingTemplateChecks.EnsureValidAsync(
            db, request.OrganizationId, type, request.Layout!.Value, hidden, customFieldIds, cancellationToken);

        var organization = await db.Organizations.SingleAsync(x => x.Id == request.OrganizationId, cancellationToken);
        var settings = new PrintingTemplateSettings(
            "Preview", request.Layout!.Value, hidden, customFieldIds, request.FooterText?.Trim() is { Length: > 0 } footer ? footer : null,
            request.CompactMargins!.Value);

        var offered = PrintElements.For(type);
        var taxDocument = PrintElements.IsTaxDocument(type);
        var today = NepalTime.LocalDate(DateTimeOffset.UtcNow);
        // Phase 84 -- a bill's date, which the money receipt is too; the statutory parts below stay the tax documents'.
        var billDated = PrintElements.IsBillDated(type);
        var date = billDated ? BillDate.Format(today) : RequestCalendar.Format(today);
        var logo = settings.Shows(PrintElement.OrganizationLogo)
            ? await PrintDocumentQueryHandler.ReadLogoAsync(storage, organization, cancellationToken)
            : null;

        var customFieldNames = await db.CustomFieldDefinitions
            .Where(x => x.OrganizationId == request.OrganizationId && customFieldIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var (sections, summary, total) = SampleBody(type, settings.Shows(PrintElement.ItemCode));
        var hasParty = offered.Contains(PrintElement.PartyAddress);

        // Phase 75 -- a sample location line on the same rule a real print follows: only for an organization with
        // more than one location.
        var multipleLocations = await db.BillingLocations.CountAsync(x => x.OrganizationId == request.OrganizationId, cancellationToken) > 1;

        var dto = new PrintableDocumentDto(
            type,
            SampleTitle(type),
            "SAMPLE",
            date,
            offered.Contains(PrintElement.Reference) ? "REF-001" : null,
            organization.Name,
            organization.Address,
            organization.Phone,
            organization.Email,
            organization.PanNumber,
            organization.Website,
            logo,
            hasParty ? "Bill To" : null,
            hasParty ? "C-001 — Sample Customer" : null,
            hasParty ? "New Road, Kathmandu" : null,
            settings.Name,
            [],
            sections,
            summary,
            offered.Contains(PrintElement.Notes) ? "Sample notes for this document." : null,
            offered.Contains(PrintElement.Terms) ? "<p>Goods once sold are not returned.</p>" : null,
            billDated ? null : RequestCalendar.DisclosureLine,
            PartyPan: taxDocument && hasParty ? "123456789" : null,
            DateLabel: taxDocument ? "Transaction Date" : "Date",
            IssueDateText: taxDocument ? date : null)
        {
            AmountInWords = offered.Contains(PrintElement.AmountInWords) ? AmountInWords.Rupees(total) : null,
            CustomFields = customFieldIds
                .Where(customFieldNames.ContainsKey)
                .Select(id => new PrintableFieldDto(customFieldNames[id], "Sample value"))
                .ToList(),
            LocationName = multipleLocations ? "Sample Branch" : null,
            LocationAddress = multipleLocations ? "Lakeside, Pokhara" : null,
        };

        return PrintTemplateApplier.Apply(dto, settings);
    }

    private static string SampleTitle(DocumentType type) => type switch
    {
        DocumentType.Invoice => "Tax Invoice",
        DocumentType.CreditNote => "Credit Note",
        DocumentType.DebitNote => "Debit Note",
        DocumentType.SalesOrder => "Sales Order",
        DocumentType.PurchaseOrder => "Purchase Order",
        DocumentType.PurchaseBill => "Purchase Bill",
        DocumentType.DeliveryNote => "Delivery Note",
        DocumentType.GoodsReceivedNote => "Goods Received Note",
        DocumentType.Payment => "Customer Receipt",
        DocumentType.JournalVoucher => "Journal Voucher",
        DocumentType.CashTransfer => "Cash Transfer",
        DocumentType.WarehouseTransfer => "Warehouse Transfer",
        DocumentType.InventoryAdjustment => "Inventory Adjustment",
        DocumentType.ProductionOrder => "Production Order",
        DocumentType.ProductionJournal => "Production Journal",
        _ => type.ToString(),
    };

    /// <summary>The sample's tables: the product table for the nine product-line types (every keyed column present,
    /// so each one can be seen to go), a product list for the other types that name products, and an account table
    /// for the rest.</summary>
    private static (IReadOnlyList<PrintableSectionDto> Sections, IReadOnlyList<PrintableFieldDto> Summary, decimal Total)
        SampleBody(DocumentType type, bool showItemCode)
    {
        string Product(string code, string name) => showItemCode ? $"{code} — {name}" : name;

        if (PrintElements.ProductLineTypes.Contains(type))
        {
            var items = new PrintableSectionDto(
                "Items",
                [
                    // The real product table's widths (PrintDocumentQueryHandler), so the preview wraps where a bill would.
                    new PrintableColumnDto("Product", 3.4f),
                    new PrintableColumnDto("HS Code", 1, Element: PrintElement.HsCode),
                    new PrintableColumnDto("Qty", 0.8f, AlignRight: true),
                    new PrintableColumnDto("Unit", 0.8f, Element: PrintElement.Unit),
                    new PrintableColumnDto("Rate", 1.4f, AlignRight: true),
                    new PrintableColumnDto("Disc %", 0.8f, AlignRight: true, Element: PrintElement.DiscountColumn),
                    new PrintableColumnDto("VAT", 1.2f, AlignRight: true, Element: PrintElement.VatColumn),
                    new PrintableColumnDto("Amount", 1.5f, AlignRight: true),
                ],
                [
                    new PrintableRowDto([Product("P-001", "Sample Product"), "8471", "2", "Pcs", "1,000.00", "0", "260.00", "2,260.00"]),
                    new PrintableRowDto([Product("P-002", "Second Product"), "8473", "5", "Pcs", "200.00", "10", "117.00", "1,017.00"]),
                ],
                new PrintableRowDto(["Total", string.Empty, "7", "Pcs", string.Empty, string.Empty, "377.00", "3,277.00"]));

            return (
                [items],
                [
                    new PrintableFieldDto("Sub Total", "2,900.00"),
                    new PrintableFieldDto("VAT", "377.00"),
                    new PrintableFieldDto("Grand Total", "3,277.00", Emphasise: true),
                ],
                3277m);
        }

        if (PrintElements.For(type).Contains(PrintElement.ItemCode))
        {
            return (
                [
                    new PrintableSectionDto(
                        "Items",
                        [new PrintableColumnDto("Product", 4), new PrintableColumnDto("Quantity", 1, AlignRight: true)],
                        [
                            new PrintableRowDto([Product("P-001", "Sample Product"), "2"]),
                            new PrintableRowDto([Product("P-002", "Second Product"), "5"]),
                        ]),
                ],
                [new PrintableFieldDto("Total Quantity", "7", Emphasise: true)],
                0m);
        }

        return (
            [
                new PrintableSectionDto(
                    "Lines",
                    [new PrintableColumnDto("Account", 4), new PrintableColumnDto("Amount", 1, AlignRight: true)],
                    [
                        new PrintableRowDto(["1001 — Sample Account", "2,500.00"]),
                        new PrintableRowDto(["1002 — Second Account", "500.00"]),
                    ],
                    new PrintableRowDto(["Total", "3,000.00"])),
            ],
            [new PrintableFieldDto("Total", "3,000.00", Emphasise: true)],
            3000m);
    }
}
