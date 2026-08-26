using ErpApp.Domain.Catalog;
using ErpApp.Domain.Sales;

namespace ErpApp.Domain.UnitTests.Sales;

/// <summary>Phase 20b: SetCustomStatus is orthogonal to the Draft/Approved/Void/Converted
/// lifecycle -- live-confirmed against the real Tigg tenant to be settable regardless of Status,
/// unlike UpdateHeader/AddLine/ClearLines which all require EnsureDraft.</summary>
public class QuotationTests
{
    [Fact]
    public void SetCustomStatus_is_allowed_on_a_draft_quotation()
    {
        var quotation = Quotation.Create(Guid.NewGuid(), Guid.NewGuid(), Today(), null, null);
        var customStatusId = Guid.NewGuid();

        quotation.SetCustomStatus(customStatusId);

        Assert.Equal(customStatusId, quotation.CustomStatusId);
        Assert.Equal(QuotationStatus.Draft, quotation.Status);
    }

    [Fact]
    public void SetCustomStatus_is_allowed_on_an_approved_quotation()
    {
        var quotation = Quotation.Create(Guid.NewGuid(), Guid.NewGuid(), Today(), null, null);
        quotation.AddLine(Guid.NewGuid(), 1m, 100m, VatRate.NoVat, 0);
        quotation.Approve(Guid.NewGuid(), "Q0001");
        var customStatusId = Guid.NewGuid();

        quotation.SetCustomStatus(customStatusId);

        Assert.Equal(customStatusId, quotation.CustomStatusId);
        Assert.Equal(QuotationStatus.Approved, quotation.Status);
    }

    [Fact]
    public void SetCustomStatus_null_clears_a_previously_set_status()
    {
        var quotation = Quotation.Create(Guid.NewGuid(), Guid.NewGuid(), Today(), null, null);
        quotation.SetCustomStatus(Guid.NewGuid());

        quotation.SetCustomStatus(null);

        Assert.Null(quotation.CustomStatusId);
    }

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);
}
