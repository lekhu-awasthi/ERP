using ErpApp.Domain.Common;
using ErpApp.Domain.Configuration;

namespace ErpApp.Domain.UnitTests.Configuration;

/// <summary>Phase 79 -- which layouts each type is offered, and that no layout is a way round a statutory element.</summary>
public class PrintLayoutsTests
{
    [Fact]
    public void Every_printable_type_is_offered_every_a4_layout()
    {
        foreach (var type in DocumentMechanisms.Printable)
        {
            foreach (var layout in PrintLayouts.A4)
            {
                Assert.True(PrintLayouts.IsOffered(layout, type), $"{type} is not offered {layout}.");
            }
        }
    }

    [Fact]
    public void The_roll_is_offered_to_invoice_credit_note_sales_order_and_the_money_receipt_only()
    {
        var offered = DocumentMechanisms.Printable.Where(type => PrintLayouts.IsOffered(PrintLayout.Retail, type)).ToList();

        // Phase 98 -- the money receipt (a Payment) joined the vendor's three.
        Assert.Equal(
            [DocumentType.SalesOrder, DocumentType.Invoice, DocumentType.CreditNote, DocumentType.Payment],
            offered.OrderBy(x => x).ToList());
    }

    [Fact]
    public void Every_layout_is_offered_somewhere()
    {
        foreach (var layout in Enum.GetValues<PrintLayout>())
        {
            Assert.Contains(DocumentMechanisms.Printable, type => PrintLayouts.IsOffered(layout, type));
        }
    }

    [Fact]
    public void A_type_with_no_printable_record_is_offered_nothing()
    {
        var notPrintable = Enum.GetValues<DocumentType>().First(x => !DocumentMechanisms.Printable.Contains(x));

        Assert.Empty(PrintLayouts.OfferedFor(notPrintable));
    }

    [Fact]
    public void No_layout_omits_an_element_a_type_it_is_offered_to_must_print()
    {
        foreach (var type in DocumentMechanisms.Printable)
        {
            foreach (var layout in PrintLayouts.OfferedFor(type))
            {
                Assert.Empty(PrintLayouts.Omits(layout).Intersect(PrintElements.Locked(type)));
            }
        }
    }

    [Fact]
    public void Settings_do_not_show_what_their_layout_omits()
    {
        var roll = PrintingTemplateSettings.Default with { Layout = PrintLayout.Retail };

        Assert.False(roll.Shows(PrintElement.OrganizationLogo));
        Assert.False(roll.Shows(PrintElement.SignatureLine));
        Assert.True(roll.Shows(PrintElement.AmountInWords));
        Assert.True(PrintingTemplateSettings.Default.Shows(PrintElement.SignatureLine));
    }

    [Fact]
    public void UpdateLayout_refuses_the_roll_on_a_type_not_offered_it()
    {
        var template = PrintingTemplate.Create(Guid.NewGuid(), "Quote", DocumentType.Quotation, isDefault: true);

        Assert.Throws<InvalidOperationException>(() => template.UpdateLayout(PrintLayout.Retail, [], [], null, false));
        Assert.Equal(PrintLayout.Standard, template.Layout);
    }

    [Theory]
    [InlineData(PrintLayout.Modern)]
    [InlineData(PrintLayout.Classic)]
    [InlineData(PrintLayout.Retail)]
    public void UpdateLayout_stores_a_new_layout_on_an_invoice(PrintLayout layout)
    {
        var template = PrintingTemplate.Create(Guid.NewGuid(), "Bill", DocumentType.Invoice, isDefault: true);

        template.UpdateLayout(layout, [], [], null, false);

        Assert.Equal(layout, template.Layout);
    }

    [Fact]
    public void Moving_a_roll_template_to_a_type_not_offered_it_falls_back_to_standard()
    {
        var template = PrintingTemplate.Create(Guid.NewGuid(), "Roll", DocumentType.Invoice, isDefault: true);
        template.UpdateLayout(PrintLayout.Retail, [], [], null, false);

        template.Update("Roll", DocumentType.PurchaseBill, true);

        Assert.Equal(PrintLayout.Standard, template.Layout);
    }

    [Fact]
    public void Moving_a_roll_template_between_types_offered_it_keeps_the_roll()
    {
        var template = PrintingTemplate.Create(Guid.NewGuid(), "Roll", DocumentType.Invoice, isDefault: true);
        template.UpdateLayout(PrintLayout.Retail, [], [], null, false);

        template.Update("Roll", DocumentType.CreditNote, true);

        Assert.Equal(PrintLayout.Retail, template.Layout);
    }
}
