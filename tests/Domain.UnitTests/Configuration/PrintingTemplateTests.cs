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
}
