using ErpApp.Domain.Common;
using ErpApp.Application.Printing.Queries.PrintDocument;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ErpApp.Api.Printing;

/// <summary>
/// Phase 20d's print-to-PDF renderer (FR-11.2, closes Phase 16c's deferred print-formatted
/// output) -- this codebase's first PDF output. QuestPDF was chosen over a headless-browser
/// HTML-to-PDF pipeline (Puppeteer/wkhtmltopdf-style): it's a pure, in-process C# library with no
/// Chromium/browser process to install, deploy, or keep patched -- consistent with this codebase's
/// bias against adding infra it doesn't strictly need (docs/phase-20d-status.md).
///
/// <para><b>Phase 27b reduced this from two layouts to one.</b> Phase 20d rendered a line-item
/// layout or a ledger layout depending on which of the DTO's two collections was populated. The
/// confirm-live pass that preceded 27b read the reference product's real print output for a
/// Production Journal, a Cash Transfer and a Customer Payment and found one frame with a varying
/// number of titled tables -- so <see cref="PrintableDocumentDto.Sections"/> now carries that
/// structure and this file renders it generically. <b>Nothing here switches on
/// <c>DocumentType</c>,</b> which is the property that makes one layout serve all fifteen types and
/// will serve Phase 28's without a change.</para>
///
/// <para>The tenant's default PrintingTemplate is still metadata only (that entity has no
/// layout-definition field at all -- see its doc comment); its Name is surfaced in the footer to
/// prove the wiring is real rather than vestigial.</para>
/// </summary>
public static class DocumentPdfRenderer
{
    public static byte[] Render(PrintableDocumentDto dto) =>
        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Element(header => RenderHeader(header, dto));
                page.Content().Element(content => RenderContent(content, dto));
                page.Footer().Element(footer => RenderFooter(footer, dto));
            });
        }).GeneratePdf();

    /// <summary>Organization block on the left, document title and number on the right -- the
    /// arrangement the reference product prints for every document type.</summary>
    private static void RenderHeader(IContainer container, PrintableDocumentDto dto)
    {
        container.PaddingBottom(10).Row(row =>
        {
            // Phase 39 -- the logo sits top-left, ahead of the organization block, which is where
            // the reference product prints it (Invoice print preview, read live 2026-09-13: a square
            // mark of roughly 80pt at the left edge). Its wider header treatment -- the organization
            // block centred beside the mark and the document title centred below rather than right-
            // aligned -- is a divergence this phase records and does not chase: re-laying out the one
            // shared layout would change all fifteen document types for a cosmetic reason.
            //
            // A ConstantItem rather than a RelativeItem so a tall logo cannot squeeze the text, and
            // FitArea so neither a wide nor a tall image distorts.
            //
            // The bytes are re-checked with the same ImageHeader the upload used, and skipped if
            // they are not one of the three formats. QuestPDF throws at GeneratePdf time for an
            // image it cannot decode, which is after composition and so not catchable around the
            // draw call -- and an organization whose stored logo has somehow gone bad still needs
            // its invoices to print.
            if (dto.OrganizationLogo is { Length: > 0 } logo && ImageHeader.TryRead(new MemoryStream(logo)) is not null)
            {
                row.ConstantItem(60).AlignTop().Height(60).Image(logo).FitArea();
                row.ConstantItem(12);
            }

            row.RelativeItem(2).Column(column =>
            {
                column.Item().Text(dto.OrganizationName).Bold().FontSize(16);

                if (!string.IsNullOrWhiteSpace(dto.OrganizationAddress))
                {
                    column.Item().Text(dto.OrganizationAddress).FontSize(9);
                }

                var contactLine = string.Join(
                    "  |  ",
                    new[]
                    {
                        dto.OrganizationPhone is { } phone ? $"Phone: {phone}" : null,
                        dto.OrganizationEmail is { } email ? $"Email: {email}" : null,
                        dto.OrganizationPan is { } pan ? $"PAN: {pan}" : null,
                        dto.OrganizationWebsite is { } website ? website : null,
                    }.Where(x => x is not null));

                if (!string.IsNullOrWhiteSpace(contactLine))
                {
                    column.Item().Text(contactLine).FontSize(9);
                }
            });

            row.RelativeItem().AlignRight().Column(column =>
            {
                column.Item().AlignRight().Text(dto.Title.ToUpperInvariant()).Bold().FontSize(16);
                column.Item().AlignRight().Text(dto.Code).FontSize(11);
                column.Item().AlignRight().Text($"Date: {dto.DateText}").FontSize(9);

                if (!string.IsNullOrWhiteSpace(dto.Reference))
                {
                    column.Item().AlignRight().Text($"Ref: {dto.Reference}").FontSize(9);
                }
            });
        });
    }

    private static void RenderContent(IContainer container, PrintableDocumentDto dto)
    {
        container.PaddingVertical(10).Column(column =>
        {
            column.Spacing(12);

            if (dto.PartyLabel is not null || dto.HeaderFields.Count > 0)
            {
                column.Item().Row(row =>
                {
                    row.RelativeItem().Column(party =>
                    {
                        if (dto.PartyLabel is null)
                        {
                            return;
                        }

                        party.Item().Text(dto.PartyHeading ?? "For").Bold().FontSize(9);
                        party.Item().Text(dto.PartyLabel);

                        if (!string.IsNullOrWhiteSpace(dto.PartyAddress))
                        {
                            party.Item().Text(dto.PartyAddress).FontSize(9);
                        }
                    });

                    row.RelativeItem().Column(fields =>
                    {
                        foreach (var field in dto.HeaderFields)
                        {
                            fields.Item().Text($"{field.Label}: {field.Value}").FontSize(9);
                        }
                    });
                });
            }

            foreach (var section in dto.Sections)
            {
                column.Item().Element(item => RenderSection(item, section));
            }

            if (dto.Summary.Count > 0)
            {
                column.Item().AlignRight().Width(220).Column(summary =>
                {
                    foreach (var field in dto.Summary)
                    {
                        summary.Item().Row(row =>
                        {
                            var label = row.RelativeItem().Text(field.Label);

                            // Phase 28: the emphasised summary line -- and only it -- carries the
                            // currency code, matching the reference product's printed Net Total
                            // ("NPR 3,06,500.00", confirmed live 2026-09-04). Every other money
                            // cell in the frame stays bare there, so it stays bare here.
                            var text = field.Emphasise ? $"{dto.CurrencyCode} {field.Value}" : field.Value;
                            var value = row.ConstantItem(110).AlignRight().Text(text);

                            if (field.Emphasise)
                            {
                                label.Bold().FontSize(11);
                                value.Bold().FontSize(11);
                            }
                        });
                    }
                });
            }

            if (!string.IsNullOrWhiteSpace(dto.Notes))
            {
                column.Item().Column(notes =>
                {
                    notes.Item().Text("Notes").Bold().FontSize(9);
                    notes.Item().Text(dto.Notes).FontSize(9);
                });
            }

            // Phase 39 -- Terms is rich text now, so it is drawn from the same RichTextNode tree
            // the stored HTML is emitted from rather than printed as a string. RichText.IsEmpty
            // rather than IsNullOrWhiteSpace: the editor's "empty" is "<p><br></p>", which would
            // otherwise print a heading over nothing.
            if (!RichText.IsEmpty(dto.Terms))
            {
                column.Item().Column(terms =>
                {
                    terms.Item().Text("Terms and Conditions").Bold().FontSize(9);
                    terms.Item().Element(body => RichTextPdfRenderer.Render(body, dto.Terms, 9));
                });
            }

            column.Item().PaddingTop(25).Row(row =>
            {
                row.RelativeItem().Text("Prepared By").FontSize(8).Light();
                row.RelativeItem().AlignRight().Text("Approved By").FontSize(8).Light();
            });
        });
    }

    /// <summary>One titled table. An empty section still prints its title and header row -- a
    /// receipt whose "Payment For" table vanishes reads as if nothing were outstanding, where an
    /// empty table plainly says nothing is allocated.</summary>
    private static void RenderSection(IContainer container, PrintableSectionDto section)
    {
        container.Column(column =>
        {
            column.Item().PaddingBottom(3).Text(section.Title).Bold().FontSize(11);

            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    foreach (var definition in section.Columns)
                    {
                        columns.RelativeColumn(definition.Width);
                    }
                });

                table.Header(header =>
                {
                    foreach (var definition in section.Columns)
                    {
                        var cell = header.Cell().BorderBottom(1).PaddingBottom(2);
                        (definition.AlignRight ? cell.AlignRight() : cell).Text(definition.Header).Bold().FontSize(9);
                    }
                });

                foreach (var row in section.Rows)
                {
                    WriteCells(table, section, row, bold: false);
                }

                if (section.TotalRow is { } totalRow)
                {
                    WriteCells(table, section, totalRow, bold: true, borderTop: true);
                }
            });
        });
    }

    /// <summary>A row carrying fewer cells than the section has columns pads with blanks rather
    /// than throwing: QuestPDF assigns cells positionally, so a short row would otherwise shift
    /// every following row one column left and produce a plausible-looking, wrong document.</summary>
    private static void WriteCells(
        TableDescriptor table, PrintableSectionDto section, PrintableRowDto row, bool bold, bool borderTop = false)
    {
        for (var index = 0; index < section.Columns.Count; index++)
        {
            var definition = section.Columns[index];
            var cell = table.Cell().PaddingVertical(2);

            if (borderTop)
            {
                cell = cell.BorderTop(1).PaddingTop(3);
            }

            var text = index < row.Cells.Count ? row.Cells[index] : string.Empty;
            var descriptor = (definition.AlignRight ? cell.AlignRight() : cell).Text(text);

            if (bold)
            {
                descriptor.Bold();
            }
        }
    }

    private static void RenderFooter(IContainer container, PrintableDocumentDto dto)
    {
        container.AlignCenter().Column(column =>
        {
            if (dto.CalendarNote is { } calendarNote)
            {
                column.Item().AlignCenter().Text(calendarNote).FontSize(8);
            }

            column.Item().AlignCenter().Text($"Template: {dto.PrintingTemplateName}").FontSize(8);
        });
    }
}
