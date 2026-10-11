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
///
/// <para><b>Phase 79 adds three more</b>, from the vendor's own previews read live on 2026-10-11: <i>Modern</i> and
/// <i>Classic</i> on A4, and <i>Retail</i>, an 80 mm roll for a thermal printer. Still no <c>DocumentType</c>: the
/// roll draws any section by stacking each row as its first cell over its other cells, so it would serve a payment as
/// well as an invoice; which types are <i>offered</i> it is the Domain's question (<see cref="PrintLayouts"/>).</para>
/// </summary>
public static class DocumentPdfRenderer
{
    /// <summary>The few measures the A4 layouts differ by.</summary>
    private sealed record Style(bool Ruled, bool FlushLeft, float BaseFontSize, float MarginCm, float Spacing)
    {
        public static Style For(PrintableDocumentDto dto)
        {
            var style = dto.Layout switch
            {
                PrintLayout.Traditional => new Style(Ruled: true, FlushLeft: false, BaseFontSize: 10, MarginCm: 1.5f, Spacing: 10),
                PrintLayout.Minimal => new Style(Ruled: false, FlushLeft: true, BaseFontSize: 9, MarginCm: 1.5f, Spacing: 10),
                PrintLayout.Modern => new Style(Ruled: false, FlushLeft: true, BaseFontSize: 10, MarginCm: 1.8f, Spacing: 12),
                _ => new Style(Ruled: false, FlushLeft: false, BaseFontSize: 10, MarginCm: 2, Spacing: 12),
            };

            return dto.CompactMargins ? style with { MarginCm = 1, Spacing = style.Spacing / 2 } : style;
        }
    }

    private static readonly Color RuleColour = Colors.Grey.Darken2;
    private static readonly Color FaintRule = Colors.Grey.Lighten1;
    private static readonly Color MutedText = Colors.Grey.Darken2;

    /// <summary>Phase 79 -- the roll's width, and its printable margin either side (a thermal head prints ~72 mm).</summary>
    private const float RollWidthMm = 80;
    private const float RollMarginMm = 4;

    public static byte[] Render(PrintableDocumentDto dto)
    {
        PdfFonts.EnsureRegistered();

        if (dto.Layout == PrintLayout.Retail)
        {
            return RenderRoll(dto);
        }

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
    private static void RenderCopyMark(IContainer container, PrintedCopyDto copy, float fontSize = 10) =>
        container.AlignCenter().Border(1).PaddingVertical(2).PaddingHorizontal(8)
            .Text($"COPY OF ORIGINAL · printed {copy.PrintNumber} times").Bold().FontSize(fontSize);

    /// <summary>The logo bytes if this layout draws a logo and the bytes are one of the three formats, else null.
    /// Re-checked with the same ImageHeader the upload used: QuestPDF throws at GeneratePdf time for an image it
    /// cannot decode -- after composition, so not catchable around the draw call -- and an organization whose stored
    /// logo has somehow gone bad still needs its invoices to print.</summary>
    private static byte[]? DrawableLogo(PrintableDocumentDto dto) =>
        dto.Layout != PrintLayout.Minimal
        && dto.OrganizationLogo is { Length: > 0 } bytes
        && ImageHeader.TryRead(new MemoryStream(bytes)) is not null
            ? bytes
            : null;

    /// <summary>Phase 75 -- the issuing location, "Location: name, address", or null.</summary>
    private static string? LocationLine(PrintableDocumentDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.LocationName))
        {
            return null;
        }

        var location = string.IsNullOrWhiteSpace(dto.LocationAddress)
            ? dto.LocationName
            : $"{dto.LocationName}, {dto.LocationAddress}";
        return $"Location: {location}";
    }

    /// <summary>The organization's contact items, each already labelled, in the order every layout prints them.</summary>
    private static IReadOnlyList<string> ContactItems(PrintableDocumentDto dto) =>
        new[]
        {
            dto.OrganizationPhone is { } phone ? $"Phone: {phone}" : null,
            dto.OrganizationEmail is { } email ? $"Email: {email}" : null,
            dto.OrganizationPan is { } pan ? $"PAN: {pan}" : null,
            dto.OrganizationWebsite is { } website ? website : null,
        }.Where(x => x is not null).Select(x => x!).ToList();

    /// <summary>Phase 79 -- the document's identity as label/value pairs, for the layouts that print it as a list
    /// (Modern, Classic, the roll) rather than as centred lines: its number, its date or the tax document's two
    /// statutory dates, the reference, then the type's own header fields.</summary>
    private static IReadOnlyList<(string Label, string Value)> DetailPairs(PrintableDocumentDto dto)
    {
        var pairs = new List<(string, string)> { ("Number", dto.Code), (dto.DateLabel, dto.DateText) };
        if (dto.IssueDateText is { } issued)
        {
            pairs.Add(("Issue Date", issued));
        }

        if (!string.IsNullOrWhiteSpace(dto.Reference))
        {
            pairs.Add(("Ref", dto.Reference));
        }

        pairs.AddRange(dto.HeaderFields.Select(field => (field.Label, field.Value)));
        return pairs;
    }

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
    /// a logo (the vendor's Minimal has no logo section at all). Phase 79 -- Modern and Classic draw their own.</para>
    /// </summary>
    private static void RenderHeader(IContainer container, PrintableDocumentDto dto, Style style)
    {
        switch (dto.Layout)
        {
            case PrintLayout.Modern:
                RenderModernHeader(container, dto, style);
                return;
            case PrintLayout.Classic:
                RenderClassicHeader(container, dto, style);
                return;
        }

        var logo = DrawableLogo(dto);

        container.PaddingBottom(style.Spacing - 2).Column(header =>
        {
            var organizationBox = header.Item();
            if (style.Ruled)
            {
                organizationBox = organizationBox.Border(1).BorderColor(RuleColour).Padding(6);
            }

            organizationBox.Element(box => RenderCentredOrganization(box, dto, style, logo));

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

                RenderTitle(column, dto, style);

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

    /// <summary>The organization block centred beside a balanced logo (Standard, Traditional, Classic), or flush left
    /// with no logo (Minimal).</summary>
    private static void RenderCentredOrganization(IContainer container, PrintableDocumentDto dto, Style style, byte[]? logo)
    {
        container.Row(row =>
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
                if (LocationLine(dto) is { } location)
                {
                    Aligned(column.Item(), style).Text(location).FontSize(9);
                }

                var contactLine = string.Join("  |  ", ContactItems(dto));
                if (!string.IsNullOrWhiteSpace(contactLine))
                {
                    Aligned(column.Item(), style).Text(contactLine).FontSize(9);
                }
            });

            if (logo is not null)
            {
                // The counterweight -- see the note on RenderHeader on centring the block on the page.
                row.ConstantItem(72);
            }
        });
    }

    /// <summary>The title in capitals, and (phase 67) under it the heading in Nepali, as the till receipt prints it.
    /// Only that line names the Devanagari family; the rest of the page keeps the default font.</summary>
    private static void RenderTitle(ColumnDescriptor column, PrintableDocumentDto dto, Style style, float size = 0)
    {
        var titleSize = size > 0 ? size : style.FlushLeft ? 13 : 16;
        Aligned(column.Item(), style).Text(dto.Title.ToUpperInvariant()).Bold().FontSize(titleSize);

        if (dto.TitleNepali is { } nepali)
        {
            Aligned(column.Item(), style).Text(nepali).FontFamily(PdfFonts.Devanagari).Bold().FontSize(13);
        }
    }

    /// <summary>Phase 79 -- Modern's header: the organization flush left in light type, each detail on its own line,
    /// the logo opposite, and a rule under each half of the page (the vendor's two rules).</summary>
    private static void RenderModernHeader(IContainer container, PrintableDocumentDto dto, Style style)
    {
        var logo = DrawableLogo(dto);

        container.PaddingBottom(style.Spacing - 2).Column(header =>
        {
            if (dto.PrintedCopy is { IsCopy: true } copy)
            {
                header.Item().PaddingBottom(6).Element(item => RenderCopyMark(item, copy));
            }

            header.Item().Row(row =>
            {
                row.RelativeItem().Column(organization =>
                {
                    organization.Item().Text(dto.OrganizationName).FontSize(18).Light();

                    var lines = new List<string>();
                    if (!string.IsNullOrWhiteSpace(dto.OrganizationAddress))
                    {
                        lines.Add(dto.OrganizationAddress);
                    }

                    if (LocationLine(dto) is { } location)
                    {
                        lines.Add(location);
                    }

                    lines.AddRange(ContactItems(dto));
                    foreach (var line in lines)
                    {
                        organization.Item().Text(line).FontSize(9).FontColor(MutedText);
                    }
                });

                if (logo is not null)
                {
                    row.ConstantItem(70).AlignRight().AlignTop().Height(60).Image(logo).FitArea();
                }
            });

            header.Item().PaddingTop(8).Row(rules =>
            {
                rules.RelativeItem().LineHorizontal(1).LineColor(RuleColour);
                rules.ConstantItem(20);
                rules.RelativeItem(1.25f).LineHorizontal(1).LineColor(RuleColour);
            });
        });
    }

    /// <summary>Phase 79 -- Classic's header: Standard's centred organization block over a full-width rule, then the
    /// title centred under the rule.</summary>
    private static void RenderClassicHeader(IContainer container, PrintableDocumentDto dto, Style style)
    {
        var logo = DrawableLogo(dto);

        container.PaddingBottom(style.Spacing - 2).Column(header =>
        {
            header.Item().Element(box => RenderCentredOrganization(box, dto, style, logo));
            header.Item().PaddingVertical(6).LineHorizontal(1).LineColor(RuleColour);

            if (dto.PrintedCopy is { IsCopy: true } copy)
            {
                header.Item().PaddingBottom(4).Element(item => RenderCopyMark(item, copy));
            }

            RenderTitle(header, dto, style, size: 14);
        });
    }

    private static IContainer Aligned(IContainer item, Style style) => style.FlushLeft ? item.AlignLeft() : item.AlignCenter();

    private static void RenderContent(IContainer container, PrintableDocumentDto dto, Style style)
    {
        switch (dto.Layout)
        {
            case PrintLayout.Modern:
                RenderModernContent(container, dto, style);
                return;
            case PrintLayout.Classic:
                RenderClassicContent(container, dto, style);
                return;
        }

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

                    partyCell.Element(cell => RenderParty(cell, dto, heading: dto.PartyHeading ?? "For"));

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

            RenderCustomFields(column, dto, ruled: style.Ruled);

            foreach (var section in dto.Sections)
            {
                column.Item().Element(item => RenderSection(item, section, style, dto.Layout));
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
                        summary.Item().Row(row => RenderSummaryRow(row, dto, field));
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

            RenderNotesAndTerms(column, dto);

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

    /// <summary>The party block: its heading, name, address and the buyer's PAN. Draws nothing for a document with no
    /// party.</summary>
    private static void RenderParty(IContainer container, PrintableDocumentDto dto, string heading, bool uppercaseHeading = false)
    {
        container.Column(party =>
        {
            if (dto.PartyLabel is null)
            {
                return;
            }

            party.Item().Text(uppercaseHeading ? heading.ToUpperInvariant() : heading).Bold().FontSize(9);
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
    }

    /// <summary>One summary line: the label, and the value right-aligned. Phase 28: the emphasised summary line -- and
    /// only it -- carries the currency code, matching the reference product's printed Net Total ("NPR 3,06,500.00",
    /// confirmed live 2026-09-04). Every other money cell in the frame stays bare there, so it stays bare here.</summary>
    private static void RenderSummaryRow(RowDescriptor row, PrintableDocumentDto dto, PrintableFieldDto field, float valueWidth = 110)
    {
        var label = row.RelativeItem().Text(field.Label);
        var text = field.Emphasise ? $"{dto.CurrencyCode} {field.Value}" : field.Value;
        var value = row.ConstantItem(valueWidth).AlignRight().Text(text);

        if (field.Emphasise)
        {
            label.Bold().FontSize(11);
            value.Bold().FontSize(11);
        }
    }

    /// <summary>Phase 73 -- the custom fields the template prints, as one wrapped line of label/value pairs between the
    /// header and the tables, where the vendor's Classic layout prints them.</summary>
    private static void RenderCustomFields(ColumnDescriptor column, PrintableDocumentDto dto, bool ruled)
    {
        if (dto.CustomFields is not { Count: > 0 } customFields)
        {
            return;
        }

        var block = column.Item();
        if (ruled)
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

    /// <summary>Notes, then terms. Phase 39 -- Terms is rich text, drawn from the same RichTextNode tree the stored HTML
    /// is emitted from rather than printed as a string. RichText.IsEmpty rather than IsNullOrWhiteSpace: the editor's
    /// "empty" is "&lt;p&gt;&lt;br&gt;&lt;/p&gt;", which would otherwise print a heading over nothing.</summary>
    private static void RenderNotesAndTerms(ColumnDescriptor column, PrintableDocumentDto dto, float fontSize = 9)
    {
        if (!string.IsNullOrWhiteSpace(dto.Notes))
        {
            column.Item().Column(notes =>
            {
                notes.Item().Text("Notes").Bold().FontSize(fontSize);
                notes.Item().Text(dto.Notes).FontSize(fontSize);
            });
        }

        if (!RichText.IsEmpty(dto.Terms))
        {
            column.Item().Column(terms =>
            {
                terms.Item().Text("Terms and Conditions").Bold().FontSize(fontSize);
                terms.Item().Element(body => RichTextPdfRenderer.Render(body, dto.Terms, fontSize));
            });
        }
    }

    /// <summary>Label/value pairs as a two-column table: the label muted, the value bold (Modern, left-aligned) or
    /// plain and right-aligned (Classic). The label column is fixed, wide enough for "Exchange Rate To NPR:" on one line
    /// (a relative one wrapped it in the E2E), and the block takes a little more than half the row for the same reason.</summary>
    private static void RenderPairs(IContainer container, IReadOnlyList<(string Label, string Value)> pairs, bool valuesRight)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(108);
                columns.RelativeColumn();
            });

            foreach (var (label, value) in pairs)
            {
                table.Cell().PaddingVertical(1).Text($"{label}:").FontSize(9).FontColor(MutedText);
                var cell = table.Cell().PaddingVertical(1);
                if (valuesRight)
                {
                    cell.AlignRight().Text(value).FontSize(9);
                }
                else
                {
                    cell.Text(value).FontSize(9).Bold();
                }
            }
        });
    }

    /// <summary>Phase 79 -- Modern's body: the party opposite the title and its details, a table with a rule under
    /// every row, and the totals with the total in words beside the notes.</summary>
    private static void RenderModernContent(IContainer container, PrintableDocumentDto dto, Style style)
    {
        container.PaddingVertical(style.Spacing - 2).Column(column =>
        {
            column.Spacing(style.Spacing);

            column.Item().Row(row =>
            {
                row.RelativeItem().Element(cell => RenderParty(cell, dto, dto.PartyHeading ?? "For", uppercaseHeading: true));
                row.ConstantItem(20);
                row.RelativeItem(1.25f).Column(details =>
                {
                    RenderTitle(details, dto, style, size: 14);
                    details.Item().PaddingTop(4).Element(item => RenderPairs(item, DetailPairs(dto), valuesRight: false));
                });
            });

            RenderCustomFields(column, dto, ruled: false);

            foreach (var section in dto.Sections)
            {
                column.Item().Element(item => RenderSection(item, section, style, dto.Layout));
            }

            column.Item().Row(row =>
            {
                row.RelativeItem().Column(left => RenderNotesAndTerms(left, dto));
                row.ConstantItem(16);
                row.ConstantItem(230).Column(right =>
                {
                    foreach (var field in dto.Summary)
                    {
                        var line = right.Item();
                        line = field.Emphasise
                            ? line.BorderTop(1.5f).BorderColor(RuleColour).PaddingVertical(4)
                            : line.BorderBottom(0.5f).BorderColor(FaintRule).PaddingVertical(3);
                        line.Row(summaryRow => RenderSummaryRow(summaryRow, dto, field));
                    }

                    if (!string.IsNullOrWhiteSpace(dto.AmountInWords))
                    {
                        right.Item().PaddingTop(10).AlignRight().Text("AMOUNT IN WORDS").Bold().FontSize(8);
                        right.Item().AlignRight().Text(dto.AmountInWords).FontSize(9);
                    }
                });
            });

            if (dto.ShowSignatureLine)
            {
                column.Item().PaddingTop(style.Spacing * 2).Element(item => RenderSignatureRules(item));
            }
        });
    }

    /// <summary>Phase 79 -- Classic's body: "Bill To" opposite the details, the gridded tables, then one box holding
    /// the notes and the signature beside the totals and the total in words (the vendor's Classic).</summary>
    private static void RenderClassicContent(IContainer container, PrintableDocumentDto dto, Style style)
    {
        container.PaddingVertical(style.Spacing - 2).Column(column =>
        {
            column.Spacing(style.Spacing);

            column.Item().BorderBottom(0.5f).BorderColor(FaintRule).PaddingBottom(6).Row(row =>
            {
                row.RelativeItem().Element(cell => RenderParty(cell, dto, $"{dto.PartyHeading ?? "For"}:"));
                row.ConstantItem(20);
                row.RelativeItem(1.25f).Element(item => RenderPairs(item, DetailPairs(dto), valuesRight: true));
            });

            RenderCustomFields(column, dto, ruled: false);

            foreach (var section in dto.Sections)
            {
                column.Item().Element(item => RenderSection(item, section, style, dto.Layout));
            }

            var hasLeft = !string.IsNullOrWhiteSpace(dto.Notes) || !RichText.IsEmpty(dto.Terms) || dto.ShowSignatureLine;
            var hasRight = dto.Summary.Count > 0 || !string.IsNullOrWhiteSpace(dto.AmountInWords);
            if (!hasLeft && !hasRight)
            {
                return;
            }

            column.Item().Border(1).BorderColor(RuleColour).Row(row =>
            {
                row.RelativeItem().BorderRight(1).BorderColor(RuleColour).Padding(6).Column(left =>
                {
                    left.Spacing(6);
                    RenderNotesAndTerms(left, dto);

                    if (dto.ShowSignatureLine)
                    {
                        left.Item().PaddingTop(28).Element(item => RenderSignatureRules(item));
                    }
                });

                row.ConstantItem(240).Column(right =>
                {
                    foreach (var field in dto.Summary)
                    {
                        right.Item().BorderBottom(0.5f).BorderColor(RuleColour).PaddingVertical(3).PaddingHorizontal(4)
                            .Row(summaryRow => RenderSummaryRow(summaryRow, dto, field));
                    }

                    if (!string.IsNullOrWhiteSpace(dto.AmountInWords))
                    {
                        right.Item().Padding(4).AlignRight().Text("Amount in Words").Bold().FontSize(9);
                        right.Item().PaddingHorizontal(4).PaddingBottom(4).AlignRight().Text(dto.AmountInWords).FontSize(9);
                    }
                });
            });
        });
    }

    /// <summary>Phase 79 -- the signature line as two short rules with their captions under them, for the layouts that
    /// box or rule their foot. Relative widths, since Classic draws it in half a page and Modern across a whole one.</summary>
    private static void RenderSignatureRules(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem(2).BorderTop(0.75f).BorderColor(RuleColour).PaddingTop(2).Text("Prepared By").FontSize(8);
            row.RelativeItem(1);
            row.RelativeItem(2).BorderTop(0.75f).BorderColor(RuleColour).PaddingTop(2).AlignRight().Text("Approved By").FontSize(8);
        });
    }

    /// <summary>One titled table. An empty section still prints its title and header row -- a
    /// receipt whose "Payment For" table vanishes reads as if nothing were outstanding, where an
    /// empty table plainly says nothing is allocated. Phase 73 -- Traditional rules every cell and shades the
    /// header row; Minimal keeps only the rule under the header. Phase 79 -- Classic grids every cell without the
    /// shading, and Modern rules under every row.</summary>
    private static void RenderSection(IContainer container, PrintableSectionDto section, Style style, PrintLayout layout)
    {
        var grid = style.Ruled || layout == PrintLayout.Classic;

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
                        var cell = layout switch
                        {
                            PrintLayout.Classic => header.Cell().Border(0.5f).BorderColor(RuleColour).Padding(3),
                            PrintLayout.Modern => header.Cell().BorderBottom(1.5f).BorderColor(RuleColour).PaddingVertical(3).PaddingHorizontal(3),
                            _ when style.Ruled => header.Cell().Border(1).BorderColor(RuleColour).Background(Colors.Grey.Lighten3).Padding(3),
                            _ => header.Cell().BorderBottom(1).PaddingBottom(2).PaddingHorizontal(3),
                        };
                        var label = layout == PrintLayout.Modern ? definition.Header.ToUpperInvariant() : definition.Header;
                        (definition.AlignRight ? cell.AlignRight() : cell).Text(label).Bold().FontSize(layout == PrintLayout.Modern ? 8 : 9);
                    }
                });

                foreach (var row in section.Rows)
                {
                    WriteCells(table, section, row, grid, layout, bold: false);
                }

                if (section.TotalRow is { } totalRow)
                {
                    WriteCells(table, section, totalRow, grid, layout, bold: true, borderTop: true);
                }
            });
        });
    }

    /// <summary>A row carrying fewer cells than the section has columns pads with blanks rather
    /// than throwing: QuestPDF assigns cells positionally, so a short row would otherwise shift
    /// every following row one column left and produce a plausible-looking, wrong document.</summary>
    private static void WriteCells(
        TableDescriptor table, PrintableSectionDto section, PrintableRowDto row, bool grid, PrintLayout layout, bool bold,
        bool borderTop = false)
    {
        for (var index = 0; index < section.Columns.Count; index++)
        {
            var definition = section.Columns[index];
            IContainer cell;
            if (grid)
            {
                // Phase 79 -- Classic's cells meet as one rule: two half-width borders side by side.
                cell = table.Cell().Border(layout == PrintLayout.Classic ? 0.5f : 1).BorderColor(RuleColour).Padding(3);
            }
            else
            {
                // The total's rule goes on the cell before any padding, so it runs unbroken across the columns; then a
                // little room each side (phase 73), or a right-aligned quantity runs into the unit beside it ("2Pcs").
                cell = table.Cell();
                if (borderTop)
                {
                    cell = layout == PrintLayout.Modern
                        ? cell.BorderTop(1.5f).BorderColor(RuleColour).PaddingTop(1)
                        : cell.BorderTop(1).PaddingTop(1);
                }
                else if (layout == PrintLayout.Modern)
                {
                    // Phase 79 -- Modern's rule under every line.
                    cell = cell.BorderBottom(0.5f).BorderColor(FaintRule);
                }

                cell = layout == PrintLayout.Modern
                    ? cell.PaddingVertical(4).PaddingHorizontal(3)
                    : cell.PaddingVertical(2).PaddingHorizontal(3);
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

    // ---- Phase 79: the 80 mm roll ---------------------------------------------------------------

    /// <summary>
    /// Phase 79 -- the till roll: one continuous page 80 mm wide, as long as the bill. Everything a tax invoice must
    /// carry is here, where the vendor's Retail drops the transaction date: the seller's name, address and PAN, the
    /// title in both languages, the number, both statutory dates, the buyer's name, address and PAN, every line, the
    /// totals, the total in words and the copy mark top and bottom.
    ///
    /// <para><b>A line is stacked, not squeezed.</b> A table of seven columns in 72 mm of print wraps every figure, and
    /// dropping the VAT and discount columns (the vendor's answer) leaves an Amount that is not Qty × Rate with
    /// nothing on the paper saying why. So each row prints its first cell on a line of its own, then every other
    /// non-empty cell as "Header value" in small type, with the last cell -- the line's amount -- at the right. That
    /// is generic over any section, which is what keeps this file free of a <c>DocumentType</c>.</para>
    /// </summary>
    private static byte[] RenderRoll(PrintableDocumentDto dto)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.ContinuousSize(RollWidthMm, Unit.Millimetre);
                page.MarginHorizontal(RollMarginMm, Unit.Millimetre);
                page.MarginVertical(5, Unit.Millimetre);
                page.DefaultTextStyle(x => x.FontSize(8));

                page.Content().Column(column =>
                {
                    column.Spacing(2);

                    if (dto.PrintedCopy is { IsCopy: true } copy)
                    {
                        column.Item().Element(item => RenderCopyMark(item, copy, fontSize: 7));
                    }

                    column.Item().AlignCenter().Text(dto.OrganizationName).Bold().FontSize(11);

                    var organizationLines = new List<string>();
                    if (!string.IsNullOrWhiteSpace(dto.OrganizationAddress))
                    {
                        organizationLines.Add(dto.OrganizationAddress);
                    }

                    if (LocationLine(dto) is { } location)
                    {
                        organizationLines.Add(location);
                    }

                    organizationLines.AddRange(ContactItems(dto));
                    foreach (var line in organizationLines)
                    {
                        column.Item().AlignCenter().Text(line);
                    }

                    RollRule(column);

                    column.Item().AlignCenter().Text(dto.Title.ToUpperInvariant()).Bold().FontSize(10);
                    if (dto.TitleNepali is { } nepali)
                    {
                        column.Item().AlignCenter().Text(nepali).FontFamily(PdfFonts.Devanagari).Bold().FontSize(9);
                    }

                    foreach (var (label, value) in DetailPairs(dto))
                    {
                        column.Item().Text(text =>
                        {
                            text.Span($"{label}: ").SemiBold();
                            text.Span(value);
                        });
                    }

                    if (dto.PartyLabel is not null)
                    {
                        RollRule(column);
                        column.Item().Text($"{dto.PartyHeading ?? "For"}:").Bold();
                        column.Item().Text(dto.PartyLabel);
                        foreach (var line in new[] { dto.PartyAddress, dto.PartyPan is { } pan ? $"PAN: {pan}" : null })
                        {
                            if (!string.IsNullOrWhiteSpace(line))
                            {
                                column.Item().Text(line);
                            }
                        }
                    }

                    foreach (var field in dto.CustomFields ?? [])
                    {
                        column.Item().Text(text =>
                        {
                            text.Span($"{field.Label}: ").SemiBold();
                            text.Span(field.Value);
                        });
                    }

                    foreach (var section in dto.Sections)
                    {
                        RollRule(column);
                        RenderRollSection(column, section);
                    }

                    if (dto.Summary.Count > 0)
                    {
                        RollRule(column);
                        foreach (var field in dto.Summary)
                        {
                            column.Item().Row(row => RenderSummaryRow(row, dto, field));
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(dto.AmountInWords))
                    {
                        column.Item().Text(text =>
                        {
                            text.Span("Amount in Words: ").Bold();
                            text.Span(dto.AmountInWords).Italic();
                        });
                    }

                    RenderNotesAndTerms(column, dto, fontSize: 7);

                    RollRule(column);

                    if (!string.IsNullOrWhiteSpace(dto.FooterText))
                    {
                        column.Item().AlignCenter().Text(dto.FooterText);
                    }

                    if (dto.CalendarNote is { } calendarNote)
                    {
                        column.Item().AlignCenter().Text(calendarNote).FontSize(7);
                    }

                    if (dto.PrintedCopy is { } printed)
                    {
                        column.Item().AlignCenter().Text($"Printed by {printed.PrintedBy} on {printed.PrintedAtText}").FontSize(7);

                        if (printed.IsCopy)
                        {
                            column.Item().Element(item => RenderCopyMark(item, printed, fontSize: 7));
                        }
                    }
                });
            });
        }).GeneratePdf();
    }

    private const char NoBreakSpace = '\u00A0';

    private static string Unbroken(string text) => text.Replace(' ', NoBreakSpace);

    private static void RollRule(ColumnDescriptor column) =>
        column.Item().PaddingVertical(1).LineHorizontal(0.5f).LineColor(RuleColour);

    /// <summary>One section on the roll: its title, then each row stacked (see <see cref="RenderRoll"/>), then the
    /// total row as its first and last cells in bold over a rule.</summary>
    private static void RenderRollSection(ColumnDescriptor column, PrintableSectionDto section)
    {
        column.Item().Text(section.Title).Bold();

        var last = section.Columns.Count - 1;
        string Cell(PrintableRowDto row, int index) => index < row.Cells.Count ? row.Cells[index] : string.Empty;

        foreach (var row in section.Rows)
        {
            column.Item().Column(line =>
            {
                line.Item().Text(Cell(row, 0));

                if (last < 1)
                {
                    return;
                }

                line.Item().Row(figures =>
                {
                    figures.RelativeItem().PaddingLeft(6).Text(text =>
                    {
                        text.DefaultTextStyle(x => x.FontSize(7).FontColor(MutedText));
                        var first = true;
                        for (var index = 1; index < last; index++)
                        {
                            var value = Cell(row, index);
                            if (string.IsNullOrWhiteSpace(value))
                            {
                                continue;
                            }

                            // A pair breaks between pairs, never inside one ("Disc %" over "10" read as two things).
                            text.Span(first ? string.Empty : "   ");
                            text.Span($"{Unbroken(section.Columns[index].Header)}{NoBreakSpace}").SemiBold();
                            text.Span(Unbroken(value));
                            first = false;
                        }
                    });

                    figures.ConstantItem(60).AlignRight().AlignBottom().Text(Cell(row, last));
                });
            });
        }

        if (section.TotalRow is { } total)
        {
            column.Item().BorderTop(0.5f).BorderColor(RuleColour).PaddingTop(2).Row(row =>
            {
                row.RelativeItem().Text(Cell(total, 0)).Bold();
                if (last >= 1)
                {
                    row.ConstantItem(90).AlignRight().Text(Cell(total, last)).Bold();
                }
            });
        }
    }
}
