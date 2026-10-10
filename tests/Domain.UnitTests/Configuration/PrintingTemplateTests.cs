using ErpApp.Domain.Common;
using ErpApp.Domain.Configuration;

namespace ErpApp.Domain.UnitTests.Configuration;

public class PrintingTemplateTests
{
    [Fact]
    public void Create_starts_active_with_the_given_default_flag()
    {
        var template = PrintingTemplate.Create(Guid.NewGuid(), "Standard", DocumentType.Invoice, isDefault: true);

        Assert.Equal("Standard", template.Name);
        Assert.Equal(DocumentType.Invoice, template.DocumentType);
        Assert.True(template.IsDefault);
        Assert.True(template.IsActive);
    }

    [Fact]
    public void Update_replaces_name_document_type_and_active_flag()
    {
        var template = PrintingTemplate.Create(Guid.NewGuid(), "Standard", DocumentType.Invoice, isDefault: true);

        template.Update("Modern", DocumentType.Invoice, false);

        Assert.Equal("Modern", template.Name);
        Assert.Equal(DocumentType.Invoice, template.DocumentType);
        Assert.False(template.IsActive);
    }

    [Fact]
    public void Update_clears_default_when_moved_to_a_different_document_type()
    {
        var template = PrintingTemplate.Create(Guid.NewGuid(), "Standard", DocumentType.Invoice, isDefault: true);

        template.Update("Standard", DocumentType.Quotation, true);

        Assert.False(template.IsDefault);
    }

    [Fact]
    public void Update_keeps_default_when_document_type_is_unchanged()
    {
        var template = PrintingTemplate.Create(Guid.NewGuid(), "Standard", DocumentType.Invoice, isDefault: true);

        template.Update("Standard Renamed", DocumentType.Invoice, true);

        Assert.True(template.IsDefault);
    }

    [Fact]
    public void MarkAsDefault_and_ClearDefault_toggle_the_flag()
    {
        var template = PrintingTemplate.Create(Guid.NewGuid(), "Standard", DocumentType.Invoice, isDefault: false);

        template.MarkAsDefault();
        Assert.True(template.IsDefault);

        template.ClearDefault();
        Assert.False(template.IsDefault);
    }

    // ---- Phase 73: the layout settings ------------------------------------------------------------

    [Fact]
    public void A_new_template_prints_standard_with_nothing_hidden()
    {
        var template = PrintingTemplate.Create(Guid.NewGuid(), "Standard", DocumentType.Invoice, isDefault: true);

        Assert.Equal(PrintLayout.Standard, template.Layout);
        Assert.Empty(template.HiddenElements);
        Assert.Empty(template.PrintedCustomFieldIds);
        Assert.Null(template.FooterText);
        Assert.False(template.CompactMargins);
        Assert.Equal(PrintLayout.Standard, PrintingTemplateSettings.Default.Layout);
        Assert.Empty(PrintingTemplateSettings.Default.HiddenElements);
    }

    [Fact]
    public void UpdateLayout_stores_the_settings_deduplicated_and_trims_the_footer()
    {
        var template = PrintingTemplate.Create(Guid.NewGuid(), "Plain", DocumentType.Quotation, isDefault: true);
        var field = Guid.NewGuid();

        template.UpdateLayout(
            PrintLayout.Traditional,
            [PrintElement.VatColumn, PrintElement.OrganizationPan, PrintElement.VatColumn],
            [field, field],
            "  Bank: NIC Asia 001  ",
            compactMargins: true);

        Assert.Equal(PrintLayout.Traditional, template.Layout);
        Assert.Equal([PrintElement.OrganizationPan, PrintElement.VatColumn], template.HiddenElements);
        Assert.Equal([field], template.PrintedCustomFieldIds);
        Assert.Equal("Bank: NIC Asia 001", template.FooterText);
        Assert.True(template.CompactMargins);
        Assert.False(template.Settings.Shows(PrintElement.VatColumn));
        Assert.True(template.Settings.Shows(PrintElement.Unit));
    }

    [Fact]
    public void UpdateLayout_reads_a_blank_footer_as_none()
    {
        var template = PrintingTemplate.Create(Guid.NewGuid(), "Plain", DocumentType.Quotation, isDefault: true);

        template.UpdateLayout(PrintLayout.Minimal, [], [], "   ", compactMargins: false);

        Assert.Null(template.FooterText);
    }

    [Theory]
    [InlineData(DocumentType.Invoice, PrintElement.OrganizationPan)]
    [InlineData(DocumentType.Invoice, PrintElement.OrganizationAddress)]
    [InlineData(DocumentType.CreditNote, PrintElement.PartyAddress)]
    [InlineData(DocumentType.DebitNote, PrintElement.OrganizationPan)]
    public void UpdateLayout_refuses_to_hide_a_tax_document_statutory_element(DocumentType type, PrintElement element)
    {
        var template = PrintingTemplate.Create(Guid.NewGuid(), "Plain", type, isDefault: true);

        var refused = Assert.Throws<InvalidOperationException>(
            () => template.UpdateLayout(PrintLayout.Standard, [element], [], null, false));

        Assert.Contains("cannot be hidden", refused.Message);
        Assert.Empty(template.HiddenElements);
    }

    [Fact]
    public void UpdateLayout_refuses_an_element_the_type_does_not_print()
    {
        var template = PrintingTemplate.Create(Guid.NewGuid(), "Plain", DocumentType.JournalVoucher, isDefault: true);

        var refused = Assert.Throws<InvalidOperationException>(
            () => template.UpdateLayout(PrintLayout.Standard, [PrintElement.VatColumn], [], null, false));

        Assert.Contains("does not print", refused.Message);
    }

    [Fact]
    public void UpdateLayout_refuses_a_footer_over_the_limit()
    {
        var template = PrintingTemplate.Create(Guid.NewGuid(), "Plain", DocumentType.Quotation, isDefault: true);

        Assert.Throws<InvalidOperationException>(() => template.UpdateLayout(
            PrintLayout.Standard, [], [], new string('x', PrintingTemplate.FooterTextMaxLength + 1), false));
    }

    [Fact]
    public void UpdateLayout_refuses_an_undefined_layout()
    {
        var template = PrintingTemplate.Create(Guid.NewGuid(), "Plain", DocumentType.Quotation, isDefault: true);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => template.UpdateLayout((PrintLayout)9, [], [], null, false));
    }

    [Fact]
    public void Moving_a_template_to_a_tax_document_drops_what_that_type_may_not_hide()
    {
        var template = PrintingTemplate.Create(Guid.NewGuid(), "Plain", DocumentType.Quotation, isDefault: true);
        template.UpdateLayout(
            PrintLayout.Minimal, [PrintElement.OrganizationPan, PrintElement.VatColumn], [Guid.NewGuid()], "Thanks", true);

        template.Update("Plain", DocumentType.Invoice, true);

        Assert.Equal([PrintElement.VatColumn], template.HiddenElements);
        Assert.Empty(template.PrintedCustomFieldIds);
        Assert.Equal(PrintLayout.Minimal, template.Layout);
        Assert.Equal("Thanks", template.FooterText);
    }

    [Fact]
    public void Renaming_a_template_keeps_its_settings()
    {
        var template = PrintingTemplate.Create(Guid.NewGuid(), "Plain", DocumentType.Quotation, isDefault: true);
        var field = Guid.NewGuid();
        template.UpdateLayout(PrintLayout.Minimal, [PrintElement.OrganizationPan], [field], null, false);

        template.Update("Renamed", DocumentType.Quotation, true);

        Assert.Equal([PrintElement.OrganizationPan], template.HiddenElements);
        Assert.Equal([field], template.PrintedCustomFieldIds);
    }
}
