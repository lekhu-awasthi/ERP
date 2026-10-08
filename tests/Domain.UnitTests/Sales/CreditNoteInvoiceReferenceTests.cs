using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;

namespace ErpApp.Domain.UnitTests.Sales;

/// <summary>
/// Phase 69 -- the invoice a credit note relates to (VAT Rules Rule 20(1)(e)) and the reason on an ERP
/// note. The aggregate checks the reference's shape; whether the named invoice fits the note is the
/// handler's (CreditNoteInvoiceReferences), so these are the backstop.
/// </summary>
public class CreditNoteInvoiceReferenceTests
{
    private static readonly DateOnly Day = new(2026, 10, 8);

    private static CreditNote Standalone() => CreditNote.Create(Guid.NewGuid(), Guid.NewGuid(), Day, null, null, null);

    private static CreditNote Conversion(Guid invoiceId) =>
        CreditNote.Create(Guid.NewGuid(), Guid.NewGuid(), Day, "From Invoice INV0001", DocumentType.Invoice, invoiceId);

    [Fact]
    public void A_picked_invoice_is_what_a_standalone_note_relates_to()
    {
        var invoiceId = Guid.NewGuid();
        var note = Standalone();

        note.SetInvoiceReference(invoiceId, null, null);

        Assert.Equal(invoiceId, note.AgainstInvoiceId);
        Assert.Equal(invoiceId, note.RelatedInvoiceId);
        Assert.False(note.IsConversionFromInvoice);
        Assert.True(note.NamesAnInvoice);
    }

    [Fact]
    public void A_conversion_relates_to_its_source_through_the_referrer_and_stores_no_second_copy()
    {
        var invoiceId = Guid.NewGuid();
        var note = Conversion(invoiceId);

        Assert.True(note.IsConversionFromInvoice);
        Assert.Equal(invoiceId, note.RelatedInvoiceId);
        Assert.Null(note.AgainstInvoiceId);
        Assert.True(note.NamesAnInvoice);
    }

    [Fact]
    public void A_conversion_refuses_a_second_reference_but_may_be_cleared_to_none()
    {
        var note = Conversion(Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(() => note.SetInvoiceReference(Guid.NewGuid(), null, null));
        Assert.Throws<InvalidOperationException>(() => note.SetInvoiceReference(null, "OLD-17", new DateOnly(2025, 1, 1)));

        note.SetInvoiceReference(null, null, null);
        Assert.True(note.NamesAnInvoice);
    }

    [Fact]
    public void A_typed_invoice_needs_its_number_and_its_date_and_is_trimmed()
    {
        var note = Standalone();

        Assert.Throws<InvalidOperationException>(() => note.SetInvoiceReference(null, "OLD-17", null));
        Assert.Throws<InvalidOperationException>(() => note.SetInvoiceReference(null, null, new DateOnly(2025, 1, 1)));
        Assert.Throws<InvalidOperationException>(() => note.SetInvoiceReference(null, "  ", new DateOnly(2025, 1, 1)));

        note.SetInvoiceReference(null, "  OLD-17 ", new DateOnly(2025, 1, 1));

        Assert.Equal("OLD-17", note.AgainstInvoiceNumber);
        Assert.Equal(new DateOnly(2025, 1, 1), note.AgainstInvoiceDate);
        Assert.Null(note.RelatedInvoiceId);
        Assert.True(note.NamesAnInvoice);
    }

    [Fact]
    public void A_note_names_a_picked_invoice_or_a_typed_one_never_both()
    {
        var note = Standalone();

        Assert.Throws<InvalidOperationException>(
            () => note.SetInvoiceReference(Guid.NewGuid(), "OLD-17", new DateOnly(2025, 1, 1)));
        Assert.Throws<InvalidOperationException>(() => note.SetInvoiceReference(Guid.Empty, null, null));
    }

    [Fact]
    public void A_typed_number_longer_than_the_column_is_refused()
    {
        var note = Standalone();

        Assert.Throws<InvalidOperationException>(() => note.SetInvoiceReference(
            null, new string('9', CreditNote.MaxAgainstInvoiceNumberLength + 1), new DateOnly(2025, 1, 1)));
    }

    [Fact]
    public void The_reference_is_draft_only()
    {
        var note = Standalone();
        note.AddLine(Guid.NewGuid(), 1, 100m, VatRate.NoVat, 0, null, 1m);
        note.Approve(Guid.NewGuid(), "CN0001");

        Assert.Throws<InvalidOperationException>(() => note.SetInvoiceReference(Guid.NewGuid(), null, null));
        Assert.Throws<InvalidOperationException>(() => note.SetReason("Late delivery"));
    }

    [Fact]
    public void A_VAT_registered_seller_cannot_issue_a_note_naming_no_invoice_and_anyone_else_can()
    {
        var note = Standalone();

        var refused = Assert.Throws<InvalidOperationException>(() => note.EnsureNamesInvoiceFor(sellerIsVatRegistered: true));
        Assert.Contains("Rule 20", refused.Message);

        note.EnsureNamesInvoiceFor(sellerIsVatRegistered: false);

        note.SetInvoiceReference(null, "OLD-17", new DateOnly(2025, 1, 1));
        note.EnsureNamesInvoiceFor(sellerIsVatRegistered: true);
    }

    [Fact]
    public void An_ERP_reason_is_optional_trimmed_and_capped()
    {
        var note = Standalone();

        note.SetReason("  Price agreed lower after delivery  ");
        Assert.Equal("Price agreed lower after delivery", note.Reason);

        note.SetReason("   ");
        Assert.Null(note.Reason);

        Assert.Throws<InvalidOperationException>(() => note.SetReason(new string('x', CreditNote.MaxReasonLength + 1)));
    }

    [Fact]
    public void A_till_refunds_reason_is_set_when_it_is_made_and_not_through_the_ERP_setter()
    {
        var refund = CreditNote.CreatePosRefund(
            Guid.NewGuid(), Guid.NewGuid(), Day, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "INV0001", 0, "Wrong order");

        Assert.Throws<InvalidOperationException>(() => refund.SetReason("Something else"));
        Assert.Equal("Wrong order", refund.Reason);
        Assert.True(refund.IsConversionFromInvoice);
    }
}
