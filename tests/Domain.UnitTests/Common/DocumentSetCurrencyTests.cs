using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;

namespace ErpApp.Domain.UnitTests.Common;

/// <summary>Phase 28. SetCurrency is identical on all eleven transactional aggregates (one
/// generated method body calling the shared <see cref="ExchangeRates.Validate"/>), so Invoice is
/// the representative case here, exactly as it is for the phase-16b discount formula.</summary>
public class DocumentSetCurrencyTests
{
    [Fact]
    public void A_new_document_is_in_the_base_currency_at_rate_one()
    {
        var invoice = NewInvoice();

        Assert.Equal(CurrencyCatalog.BaseCode, invoice.CurrencyCode);
        Assert.Equal(ExchangeRates.BaseRate, invoice.ExchangeRate);
    }

    [Fact]
    public void SetCurrency_stores_the_normalised_pair()
    {
        var invoice = NewInvoice();

        invoice.SetCurrency("usd", 133.5m);

        Assert.Equal("USD", invoice.CurrencyCode);
        Assert.Equal(133.5m, invoice.ExchangeRate);
    }

    [Fact]
    public void SetCurrency_with_nulls_returns_the_document_to_the_base_pair()
    {
        var invoice = NewInvoice();
        invoice.SetCurrency("USD", 133m);

        invoice.SetCurrency(null, null);

        Assert.Equal(CurrencyCatalog.BaseCode, invoice.CurrencyCode);
        Assert.Equal(ExchangeRates.BaseRate, invoice.ExchangeRate);
    }

    [Fact]
    public void An_approved_document_cannot_have_its_rate_changed()
    {
        // The amounts are already posted to the general ledger at the old rate, so a later change
        // would silently invalidate the posting.
        var invoice = NewInvoice();
        invoice.AddLine(Guid.NewGuid(), 1m, 100m, Domain.Catalog.VatRate.NoVat, discountPct: 0);
        invoice.Approve(Guid.NewGuid(), "INV-1");

        Assert.Throws<InvalidOperationException>(() => invoice.SetCurrency("USD", 133m));
    }

    private static Invoice NewInvoice() => Invoice.Create(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow), null, null, null);
}
