using ErpApp.Domain.Common;
using ErpApp.Domain.Configuration;
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
/// <para><b>Phase 73 draws that frame three ways</b> (<see cref="PrintLayout"/>), still without a
/// <c>DocumentType</c> in sight, so each layout serves all seventeen types. <i>Standard</i> is the frame as it was;
/// <i>Traditional</i> rules every block; <i>Minimal</i> drops the logo and the rules and sets the header flush left.
/// What the template hides has already gone from the DTO (<c>PrintTemplateApplier</c>); this file only draws.</para>
/// </summary>
public static class DocumentPdfRenderer
{
    /// <summary>The few measures the three layouts differ by.</summary>
    private sealed record Style(bool Ruled, bool FlushLeft, float BaseFontSize, float MarginCm, float Spacing)
    {
        public static Style For(PrintableDocumentDto dto)
        {
            var style = dto.Layout switch
            {
                PrintLayout.Traditional => new Style(Ruled: true, FlushLeft: false, BaseFontSize: 10, MarginCm: 1.5f, Spacing: 10),
                PrintLayout.Minimal => new Style(Ruled: false, FlushLeft: true, BaseFontSize: 9, MarginCm: 1.5f, Spacing: 10),
                _ => new Style(Ruled: false, FlushLeft: false, BaseFontSize: 10, MarginCm: 2, Spacing: 12),
            };

            return dto.CompactMargins ? style with { MarginCm = 1, Spacing = style.Spacing / 2 } : style;
        }
    }

    private static readonly Color RuleColour = Colors.Grey.Darken2;

    public static byte[] Render(PrintableDocumentDto dto)
    {
        PdfFonts.EnsureRegistered();
        var style = Style.For(dto);

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(style.MarginCm, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(style.BaseFontSize));

                page.Header().Element(header => RenderHeader(header, dto, style));
                page.Content().Element(content => RenderContent(content, dto, style));
                page.Footer().Element(footer => RenderFooter(footer, dto));
            });
        }).GeneratePdf();
    }

    /// <summary>
    /// Phase 67 -- the reprint mark the Procedure Related to Computerized Invoicing, 2072, §6 asks for:
    /// a visible "copy of original" with how many times the document has been printed. Boxed, at the top
    /// and the bottom of every page, in the till receipt's own words (phase 62), so one bill's copies
    /// read alike on a till roll and on A4.
    /// </summary>
    private static void RenderCopyMark(IContainer container, PrintedCopyDto copy) =>
        container.AlignCenter().Border(1).PaddingVertical(2).PaddingHorizontal(8)
            .Text($"COPY OF ORIGINAL · printed {copy.PrintNumber} times").Bold().FontSize(10);

    /// <summary>
    /// Phase 44 (39 carried item #3) -- the header's wider arrangement, brought into line with the
    /// reference product: the organization block is <b>centred beside the logo</b> and the document
    /// title is <b>centred below</b>, rather than organization-left / title-right.
    ///
    /// <para><b>The logo is balanced, not just placed.</b> A constant spacer of the same width sits
    /// on the right of the row, so the organization block is centred on the <i>page</i> rather than
    /// on the space left over beside the mark. Without it a logo would shift the block right by half
    /// its own width, which reads as a misalignment rather than as a design.</para>
    ///
    /// <para>Phase 73 -- Traditional draws the same header inside a box; Minimal sets it flush left and never draws
    /// a logo (the vendor's Minimal has no logo section at all).</para>
    /// </summary>
    private static void RenderHeader(IContainer container, PrintableDocumentDto dto, Style style)
    {
        // Re-checked with the same ImageHeader the upload used, and skipped if the bytes are not one
        // of the three formats. QuestPDF throws at GeneratePdf time for an image it cannot decode --
        // after composition, so not catchable around the draw call -- and an organization whose
        // stored logo has somehow gone bad still needs its invoices to print.
        var logo = !style.FlushLeft
                   && dto.OrganizationLogo is { Length: > 0 } bytes
                   && ImageHeader.TryRead(new MemoryStream(bytes)) is not null
            ? bytes
            : null;

        container.PaddingBottom(style.Spacing - 2).Column(header =>
        {
            var organizationBox = header.Item();
            if (style.Ruled)
            {
                organizationBox = organizationBox.Border(1).BorderColor(RuleColour).Padding(6);
            }

            organizationBox.Row(row =>
            {
                if (logo is not null)
                {
                    // A ConstantItem rather than a RelativeItem so a tall logo cannot squeeze the
                    // text, and FitArea so neither a wide nor a tall image distorts.
                    row.ConstantItem(60).AlignTop().Height(60).Image(logo).FitArea();
                    row.ConstantItem(12);
                }

                row.RelativeItem().AlignMiddle().Column(column =>
                {
                    Aligned(column.Item(), style).Text(dto.OrganizationName).Bold().FontSize(style.FlushLeft ? 13 : 16);

                    if (!string.IsNullOrWhiteSpace(dto.OrganizationAddress))
                    {
                        Aligned(column.Item(), style).Text(dto.OrganizationAddress).FontSize(9);
                    }

                    // Phase 75 -- the issuing location, on its own line under the registered address.
                    if (!string.IsNullOrWhiteSpace(dto.LocationName))
                    {
                        var location = string.IsNullOrWhiteSpace(dto.LocationAddress)
                            ? dto.LocationName
                            : $"{dto.LocationName}, {dto.LocationAddress}";
                        Aligned(column.Item(), style).Text($"Location: {location}").FontSize(9);
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
                        Aligned(column.Item(), style).Text(contactLine).FontSize(9);
                    }
                });

                if (logo is not null)
                {
                    // The counterweight -- see the note above on centring the block on the page.
                    row.ConstantItem(72);
                }
            });

            var titleBlock = header.Item().PaddingTop(style.Ruled ? 0 : 8);
            if (style.Ruled)
            {
                titleBlock = titleBlock.BorderLeft(1).BorderRight(1).BorderBottom(1).BorderColor(RuleColour)
                    .Background(Colors.Grey.Lighten3).Padding(4);
            }

            titleBlock.Column(column =>
            {
                if (dto.PrintedCopy is { IsCopy: true } copy)
                {
                    column.Item().PaddingBottom(4).Element(item => RenderCopyMark(item, copy));
                }

                Aligned(column.Item(), style).Text(dto.Title.ToUpperInvariant()).Bold().FontSize(style.FlushLeft ? 13 : 16);

                // Phase 67 -- the heading in Nepali, as the till receipt prints it. Only this line
                // names the Devanagari family; the rest of the page keeps the default font.
                if (dto.TitleNepali is { } nepali)
                {
                    Aligned(column.Item(), style).Text(nepali).FontFamily(PdfFonts.Devanagari).Bold().FontSize(13);
                }

                Aligned(column.Item(), style).Text(dto.Code).FontSize(11);
                Aligned(column.Item(), style).Text($"{dto.DateLabel}: {dto.DateText}").FontSize(9);

                // Phase 71 -- a tax document's second statutory date, under the first (Schedule 5 sets them
                // side by side; a centred header has the room for one per line).
                if (dto.IssueDateText is { } issued)
                {
                    Aligned(column.Item(), style).Text($"Issue Date: {issued}").FontSize(9);
                }

                if (!string.IsNullOrWhiteSpace(dto.Reference))
                {
                    Aligned(column.Item(), style).Text($"Ref: {dto.Reference}").FontSize(9);
                }
            });
        });
    }

    private static IContainer Aligned(IContainer item, Style style) => style.FlushLeft ? item.AlignLeft() : item.AlignCenter();

    private static void RenderContent(IContainer container, PrintableDocumentDto dto, Style style)
    {
        container.PaddingVertical(style.Spacing - 2).Column(column =>
        {
            column.Spacing(style.Spacing);

            if (dto.PartyLabel is not null || dto.HeaderFields.Count > 0)
            {
                var block = column.Item();
                if (style.Ruled)
                {
                    block = block.Border(1).BorderColor(RuleColour);
                }

                block.Row(row =>
                {
                    var partyCell = row.RelativeItem();
                    if (style.Ruled)
                    {
                        partyCell = partyCell.BorderRight(1).BorderColor(RuleColour).Padding(6);
                    }

                    partyCell.Column(party =>
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

                        if (!string.IsNullOrWhiteSpace(dto.PartyPan))
                        {
                            party.Item().Text($"PAN: {dto.PartyPan}").FontSize(9);
                        }
                    });

                    var fieldsCell = row.RelativeItem();
                    if (style.Ruled)
                    {
                        fieldsCell = fieldsCell.Padding(6);
                    }

                    fieldsCell.Column(fields =>
                    {
                        foreach (var field in dto.HeaderFields)
                        {
                            fields.Item().Text($"{field.Label}: {field.Value}").FontSize(9);
                        }
                    });
                });
            }

            // Phase 73 -- the custom fields the template prints, as one wrapped line of label/value pairs between the
            // header and the tables, where the vendor's Classic layout prints them.
            if (dto.CustomFields is { Count: > 0 } customFields)
            {
                var block = column.Item();
                if (style.Ruled)
                {
                    block = block.Border(1).BorderColor(RuleColour).Padding(6);
                }

                block.Text(text =>
                {
                    for (var index = 0; index < customFields.Count; index++)
                    {
                        if (index > 0)
                        {
                            text.Span("     ");
                        }

                        text.Span($"{customFields[index].Label}: ").Bold().FontSize(9);
                        text.Span(customFields[index].Value).FontSize(9);
                    }
                });
            }

            foreach (var section in dto.Sections)
            {
                column.Item().Element(item => RenderSection(item, section, style));
            }

            if (dto.Summary.Count > 0)
            {
                var summaryBox = column.Item().AlignRight().Width(220);
                if (style.Ruled)
                {
                    summaryBox = summaryBox.Border(1).BorderColor(RuleColour).Padding(4);
                }

                summaryBox.Column(summary =>
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

            // Phase 73 -- the total in words, under the summary, where a reader checks it against the figure.
            if (!string.IsNullOrWhiteSpace(dto.AmountInWords))
            {
                column.Item().Text(text =>
                {
                    text.Span("Amount in Words: ").Bold().FontSize(9);
                    text.Span(dto.AmountInWords).Italic().FontSize(9);
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

            if (dto.ShowSignatureLine)
            {
                column.Item().PaddingTop(style.Spacing * 2).Row(row =>
                {
                    row.RelativeItem().Text("Prepared By").FontSize(8).Light();
                    row.RelativeItem().AlignRight().Text("Approved By").FontSize(8).Light();
                });
            }
        });
    }

    /// <summary>One titled table. An empty section still prints its title and header row -- a
    /// receipt whose "Payment For" table vanishes reads as if nothing were outstanding, where an
    /// empty table plainly says nothing is allocated. Phase 73 -- Traditional rules every cell and shades the
    /// header row; Minimal keeps only the rule under the header.</summary>
    private static void RenderSection(IContainer container, PrintableSectionDto section, Style style)
    {
        container.Column(column =>
        {
            column.Item().PaddingBottom(3).Text(section.Title).Bold().FontSize(style.FlushLeft ? 10 : 11);

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
                        var cell = style.Ruled
                            ? header.Cell().Border(1).BorderColor(RuleColour).Background(Colors.Grey.Lighten3).Padding(3)
                            : header.Cell().BorderBottom(1).PaddingBottom(2).PaddingHorizontal(3);
                        (definition.AlignRight ? cell.AlignRight() : cell).Text(definition.Header).Bold().FontSize(9);
                    }
                });

                foreach (var row in section.Rows)
                {
                    WriteCells(table, section, row, style, bold: false);
                }

                if (section.TotalRow is { } totalRow)
                {
                    WriteCells(table, section, totalRow, style, bold: true, borderTop: true);
                }
            });
        });
    }

    /// <summary>A row carrying fewer cells than the section has columns pads with blanks rather
    /// than throwing: QuestPDF assigns cells positionally, so a short row would otherwise shift
    /// every following row one column left and produce a plausible-looking, wrong document.</summary>
    private static void WriteCells(
        TableDescriptor table, PrintableSectionDto section, PrintableRowDto row, Style style, bool bold, bool borderTop = false)
    {
        for (var index = 0; index < section.Columns.Count; index++)
        {
            var definition = section.Columns[index];
            IContainer cell;
            if (style.Ruled)
            {
                cell = table.Cell().Border(1).BorderColor(RuleColour).Padding(3);
            }
            else
            {
                // The total's rule goes on the cell before any padding, so it runs unbroken across the columns; then a
                // little room each side (phase 73), or a right-aligned quantity runs into the unit beside it ("2Pcs").
                cell = table.Cell();
                if (borderTop)
                {
                    cell = cell.BorderTop(1).PaddingTop(1);
                }

                cell = cell.PaddingVertical(2).PaddingHorizontal(3);
            }

            var text = index < row.Cells.Count ? row.Cells[index] : string.Empty;
            var descriptor = (definition.AlignRight ? cell.AlignRight() : cell).Text(text);

            if (bold)
            {
                descriptor.Bold();
            }
        }
    }

    /// <summary>Phase 73 -- the template's footer line first; the "Template: name" line phase 20d printed to prove
    /// the wiring is gone, now that the template visibly changes the page.</summary>
    private static void RenderFooter(IContainer container, PrintableDocumentDto dto)
    {
        container.AlignCenter().Column(column =>
        {
            if (!string.IsNullOrWhiteSpace(dto.FooterText))
            {
                column.Item().AlignCenter().Text(dto.FooterText).FontSize(8);
            }

            if (dto.CalendarNote is { } calendarNote)
            {
                column.Item().AlignCenter().Text(calendarNote).FontSize(8);
            }

            // Phase 67 -- who printed this copy and when (the 2072 procedure's print record, which the
            // reference product's footer also carries), and the copy mark again at the foot.
            if (dto.PrintedCopy is { } printed)
            {
                column.Item().AlignCenter().Text($"Printed by {printed.PrintedBy} on {printed.PrintedAtText}").FontSize(8);

                if (printed.IsCopy)
                {
                    column.Item().PaddingTop(3).Element(item => RenderCopyMark(item, printed));
                }
            }
        });
    }
}
