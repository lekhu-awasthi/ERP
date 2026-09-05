using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Configuration;
using ErpApp.Domain.Payments;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Communications;

/// <summary>One document's merge values, plus the contact it is addressed to.</summary>
/// <param name="ContactId">Who the document is for — the source of the Contact group's values and
/// of the To field's suggestions.</param>
public sealed record EmailDocumentFacts(
    Guid ContactId,
    string Code,
    DateOnly Date,
    string? Reference,
    string CurrencyCode,
    decimal ExchangeRate,
    decimal SubTotal,
    decimal DiscountAmount,
    decimal NonTaxableTotal,
    decimal TaxableTotal,
    decimal VatAmount,
    decimal GrandTotal,
    string? PaymentMode = null,
    decimal? PaymentAmount = null);

/// <summary>
/// Assembles the <c>$[TOKEN]$</c> → value map for one send. The shared reader every email goes
/// through, so a template's <c>$[GRAND_TOTAL]$</c> cannot disagree with the same document's printed
/// total — phase-26b's shared-reader lesson applied to a third mechanism.
///
/// <para><b>Only the six emailable document types are loaded here</b>
/// (<see cref="DocumentMechanisms.Emailable"/>), not all fifteen. This is deliberately <i>not</i>
/// built on <c>PrintDocumentQueryHandler</c>'s fifteen loaders: that handler produces pre-formatted
/// strings for a page layout — titled sections, column widths, a calendar note — and reusing it
/// would mean parsing a print DTO back into numbers to format them differently. The overlap is the
/// SQL, and the SQL is four lines per type.</para>
/// </summary>
public static class EmailMergeValueReader
{
    /// <summary>
    /// Values for the three fixed groups. <paramref name="contactId"/> is null for a send that is
    /// not about a contact, in which case the Contact group resolves to empty strings.
    /// </summary>
    public static async Task<Dictionary<string, string>> ReadFixedAsync(
        IAppDbContext db, Guid organizationId, Guid actingUserId, Guid? contactId, CancellationToken ct)
    {
        var organization = await db.Organizations.SingleAsync(x => x.Id == organizationId, ct);
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == actingUserId, ct);

        var contact = contactId is null
            ? null
            : await db.Contacts.SingleOrDefaultAsync(
                x => x.Id == contactId.Value && x.OrganizationId == organizationId, ct);

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ORGANIZATION_NAME"] = organization.Name,

            // The live "Display Name" has no separate column here; WorkspaceName is the
            // organization's other name and is what the product shows in its own chrome.
            ["ORGANIZATION_DISPLAY_NAME"] = organization.WorkspaceName,
            ["ORGANIZATION_ADDRESS"] = organization.Address ?? string.Empty,
            ["ORGANIZATION_PHONE"] = organization.Phone ?? string.Empty,
            ["ORGANIZATION_EMAIL"] = organization.Email ?? string.Empty,
            ["ORGANIZATION_WEBSITE"] = organization.Website ?? string.Empty,
            ["ORGANIZATION_PAN"] = organization.PanNumber ?? string.Empty,

            ["CONTACT_NAME"] = contact?.Name ?? string.Empty,
            ["CONTACT_ADDRESS"] = contact?.Address ?? string.Empty,
            ["CONTACT_PHONE"] = contact?.Phone ?? string.Empty,
            ["CONTACT_EMAIL"] = contact?.Email ?? string.Empty,
            ["CONTACT_PAN"] = contact?.Pan ?? string.Empty,

            // Live, $[USER_NAME]$ rendered the sender's email when no display name was set. Here
            // FullName is required, so the fallback is unreachable -- kept so the two products
            // cannot diverge if that ever changes.
            ["USER_NAME"] = string.IsNullOrWhiteSpace(user?.FullName) ? user?.Email ?? string.Empty : user.FullName,
            ["USER_PHONE_NO"] = user?.Phone ?? string.Empty,
            ["USER_EMAIL"] = user?.Email ?? string.Empty,

            // No User.Address column exists. Offered for parity with the live catalogue, always
            // empty -- see docs/phase-30-status.md's follow-ups.
            ["USER_ADDRESS"] = string.Empty,
        };
    }

    /// <summary>Adds the document group's values. <paramref name="facts"/> comes from
    /// <see cref="ReadDocumentAsync"/>.</summary>
    public static void AddDocumentValues(
        Dictionary<string, string> values, EmailDocumentFacts facts, string contactName)
    {
        values["CUSTOMER_NAME"] = contactName;
        values["DOCUMENT_NO"] = facts.Code;
        values["DOCUMENT_DATE"] = EmailMergeResolver.FormatDate(facts.Date);
        values["DOCUMENT_REFERENCE"] = facts.Reference ?? string.Empty;

        // Live offers both "Invoice Date" and "Transaction Date" as separate fields; this codebase
        // stores one Date per document, so both resolve to it rather than one of them being absent.
        values["TRANSACTION_DATE"] = EmailMergeResolver.FormatDate(facts.Date);

        // No aggregate stores a due date -- phase-26b's carried item. DocumentAgeQueryHandler
        // already ages every document from its own Date for the same reason, so resolving DUE_DATE
        // to the document date keeps the email and the ageing report telling one story. When that
        // carried item lands, this is the one line that changes.
        values["DUE_DATE"] = EmailMergeResolver.FormatDate(facts.Date);

        values["CURRENCY"] = facts.CurrencyCode;
        values["EXCHANGE_RATE"] = EmailMergeResolver.FormatAmount(facts.ExchangeRate);
        values["SUB_TOTAL"] = EmailMergeResolver.FormatAmount(facts.SubTotal);
        values["TRANSACTION_DISCOUNT"] = EmailMergeResolver.FormatAmount(facts.DiscountAmount);
        values["NON_TAXABLE_TOTAL"] = EmailMergeResolver.FormatAmount(facts.NonTaxableTotal);
        values["TAXABLE_TOTAL"] = EmailMergeResolver.FormatAmount(facts.TaxableTotal);
        values["VAT"] = EmailMergeResolver.FormatAmount(facts.VatAmount);
        values["GRAND_TOTAL"] = EmailMergeResolver.FormatAmount(facts.GrandTotal);

        // No aggregate carries a note/narration field. Offered for parity, always empty -- an empty
        // value is truthful and, unlike leaving the token unresolved, keeps a body pasted from the
        // reference product from printing a raw placeholder into a customer's inbox.
        values["DOCUMENT_NOTE"] = string.Empty;

        values["PAYMENT_MODE"] = facts.PaymentMode ?? string.Empty;
        values["PAYMENT_REFERENCE"] = facts.Reference ?? string.Empty;
        values["PAYMENT_AMOUNT"] = facts.PaymentAmount is null
            ? string.Empty
            : EmailMergeResolver.FormatAmount(facts.PaymentAmount.Value);
    }

    /// <summary>
    /// Loads one emailable document's facts.
    /// </summary>
    /// <exception cref="NotFoundException">No such document in this organization.</exception>
    public static async Task<EmailDocumentFacts> ReadDocumentAsync(
        IAppDbContext db, Guid organizationId, DocumentType documentType, Guid documentId, CancellationToken ct)
    {
        return documentType switch
        {
            DocumentType.Quotation => await ReadQuotationAsync(db, organizationId, documentId, ct),
            DocumentType.SalesOrder => await ReadSalesOrderAsync(db, organizationId, documentId, ct),
            DocumentType.Invoice => await ReadInvoiceAsync(db, organizationId, documentId, ct),
            DocumentType.CreditNote => await ReadCreditNoteAsync(db, organizationId, documentId, ct),
            DocumentType.PurchaseOrder => await ReadPurchaseOrderAsync(db, organizationId, documentId, ct),
            DocumentType.Payment => await ReadPaymentAsync(db, organizationId, documentId, ct),
            _ => throw new ArgumentOutOfRangeException(
                nameof(documentType), documentType, "This document type has no Send Email action."),
        };
    }

    private static async Task<EmailDocumentFacts> ReadInvoiceAsync(
        IAppDbContext db, Guid organizationId, Guid id, CancellationToken ct)
    {
        var d = await db.Invoices.Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == id && x.OrganizationId == organizationId, ct)
            ?? throw new NotFoundException("Invoice not found.");

        return FromLines(
            d.ContactId, d.Code, d.Date, d.Reference, d.CurrencyCode, d.ExchangeRate, d.DiscountPct,
            d.Lines.Select(l => (l.Amount, l.VatAmount)));
    }

    private static async Task<EmailDocumentFacts> ReadQuotationAsync(
        IAppDbContext db, Guid organizationId, Guid id, CancellationToken ct)
    {
        var d = await db.Quotations.Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == id && x.OrganizationId == organizationId, ct)
            ?? throw new NotFoundException("Quotation not found.");

        return FromLines(
            d.ContactId, d.Code, d.Date, d.Reference, d.CurrencyCode, d.ExchangeRate, d.DiscountPct,
            d.Lines.Select(l => (l.Amount, l.VatAmount)));
    }

    private static async Task<EmailDocumentFacts> ReadSalesOrderAsync(
        IAppDbContext db, Guid organizationId, Guid id, CancellationToken ct)
    {
        var d = await db.SalesOrders.Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == id && x.OrganizationId == organizationId, ct)
            ?? throw new NotFoundException("Sales order not found.");

        return FromLines(
            d.ContactId, d.Code, d.Date, d.Reference, d.CurrencyCode, d.ExchangeRate, d.DiscountPct,
            d.Lines.Select(l => (l.Amount, l.VatAmount)));
    }

    private static async Task<EmailDocumentFacts> ReadCreditNoteAsync(
        IAppDbContext db, Guid organizationId, Guid id, CancellationToken ct)
    {
        var d = await db.CreditNotes.Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == id && x.OrganizationId == organizationId, ct)
            ?? throw new NotFoundException("Credit note not found.");

        return FromLines(
            d.ContactId, d.Code, d.Date, d.Reference, d.CurrencyCode, d.ExchangeRate, d.DiscountPct,
            d.Lines.Select(l => (l.Amount, l.VatAmount)));
    }

    private static async Task<EmailDocumentFacts> ReadPurchaseOrderAsync(
        IAppDbContext db, Guid organizationId, Guid id, CancellationToken ct)
    {
        var d = await db.PurchaseOrders.Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == id && x.OrganizationId == organizationId, ct)
            ?? throw new NotFoundException("Purchase order not found.");

        return FromLines(
            d.ContactId, d.Code, d.Date, d.Reference, d.CurrencyCode, d.ExchangeRate, d.DiscountPct,
            d.Lines.Select(l => (l.Amount, l.VatAmount)));
    }

    /// <summary>A Payment has no lines and no VAT: its whole value is one Amount, which is both the
    /// sub-total and the grand total. The live Customer Payment template's own extra fields
    /// (Payment Mode / Reference / Amount) are the ones that carry meaning here.</summary>
    private static async Task<EmailDocumentFacts> ReadPaymentAsync(
        IAppDbContext db, Guid organizationId, Guid id, CancellationToken ct)
    {
        var d = await db.Payments.SingleOrDefaultAsync(x => x.Id == id && x.OrganizationId == organizationId, ct)
            ?? throw new NotFoundException("Payment not found.");

        var mode = d.PaymentModeId is null
            ? null
            : await db.PaymentModes
                .Where(x => x.Id == d.PaymentModeId.Value)
                .Select(x => x.Name)
                .SingleOrDefaultAsync(ct);

        return new EmailDocumentFacts(
            d.ContactId, d.Code, d.Date, d.Reference, d.CurrencyCode, d.ExchangeRate,
            SubTotal: d.Amount,
            DiscountAmount: 0m,
            NonTaxableTotal: d.Amount,
            TaxableTotal: 0m,
            VatAmount: 0m,
            GrandTotal: d.Amount,
            PaymentMode: mode,
            PaymentAmount: d.Amount);
    }

    /// <summary>
    /// The shared line fold. <c>Amount</c> is already net of both the line discount and the
    /// document-level <c>DiscountPct</c> on every one of these aggregates, so the discount shown is
    /// reconstructed from the pre-discount gross rather than re-derived — the same figure the print
    /// pipeline puts in its summary block.
    /// </summary>
    private static EmailDocumentFacts FromLines(
        Guid contactId,
        string code,
        DateOnly date,
        string? reference,
        string currencyCode,
        decimal exchangeRate,
        decimal documentDiscountPct,
        IEnumerable<(decimal Amount, decimal VatAmount)> lines)
    {
        var materialised = lines.ToList();
        var netTotal = materialised.Sum(x => x.Amount);
        var vatTotal = materialised.Sum(x => x.VatAmount);

        var grossTotal = documentDiscountPct == 0m
            ? netTotal
            : decimal.Round(netTotal / (1m - (documentDiscountPct / 100m)), 2, MidpointRounding.AwayFromZero);

        var taxable = materialised.Where(x => x.VatAmount != 0m).Sum(x => x.Amount);

        return new EmailDocumentFacts(
            contactId, code, date, reference, currencyCode, exchangeRate,
            SubTotal: grossTotal,
            DiscountAmount: grossTotal - netTotal,
            NonTaxableTotal: netTotal - taxable,
            TaxableTotal: taxable,
            VatAmount: vatTotal,
            GrandTotal: netTotal + vatTotal);
    }
}
