using ErpApp.Application.Communications;
using ErpApp.Domain.Configuration;

namespace ErpApp.Application.UnitTests.Communications;

public class EmailMergeResolverTests
{
    private static readonly Dictionary<string, string> Values = new(StringComparer.Ordinal)
    {
        ["ORGANIZATION_NAME"] = "Moonbeam Trading",
        ["DOCUMENT_NO"] = "045",
        ["GRAND_TOTAL"] = "50,850.00",
        ["CURRENCY"] = "NPR",
    };

    [Fact]
    public void Substitutes_the_live_token_syntax()
    {
        var result = EmailMergeResolver.Apply(
            "Invoice From $[ORGANIZATION_NAME]$", Values, EmailTemplateContext.Invoice);

        Assert.Equal("Invoice From Moonbeam Trading", result);
    }

    /// <summary>
    /// The catalogue offers <c>DOCUMENT_NO</c>; the reference product's own Invoice templates write
    /// <c>INVOICE_NO</c>. Accepting both is what lets a body pasted from the reference product
    /// render — see EmailMergeFields for why we offer one spelling and accept two.
    /// </summary>
    [Fact]
    public void Accepts_the_live_per_context_alias_as_well_as_the_offered_token()
    {
        var result = EmailMergeResolver.Apply(
            "Your invoice $[INVOICE_NO]$ / $[DOCUMENT_NO]$", Values, EmailTemplateContext.Invoice);

        Assert.Equal("Your invoice 045 / 045", result);
    }

    /// <summary>An alias belongs to its own context and nowhere else, exactly as live: an Invoice
    /// template's INVOICE_NO must not resolve on a Purchase Order.</summary>
    [Fact]
    public void Does_not_resolve_another_contexts_alias()
    {
        var result = EmailMergeResolver.Apply(
            "$[INVOICE_NO]$", Values, EmailTemplateContext.PurchaseOrder);

        Assert.Equal("$[INVOICE_NO]$", result);
    }

    [Fact]
    public void Resolves_a_multi_word_contexts_alias()
    {
        var result = EmailMergeResolver.Apply(
            "$[PURCHASE_ORDER_NO]$", Values, EmailTemplateContext.PurchaseOrder);

        Assert.Equal("045", result);
    }

    /// <summary>
    /// The decision this pins: an unknown token is left standing rather than blanked. A typo
    /// reaching a customer as `$[TOATL]$` is obviously wrong and gets fixed; the same typo silently
    /// becoming "" produces "Invoice Amount: " and reads as an accounting bug.
    /// </summary>
    [Fact]
    public void Leaves_an_unknown_token_standing_rather_than_blanking_it()
    {
        var result = EmailMergeResolver.Apply("Total: $[TOATL]$", Values, EmailTemplateContext.Invoice);

        Assert.Equal("Total: $[TOATL]$", result);
        Assert.Equal(["TOATL"], EmailMergeResolver.UnresolvedTokens(result));
    }

    [Fact]
    public void Reports_no_unresolved_tokens_for_fully_substituted_text()
    {
        var resolved = EmailMergeResolver.Apply(
            "$[CURRENCY]$ $[GRAND_TOTAL]$", Values, EmailTemplateContext.Invoice);

        Assert.Equal("NPR 50,850.00", resolved);
        Assert.Empty(EmailMergeResolver.UnresolvedTokens(resolved));
    }

    [Fact]
    public void Reports_each_unresolved_token_once_and_ignores_an_unterminated_one()
    {
        var tokens = EmailMergeResolver.UnresolvedTokens("$[A]$ $[A]$ $[B]$ $[UNTERMINATED");

        Assert.Equal(["A", "B"], tokens);
    }

    /// <summary>Live: `NPR 50,850.00` — thousands separators, two decimals, invariant culture so a
    /// server's locale cannot change what a customer reads.</summary>
    [Fact]
    public void Formats_amounts_the_way_the_live_dialog_does()
    {
        Assert.Equal("50,850.00", EmailMergeResolver.FormatAmount(50850m));
        Assert.Equal("0.00", EmailMergeResolver.FormatAmount(0m));
        Assert.Equal("-1,234.50", EmailMergeResolver.FormatAmount(-1234.5m));
    }

    /// <summary>AD, day-first, deliberately not routed through RequestCalendar — an email is read by
    /// a counterparty who may not share the tenant's calendar preference and cannot see which
    /// calendar they are looking at. See EmailMergeResolver's remarks.</summary>
    [Fact]
    public void Formats_dates_as_AD_day_first()
    {
        Assert.Equal("02-09-2026", EmailMergeResolver.FormatDate(new DateOnly(2026, 9, 2)));
        Assert.Equal(string.Empty, EmailMergeResolver.FormatDate(null));
    }

    [Fact]
    public void Every_offered_field_for_every_context_has_a_unique_token_within_that_context()
    {
        foreach (var context in Enum.GetValues<EmailTemplateContext>())
        {
            var fields = EmailMergeFields.For(context);
            var tokens = fields.Select(x => x.Token).ToList();

            Assert.Equal(tokens.Count, tokens.Distinct(StringComparer.Ordinal).Count());
            Assert.All(fields, f => Assert.False(string.IsNullOrWhiteSpace(f.Group)));
            Assert.All(fields, f => Assert.False(string.IsNullOrWhiteSpace(f.Label)));
        }
    }

    /// <summary>The three fixed groups are offered everywhere; the document group only where there
    /// is a document — live, the Contact dialog's templates carry no document fields.</summary>
    [Fact]
    public void Offers_the_document_group_only_for_a_context_that_has_a_document()
    {
        var general = EmailMergeFields.For(EmailTemplateContext.General);
        var invoice = EmailMergeFields.For(EmailTemplateContext.Invoice);

        Assert.DoesNotContain(general, x => x.Token == "GRAND_TOTAL");
        Assert.Contains(invoice, x => x.Token == "GRAND_TOTAL");

        foreach (var group in new[]
                 {
                     EmailMergeFields.OrganizationGroup,
                     EmailMergeFields.ContactGroup,
                     EmailMergeFields.UserGroup,
                 })
        {
            Assert.Contains(general, x => x.Group == group);
            Assert.Contains(invoice, x => x.Group == group);
        }
    }

    /// <summary>The three payment-allocation fields the live Customer Payment template carries, and
    /// which no other context has a meaning for.</summary>
    [Fact]
    public void Offers_payment_fields_only_on_the_two_payment_contexts()
    {
        foreach (var context in Enum.GetValues<EmailTemplateContext>())
        {
            var hasPaymentFields = EmailMergeFields.For(context).Any(x => x.Token == "PAYMENT_MODE");
            var isPaymentContext = context
                is EmailTemplateContext.CustomerPayment or EmailTemplateContext.SupplierPayment;

            Assert.Equal(isPaymentContext, hasPaymentFields);
        }
    }
}
