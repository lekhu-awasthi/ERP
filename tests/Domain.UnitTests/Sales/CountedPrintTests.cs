using ErpApp.Domain.Catalog;
using ErpApp.Domain.Sales;

namespace ErpApp.Domain.UnitTests.Sales;

/// <summary>
/// Phase 67 -- the print rows now count the ERP's PDF and email as well as the till's receipt
/// (phase-67-status.md Decisions B and D). The receipt stays till-only; the statutory rule does not.
/// </summary>
public class CountedPrintTests
{
    private static readonly DateOnly Day = new(2026, 10, 3);

    private static Invoice ErpInvoice(bool approve = true)
    {
        var invoice = Invoice.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Day, null, null, null, Day);
        invoice.AddLine(Guid.NewGuid(), 1, 100m, VatRate.NoVat, 0, null, 1m);
        if (approve)
        {
            invoice.Approve(Guid.NewGuid(), "INV0001");
        }

        return invoice;
    }

    private static CreditNote ErpCreditNote(bool approve = true)
    {
        var note = CreditNote.Create(Guid.NewGuid(), Guid.NewGuid(), Day, null, null, null);
        note.AddLine(Guid.NewGuid(), 1, 100m, VatRate.NoVat, 0, null, 1m);
        if (approve)
        {
            note.Approve(Guid.NewGuid(), "CN0001");
        }

        return note;
    }

    [Theory]
    [InlineData(PrintMedium.Pdf)]
    [InlineData(PrintMedium.Email)]
    public void An_erp_invoice_is_counted_by_the_pdf_and_by_email(PrintMedium medium)
    {
        var print = InvoicePrint.Record(ErpInvoice(), 2, Guid.NewGuid(), DateTimeOffset.UtcNow, medium);

        Assert.Equal(3, print.PrintNumber);
        Assert.True(print.IsCopy);
        Assert.Equal(medium, print.Medium);
    }

    [Theory]
    [InlineData(PrintMedium.Pdf)]
    [InlineData(PrintMedium.Email)]
    public void An_erp_credit_note_is_counted_by_the_pdf_and_by_email(PrintMedium medium)
    {
        var print = CreditNotePrint.Record(ErpCreditNote(), 0, Guid.NewGuid(), DateTimeOffset.UtcNow, medium);

        Assert.Equal(1, print.PrintNumber);
        Assert.False(print.IsCopy);
        Assert.Equal(medium, print.Medium);
    }

    [Fact]
    public void A_draft_has_no_number_and_is_never_printed()
    {
        var invoice = Assert.Throws<InvalidOperationException>(() =>
            InvoicePrint.Record(ErpInvoice(approve: false), 0, Guid.NewGuid(), DateTimeOffset.UtcNow, PrintMedium.Pdf));
        Assert.Contains("draft", invoice.Message, StringComparison.Ordinal);

        Assert.Throws<InvalidOperationException>(() =>
            CreditNotePrint.Record(ErpCreditNote(approve: false), 0, Guid.NewGuid(), DateTimeOffset.UtcNow, PrintMedium.Pdf));
    }

    [Fact]
    public void A_voided_document_is_never_printed()
    {
        var invoice = ErpInvoice();
        invoice.Void(Guid.NewGuid());
        Assert.Throws<InvalidOperationException>(() =>
            InvoicePrint.Record(invoice, 0, Guid.NewGuid(), DateTimeOffset.UtcNow, PrintMedium.Email));

        var note = ErpCreditNote();
        note.Void(Guid.NewGuid());
        Assert.Throws<InvalidOperationException>(() =>
            CreditNotePrint.Record(note, 0, Guid.NewGuid(), DateTimeOffset.UtcNow, PrintMedium.Pdf));
    }

    [Fact]
    public void The_till_receipt_is_still_the_tills_alone()
    {
        Assert.Throws<InvalidOperationException>(() =>
            InvoicePrint.Record(ErpInvoice(), 0, Guid.NewGuid(), DateTimeOffset.UtcNow, PrintMedium.TillReceipt));
        Assert.Throws<InvalidOperationException>(() =>
            CreditNotePrint.Record(ErpCreditNote(), 0, Guid.NewGuid(), DateTimeOffset.UtcNow, PrintMedium.TillReceipt));
    }

    [Fact]
    public void Every_row_before_phase_67_reads_as_a_till_receipt()
    {
        // The NOT NULL column's default is the enum's zero, which is what makes the migration need no
        // backfill: every existing row was written by the till.
        Assert.Equal(0, (int)PrintMedium.TillReceipt);
    }
}
