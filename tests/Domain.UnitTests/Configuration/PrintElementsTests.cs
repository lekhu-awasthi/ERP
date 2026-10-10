using ErpApp.Domain.Common;
using ErpApp.Domain.Configuration;

namespace ErpApp.Domain.UnitTests.Configuration;

/// <summary>Phase 73 -- which elements each printable type offers a template, and which it locks.</summary>
public class PrintElementsTests
{
    [Fact]
    public void Every_printable_type_offers_the_organization_block_and_the_signature_line()
    {
        foreach (var type in DocumentMechanisms.Printable)
        {
            var elements = PrintElements.For(type);
            Assert.Contains(PrintElement.OrganizationLogo, elements);
            Assert.Contains(PrintElement.OrganizationPan, elements);
            Assert.Contains(PrintElement.SignatureLine, elements);
        }
    }

    [Fact]
    public void A_type_with_no_printable_record_offers_nothing()
    {
        Assert.Empty(PrintElements.For(DocumentType.OpeningBalance));
        Assert.Empty(PrintElements.Locked(DocumentType.OpeningBalance));
    }

    [Fact]
    public void The_product_table_columns_belong_to_the_nine_product_line_types_only()
    {
        foreach (var type in DocumentMechanisms.Printable)
        {
            var productLine = PrintElements.ProductLineTypes.Contains(type);
            Assert.Equal(productLine, PrintElements.For(type).Contains(PrintElement.Unit));
            Assert.Equal(productLine, PrintElements.For(type).Contains(PrintElement.VatColumn));
            Assert.Equal(productLine, PrintElements.For(type).Contains(PrintElement.HsCode));
        }

        Assert.Equal(9, PrintElements.ProductLineTypes.Count);
    }

    [Fact]
    public void The_item_code_is_offered_wherever_a_product_is_named()
    {
        Assert.Contains(PrintElement.ItemCode, PrintElements.For(DocumentType.WarehouseTransfer));
        Assert.Contains(PrintElement.ItemCode, PrintElements.For(DocumentType.ProductionJournal));
        Assert.DoesNotContain(PrintElement.ItemCode, PrintElements.For(DocumentType.JournalVoucher));
        Assert.DoesNotContain(PrintElement.ItemCode, PrintElements.For(DocumentType.Payment));
    }

    [Fact]
    public void Only_the_three_tax_documents_lock_anything()
    {
        foreach (var type in DocumentMechanisms.Printable)
        {
            var locked = PrintElements.Locked(type);
            if (PrintElements.IsTaxDocument(type))
            {
                Assert.Equal(
                    [PrintElement.OrganizationAddress, PrintElement.OrganizationPan, PrintElement.PartyAddress],
                    locked.OrderBy(x => x));
            }
            else
            {
                Assert.Empty(locked);
            }
        }

        Assert.Equal([DocumentType.Invoice, DocumentType.CreditNote, DocumentType.DebitNote], PrintElements.TaxDocumentTypes);
    }

    [Fact]
    public void CanHide_needs_the_element_printed_and_unlocked()
    {
        Assert.True(PrintElements.CanHide(DocumentType.Quotation, PrintElement.OrganizationPan));
        Assert.False(PrintElements.CanHide(DocumentType.Invoice, PrintElement.OrganizationPan));
        Assert.True(PrintElements.CanHide(DocumentType.Invoice, PrintElement.VatColumn));
        Assert.False(PrintElements.CanHide(DocumentType.CashTransfer, PrintElement.Terms));
    }

    [Fact]
    public void Amount_in_words_is_offered_where_a_money_total_prints()
    {
        Assert.Contains(PrintElement.AmountInWords, PrintElements.For(DocumentType.Invoice));
        Assert.Contains(PrintElement.AmountInWords, PrintElements.For(DocumentType.Payment));
        Assert.Contains(PrintElement.AmountInWords, PrintElements.For(DocumentType.Expense));
        Assert.DoesNotContain(PrintElement.AmountInWords, PrintElements.For(DocumentType.JournalVoucher));
    }
}
