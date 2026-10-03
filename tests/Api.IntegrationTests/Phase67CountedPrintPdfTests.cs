using ErpApp.Api.IntegrationTests.TestSupport;
using ErpApp.Api.Printing;
using ErpApp.Application.Printing.Commands.IssueDocumentPrint;
using ErpApp.Application.Printing.Queries.PrintDocument;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using MediatR;

namespace ErpApp.Api.IntegrationTests;

/// <summary>
/// Phase 67 -- the counted print asserted <b>in the PDF's text</b>: the heading in English and Nepali,
/// the copy mark top and bottom, and who printed it. Like phase 39's tests this needs no host, database
/// or Docker; <see cref="DocumentPdfRenderer"/> is a function from a DTO to bytes, and
/// <see cref="PdfText"/> reads the bytes back the way a viewer's copy-and-paste would.
/// </summary>
public class Phase67CountedPrintPdfTests
{
    static Phase67CountedPrintPdfTests()
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

        // A glyph the font cannot draw then throws instead of printing a box, which is the failure the
        // bundled Devanagari font exists to prevent.
        QuestPDF.Settings.CheckIfAllTextGlyphsAreAvailable = true;
    }

    private static PrintableDocumentDto Invoice(PrintedCopyDto? copy, string title = "Tax Invoice", string? nepali = "कर बीजक") =>
        new(
            DocumentType.Invoice,
            title,
            "INV0001",
            "2026-10-03",
            Reference: null,
            OrganizationName: "Moonbeam Trading",
            OrganizationAddress: "Manbhawan",
            OrganizationPhone: null,
            OrganizationEmail: null,
            OrganizationPan: "031564579",
            OrganizationWebsite: null,
            OrganizationLogo: null,
            PartyHeading: "Bill To",
            PartyLabel: "C0001 — Adhi Kishan",
            PartyAddress: "KTM",
            PrintingTemplateName: "Default",
            HeaderFields: [],
            Sections: [],
            Summary: [new PrintableFieldDto("Grand Total", "1,695.00", Emphasise: true)],
            Notes: null,
            Terms: null,
            CalendarNote: null,
            TitleNepali: nepali,
            PrintedCopy: copy);

    [Fact]
    public void The_original_carries_its_heading_in_both_languages_and_no_copy_mark()
    {
        var text = PdfText.Extract(DocumentPdfRenderer.Render(Invoice(new PrintedCopyDto(1, "Asha Accountant", "2026-10-03 09:15"))));

        Assert.Contains("TAX INVOICE", text, StringComparison.Ordinal);
        Assert.Contains("कर", text, StringComparison.Ordinal);
        Assert.Contains("Printed by Asha Accountant on 2026-10-03 09:15", text, StringComparison.Ordinal);
        Assert.DoesNotContain("COPY OF ORIGINAL", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_copy_is_marked_with_its_count_at_the_top_and_the_bottom()
    {
        var text = PdfText.Extract(DocumentPdfRenderer.Render(Invoice(new PrintedCopyDto(3, "Asha Accountant", "2026-10-03 09:20"))));

        var marks = text.Split("COPY OF ORIGINAL · printed 3 times", StringSplitOptions.None).Length - 1;
        Assert.Equal(2, marks);
    }

    [Fact]
    public void An_uncounted_document_prints_exactly_as_before()
    {
        var text = PdfText.Extract(DocumentPdfRenderer.Render(Invoice(copy: null, title: "Quotation", nepali: null)));

        Assert.Contains("QUOTATION", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Printed by", text, StringComparison.Ordinal);
        Assert.DoesNotContain("COPY OF ORIGINAL", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("संक्षिप्त कर बीजक")]
    [InlineData("बीजक")]
    [InlineData("क्रेडिट नोट")]
    public void Every_nepali_heading_renders_with_the_bundled_font(string nepali)
    {
        // CheckIfAllTextGlyphsAreAvailable makes a missing glyph throw, so rendering is the assertion.
        var pdf = DocumentPdfRenderer.Render(Invoice(new PrintedCopyDto(1, "Asha", "2026-10-03 09:15"), nepali: nepali));

        Assert.Contains("NotoSansDevanagari", System.Text.Encoding.Latin1.GetString(pdf), StringComparison.Ordinal);
    }

    // ---- The email path ----------------------------------------------------------------------

    [Theory]
    [InlineData(DocumentType.Invoice)]
    [InlineData(DocumentType.CreditNote)]
    public async Task An_emailed_invoice_or_credit_note_is_a_counted_print(DocumentType type)
    {
        var sender = new RecordingSender(Invoice(new PrintedCopyDto(1, "Asha", "2026-10-03 09:15")));

        await new MediatorDocumentPdfRenderer(sender).RenderAsync(Guid.NewGuid(), type, Guid.NewGuid());

        var issued = Assert.IsType<IssueDocumentPrintCommand>(Assert.Single(sender.Sent));
        Assert.Equal(PrintMedium.Email, issued.Medium);
        Assert.Equal(type, issued.DocumentType);
    }

    [Fact]
    public async Task Any_other_emailed_document_renders_as_before()
    {
        var sender = new RecordingSender(Invoice(copy: null, title: "Quotation", nepali: null));

        await new MediatorDocumentPdfRenderer(sender).RenderAsync(Guid.NewGuid(), DocumentType.Quotation, Guid.NewGuid());

        Assert.IsType<PrintDocumentQuery>(Assert.Single(sender.Sent));
    }

    private sealed class RecordingSender(PrintableDocumentDto dto) : ISender
    {
        public List<object> Sent { get; } = [];

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            Sent.Add(request);
            return Task.FromResult((TResponse)(object)dto);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
