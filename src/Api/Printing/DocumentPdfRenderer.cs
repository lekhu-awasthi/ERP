using ErpApp.Application.Printing.Queries.PrintDocument;
using ErpApp.Domain.Common;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ErpApp.Api.Printing;

/// <summary>
/// Phase 20d's print-to-PDF renderer (FR-11.2, closes Phase 16c's deferred print-formatted
/// output) -- this codebase's first PDF output, ClosedXML's spreadsheet-only role leaving nothing
/// to reuse (see ReportSpreadsheetExporter's own doc comment on being the *first* binary export).
/// QuestPDF was chosen over a headless-browser HTML-to-PDF pipeline (Puppeteer/wkhtmltopdf-style):
/// it's a pure, in-process C# library with no Chromium/browser process to install, deploy, or keep
/// patched -- consistent with this codebase's bias against adding infra it doesn't strictly need
/// (see docs/phase-20d-status.md's rendering-engine decision).
///
/// Exactly two layouts, not one per DocumentType -- the "PrintingTemplate" a tenant picks as
/// default (see PrintDocumentQueryHandler) is metadata only this phase (no stored
/// layout-definition field at all, per that entity's own doc comment); its Name is surfaced as a
/// footer label to prove the wiring, but which layout renders is decided purely by which of
/// PrintableDocumentDto's Lines/GlLines is populated.
/// </summary>
public static class DocumentPdfRenderer
{
    public static byte[] Render(PrintableDocumentDto dto) =>
        dto.Lines is not null ? RenderLineItemDocument(dto) : RenderLedgerDocument(dto);

    private static byte[] RenderLineItemDocument(PrintableDocumentDto dto) =>
        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Element(header => RenderHeader(header, dto));

                page.Content().Column(column =>
                {
                    column.Spacing(10);

                    column.Item().Row(row =>
                    {
                        row.RelativeItem().Column(partyColumn =>
                        {
                            partyColumn.Item().Text("Bill To").Bold();
                            partyColumn.Item().Text(dto.PartyLabel ?? "-");
                            if (!string.IsNullOrWhiteSpace(dto.PartyAddress))
                            {
                                partyColumn.Item().Text(dto.PartyAddress);
                            }
                        });

                        row.RelativeItem().Column(metaColumn =>
                        {
                            metaColumn.Item().Text($"Date: {dto.Date:yyyy-MM-dd}");
                            if (!string.IsNullOrWhiteSpace(dto.Reference))
                            {
                                metaColumn.Item().Text($"Reference: {dto.Reference}");
                            }
                        });
                    });

                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(4);
                            columns.RelativeColumn(1);
                            columns.RelativeColumn(1);
                            columns.RelativeColumn(1);
                            columns.RelativeColumn(1);
                            columns.RelativeColumn(1);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Text("Product").Bold();
                            header.Cell().AlignRight().Text("Qty").Bold();
                            header.Cell().AlignRight().Text("Rate").Bold();
                            header.Cell().AlignRight().Text("Disc %").Bold();
                            header.Cell().AlignRight().Text("VAT").Bold();
                            header.Cell().AlignRight().Text("Amount").Bold();
                        });

                        foreach (var line in dto.Lines!)
                        {
                            table.Cell().Text(line.ProductLabel);
                            table.Cell().AlignRight().Text(line.Quantity.ToString("0.##"));
                            table.Cell().AlignRight().Text(line.Rate.ToString("0.##"));
                            table.Cell().AlignRight().Text(line.DiscountPct.ToString("0.##"));
                            table.Cell().AlignRight().Text(line.VatAmount.ToString("0.##"));
                            table.Cell().AlignRight().Text((line.Amount + line.VatAmount).ToString("0.##"));
                        }
                    });

                    column.Item().AlignRight().Column(totalsColumn =>
                    {
                        if (dto.DiscountPct is { } discountPct && discountPct > 0)
                        {
                            totalsColumn.Item().Text($"Discount: {discountPct:0.##}%");
                        }

                        totalsColumn.Item().Text($"Grand Total: {dto.GrandTotal ?? 0:0.00}").Bold().FontSize(12);
                    });
                });

                page.Footer().Element(footer => RenderFooter(footer, dto));
            });
        }).GeneratePdf();

    private static byte[] RenderLedgerDocument(PrintableDocumentDto dto) =>
        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Element(header => RenderHeader(header, dto));

                page.Content().Column(column =>
                {
                    column.Spacing(10);

                    column.Item().Row(row =>
                    {
                        row.RelativeItem().Text($"Date: {dto.Date:yyyy-MM-dd}");
                        if (!string.IsNullOrWhiteSpace(dto.Reference))
                        {
                            row.RelativeItem().Text($"Reference: {dto.Reference}");
                        }
                    });

                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(4);
                            columns.RelativeColumn(1);
                            columns.RelativeColumn(1);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Text("Account").Bold();
                            header.Cell().AlignRight().Text("Debit").Bold();
                            header.Cell().AlignRight().Text("Credit").Bold();
                        });

                        foreach (var line in dto.GlLines!)
                        {
                            table.Cell().Text(line.AccountLabel);
                            table.Cell().AlignRight().Text(line.Debit > 0 ? line.Debit.ToString("0.00") : "");
                            table.Cell().AlignRight().Text(line.Credit > 0 ? line.Credit.ToString("0.00") : "");
                        }
                    });

                    column.Item().AlignRight().Text($"Total: {dto.GrandTotal ?? 0:0.00}").Bold().FontSize(12);
                });

                page.Footer().Element(footer => RenderFooter(footer, dto));
            });
        }).GeneratePdf();

    private static void RenderHeader(QuestPDF.Infrastructure.IContainer container, PrintableDocumentDto dto)
    {
        container.Column(column =>
        {
            column.Item().Text(dto.OrganizationName).Bold().FontSize(16);

            if (!string.IsNullOrWhiteSpace(dto.OrganizationAddress))
            {
                column.Item().Text(dto.OrganizationAddress);
            }

            var contactLine = string.Join(
                "  |  ",
                new[]
                {
                    dto.OrganizationPhone is { } phone ? $"Phone: {phone}" : null,
                    dto.OrganizationEmail is { } email ? $"Email: {email}" : null,
                    dto.OrganizationPan is { } pan ? $"PAN: {pan}" : null,
                }.Where(x => x is not null));

            if (!string.IsNullOrWhiteSpace(contactLine))
            {
                column.Item().Text(contactLine);
            }

            column.Item().PaddingTop(5).Text($"{DocumentTypeLabel(dto.DocumentType)} — {dto.Code}").Bold().FontSize(14);
        });
    }

    private static void RenderFooter(QuestPDF.Infrastructure.IContainer container, PrintableDocumentDto dto)
    {
        container.AlignCenter().Text($"Template: {dto.PrintingTemplateName}").FontSize(8);
    }

    private static string DocumentTypeLabel(DocumentType documentType) => documentType switch
    {
        DocumentType.Invoice => "Invoice",
        DocumentType.Quotation => "Quotation",
        DocumentType.SalesOrder => "Sales Order",
        DocumentType.PurchaseOrder => "Purchase Order",
        DocumentType.PurchaseBill => "Purchase Bill",
        DocumentType.JournalVoucher => "Journal Voucher",
        _ => documentType.ToString(),
    };
}
