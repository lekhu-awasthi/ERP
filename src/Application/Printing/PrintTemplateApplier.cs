using ErpApp.Application.Printing.Queries.PrintDocument;
using ErpApp.Domain.Configuration;

namespace ErpApp.Application.Printing;

/// <summary>
/// Phase 73 -- what a printing template does to a document, as one pure function over the finished DTO, so a real
/// print and the editor's preview hide exactly the same things. The builders print every element their type offers;
/// this removes what the template hides and carries its layout, footer line and margins through to the renderer.
///
/// <para>A column is hidden by the <see cref="PrintElement"/> it names, and every row and the total row lose the same
/// cell, so a hidden column can never shift the cells after it (the renderer's own short-row rule, phase 27b).</para>
///
/// <para>It never removes a tax document's statutory content, whatever the settings say: the Domain refuses to store
/// such a template, and this re-checks with <see cref="PrintElements.CanHide"/> rather than trusting the row, so a
/// template written by any other door still prints the seller's PAN.</para>
///
/// <para>Phase 79 -- what the layout itself never draws (the roll's logo and signature line) reaches this through
/// <see cref="PrintingTemplateSettings.Shows"/>, under the same <c>CanHide</c> re-check.</para>
/// </summary>
public static class PrintTemplateApplier
{
    public static PrintableDocumentDto Apply(PrintableDocumentDto dto, PrintingTemplateSettings template)
    {
        bool Hides(PrintElement element) =>
            !template.Shows(element) && PrintElements.CanHide(dto.DocumentType, element);

        return dto with
        {
            OrganizationLogo = Hides(PrintElement.OrganizationLogo) ? null : dto.OrganizationLogo,
            OrganizationAddress = Hides(PrintElement.OrganizationAddress) ? null : dto.OrganizationAddress,
            OrganizationPhone = Hides(PrintElement.OrganizationPhone) ? null : dto.OrganizationPhone,
            OrganizationEmail = Hides(PrintElement.OrganizationEmail) ? null : dto.OrganizationEmail,
            OrganizationPan = Hides(PrintElement.OrganizationPan) ? null : dto.OrganizationPan,
            OrganizationWebsite = Hides(PrintElement.OrganizationWebsite) ? null : dto.OrganizationWebsite,
            LocationName = Hides(PrintElement.Location) ? null : dto.LocationName,
            LocationAddress = Hides(PrintElement.Location) ? null : dto.LocationAddress,
            PartyAddress = Hides(PrintElement.PartyAddress) ? null : dto.PartyAddress,
            Reference = Hides(PrintElement.Reference) ? null : dto.Reference,
            Notes = Hides(PrintElement.Notes) ? null : dto.Notes,
            Terms = Hides(PrintElement.Terms) ? null : dto.Terms,
            AmountInWords = Hides(PrintElement.AmountInWords) ? null : dto.AmountInWords,
            ShowSignatureLine = !Hides(PrintElement.SignatureLine),
            Sections = dto.Sections
                .Select(section => WithoutHiddenColumns(WithSerials(section, template.PrintSerialNumbers), Hides))
                .ToList(),
            Layout = template.Layout,
            CompactMargins = template.CompactMargins,
            FooterText = template.FooterText,
            PrintingTemplateName = template.Name,
        };
    }

    /// <summary>Phase 95 -- a line's serial numbers, as a second line under its product when the template prints them
    /// ("SN: a, b"); every layout prints the first cell as written, so none of them changes.</summary>
    private static PrintableSectionDto WithSerials(PrintableSectionDto section, bool print)
    {
        if (section.Rows.All(row => row.SerialNumbers is null))
        {
            return section;
        }

        PrintableRowDto Apply(PrintableRowDto row) =>
            print && row.SerialNumbers is { Count: > 0 } serials && row.Cells.Count > 0
                ? new PrintableRowDto([$"{row.Cells[0]}\nSN: {string.Join(", ", serials)}", .. row.Cells.Skip(1)])
                : new PrintableRowDto(row.Cells);

        return section with { Rows = section.Rows.Select(Apply).ToList() };
    }

    private static PrintableSectionDto WithoutHiddenColumns(PrintableSectionDto section, Func<PrintElement, bool> hides)
    {
        var kept = section.Columns
            .Select((column, index) => (column, index))
            .Where(x => x.column.Element is not { } element || !hides(element))
            .Select(x => x.index)
            .ToList();

        if (kept.Count == section.Columns.Count)
        {
            return section;
        }

        PrintableRowDto Keep(PrintableRowDto row) =>
            new(kept.Select(index => index < row.Cells.Count ? row.Cells[index] : string.Empty).ToList());

        return section with
        {
            Columns = kept.Select(index => section.Columns[index]).ToList(),
            Rows = section.Rows.Select(Keep).ToList(),
            TotalRow = section.TotalRow is { } total ? Keep(total) : null,
        };
    }
}
