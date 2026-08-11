using ErpApp.Domain.Common;
using ErpApp.Domain.Configuration;

namespace ErpApp.Domain.UnitTests.Configuration;

public class CustomStatusTests
{
    [Fact]
    public void Create_starts_active_scoped_to_given_document_type()
    {
        var customStatus = CustomStatus.Create(Guid.NewGuid(), "Awaiting Approval", DocumentType.Invoice);

        Assert.Equal("Awaiting Approval", customStatus.Name);
        Assert.Equal(DocumentType.Invoice, customStatus.DocumentType);
        Assert.True(customStatus.IsActive);
    }

    [Fact]
    public void Update_replaces_name_document_type_and_active_flag()
    {
        var customStatus = CustomStatus.Create(Guid.NewGuid(), "Awaiting Approval", DocumentType.Invoice);

        customStatus.Update("Cleared", DocumentType.PurchaseBill, false);

        Assert.Equal("Cleared", customStatus.Name);
        Assert.Equal(DocumentType.PurchaseBill, customStatus.DocumentType);
        Assert.False(customStatus.IsActive);
    }
}
