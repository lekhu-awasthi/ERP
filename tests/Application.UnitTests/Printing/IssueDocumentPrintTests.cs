using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Pos.Commands.PrintPosReceipt;
using ErpApp.Application.Printing.Commands.IssueDocumentPrint;
using ErpApp.Application.Printing.Queries.PrintDocument;
using ErpApp.Application.Sales.Queries.GetCreditNote;
using ErpApp.Application.Sales.Queries.GetInvoice;
using ErpApp.Application.UnitTests.Pos;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Sales;
using ErpApp.Domain.Tenancy;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Printing;

/// <summary>
/// Phase 67 -- the ERP's counted print of an invoice and a credit note: the heading the bill is
/// (Decision A), every copy counted and the copies marked (B), no uncounted route (C), one count shared
/// with the till (D), drafts and voids refused (E), and the credit note naming its invoice (F).
/// </summary>
public class IssueDocumentPrintTests
{
    private static readonly Guid PrinterId = Guid.NewGuid();

    // ---- The heading ---------------------------------------------------------------------------

    [Fact]
    public async Task A_vat_registered_sellers_invoice_is_a_tax_invoice_in_english_and_nepali()
    {
        var (db, organizationId) = await SeedAsync(isVatRegistered: true);
        var invoice = await ApprovedInvoiceAsync(db, organizationId);

        var dto = await IssueAsync(db, organizationId, DocumentType.Invoice, invoice.Id);

        Assert.Equal("Tax Invoice", dto.Title);
        Assert.Equal("कर बीजक", dto.TitleNepali);
    }

    [Fact]
    public async Task An_unregistered_sellers_invoice_is_an_invoice()
    {
        var (db, organizationId) = await SeedAsync(isVatRegistered: false);
        var invoice = await ApprovedInvoiceAsync(db, organizationId);

        var dto = await IssueAsync(db, organizationId, DocumentType.Invoice, invoice.Id);

        Assert.Equal("Invoice", dto.Title);
        Assert.Equal("बीजक", dto.TitleNepali);
    }

    [Theory]
    [InlineData(false, false, InvoiceHeading.Invoice)]
    [InlineData(false, true, InvoiceHeading.Invoice)]
    [InlineData(true, false, InvoiceHeading.TaxInvoice)]
    [InlineData(true, true, InvoiceHeading.AbbreviatedTaxInvoice)]
    public void The_heading_rule_is_one_rule_for_the_till_and_the_pdf(
        bool isVatRegistered, bool isAbbreviated, InvoiceHeading expected)
    {
        Assert.Equal(expected, InvoiceHeadings.For(isVatRegistered, isAbbreviated));
    }

    // ---- Counting ------------------------------------------------------------------------------

    [Fact]
    public async Task The_first_print_is_the_original_and_every_later_one_a_numbered_copy()
    {
        var (db, organizationId) = await SeedAsync(isVatRegistered: true);
        var invoice = await ApprovedInvoiceAsync(db, organizationId);

        var original = await IssueAsync(db, organizationId, DocumentType.Invoice, invoice.Id);
        var copy = await IssueAsync(db, organizationId, DocumentType.Invoice, invoice.Id);

        Assert.NotNull(original.PrintedCopy);
        Assert.Equal(1, original.PrintedCopy.PrintNumber);
        Assert.False(original.PrintedCopy.IsCopy);
        Assert.Equal("Asha Accountant", original.PrintedCopy.PrintedBy);

        Assert.NotNull(copy.PrintedCopy);
        Assert.Equal(2, copy.PrintedCopy.PrintNumber);
        Assert.True(copy.PrintedCopy.IsCopy);

        var rows = await db.InvoicePrints.Where(x => x.InvoiceId == invoice.Id).OrderBy(x => x.PrintNumber).ToListAsync();
        Assert.Equal([1, 2], rows.Select(x => x.PrintNumber));
        Assert.All(rows, x => Assert.Equal(PrintMedium.Pdf, x.Medium));
    }

    [Fact]
    public async Task An_emailed_copy_counts_like_a_printed_one()
    {
        // Decision B, the user's choice: the first copy out is the original whichever way it left.
        var (db, organizationId) = await SeedAsync(isVatRegistered: true);
        var invoice = await ApprovedInvoiceAsync(db, organizationId);

        var emailed = await IssueAsync(db, organizationId, DocumentType.Invoice, invoice.Id, PrintMedium.Email);
        var printed = await IssueAsync(db, organizationId, DocumentType.Invoice, invoice.Id);

        Assert.Equal(1, emailed.PrintedCopy!.PrintNumber);
        Assert.Equal(2, printed.PrintedCopy!.PrintNumber);
        Assert.Equal(
            [PrintMedium.Email, PrintMedium.Pdf],
            await db.InvoicePrints.Where(x => x.InvoiceId == invoice.Id).OrderBy(x => x.PrintNumber).Select(x => x.Medium).ToListAsync());
    }

    [Fact]
    public async Task A_till_sale_has_one_count_whether_printed_at_the_till_or_from_the_invoice_page()
    {
        var till = await PosTestTill.CreateAsync();
        var session = await till.OpenAsync();
        var sale = await till.SellAsync(session.Id, [till.Coke(1)], [till.Cash(68m)]);

        var atTheTill = await new PrintPosReceiptCommandHandler(till.Db, till.CurrentUser())
            .Handle(new PrintPosReceiptCommand(till.OrganizationId, sale.Id), CancellationToken.None);
        var fromTheErp = await IssueAsync(till.Db, till.OrganizationId, DocumentType.Invoice, sale.Id, userId: till.UserId);
        var atTheTillAgain = await new PrintPosReceiptCommandHandler(till.Db, till.CurrentUser())
            .Handle(new PrintPosReceiptCommand(till.OrganizationId, sale.Id), CancellationToken.None);

        Assert.Equal(1, atTheTill.PrintNumber);
        Assert.Equal(2, fromTheErp.PrintedCopy!.PrintNumber);
        Assert.Equal(3, atTheTillAgain.PrintNumber);
        Assert.Equal(atTheTill.Title.ToString(), fromTheErp.Title.Replace(" ", "", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_till_refund_has_one_count_too()
    {
        var till = await PosTestTill.CreateAsync();
        var session = await till.OpenAsync();
        var sale = await till.SellAsync(session.Id, [till.Coke(1)], [till.Cash(68m)]);
        var refund = await till.RefundAsync(session.Id, sale.Id, [await till.ReturnAsync(sale.Id, till.CokeId, 1m)], [till.Cash(68m)]);

        var atTheTill = await till.PrintRefundAsync(refund.Id);
        var fromTheErp = await IssueAsync(till.Db, till.OrganizationId, DocumentType.CreditNote, refund.Id, userId: till.UserId);

        Assert.Equal(1, atTheTill.PrintNumber);
        Assert.Equal(2, fromTheErp.PrintedCopy!.PrintNumber);
        Assert.Equal(2, await till.Db.CreditNotePrints.CountAsync(x => x.CreditNoteId == refund.Id));
    }

    [Fact]
    public async Task The_detail_pages_show_how_many_copies_have_left()
    {
        var till = await PosTestTill.CreateAsync();
        var session = await till.OpenAsync();
        var sale = await till.SellAsync(session.Id, [till.Coke(1)], [till.Cash(68m)]);
        var refund = await till.RefundAsync(session.Id, sale.Id, [await till.ReturnAsync(sale.Id, till.CokeId, 1m)], [till.Cash(68m)]);

        await new PrintPosReceiptCommandHandler(till.Db, till.CurrentUser())
            .Handle(new PrintPosReceiptCommand(till.OrganizationId, sale.Id), CancellationToken.None);
        await IssueAsync(till.Db, till.OrganizationId, DocumentType.Invoice, sale.Id, PrintMedium.Email, till.UserId);
        await IssueAsync(till.Db, till.OrganizationId, DocumentType.CreditNote, refund.Id, userId: till.UserId);

        var invoice = await new GetInvoiceQueryHandler(till.Db)
            .Handle(new GetInvoiceQuery(till.OrganizationId, sale.Id), CancellationToken.None);
        var note = await new GetCreditNoteQueryHandler(till.Db)
            .Handle(new GetCreditNoteQuery(till.OrganizationId, refund.Id), CancellationToken.None);

        Assert.Equal(2, invoice.PrintCount);
        Assert.Equal(1, note.PrintCount);
    }

    // ---- Refusals ------------------------------------------------------------------------------

    [Fact]
    public async Task The_uncounted_query_refuses_an_invoice_and_a_credit_note()
    {
        // Decision C: no route renders an invoice without writing its print row.
        var (db, organizationId) = await SeedAsync(isVatRegistered: true);
        var invoice = await ApprovedInvoiceAsync(db, organizationId);

        var refused = await Assert.ThrowsAsync<ConflictException>(() =>
            new PrintDocumentQueryHandler(db, new FakeFileStorage())
                .Handle(new PrintDocumentQuery(organizationId, DocumentType.Invoice, invoice.Id), CancellationToken.None));

        Assert.Contains("POST /print/Invoice", refused.Message, StringComparison.Ordinal);
        Assert.Empty(await db.InvoicePrints.ToListAsync());
    }

    [Fact]
    public async Task A_draft_is_refused_because_it_has_no_number_yet()
    {
        var (db, organizationId) = await SeedAsync(isVatRegistered: true);
        var draft = await ApprovedInvoiceAsync(db, organizationId, approve: false);

        var refused = await Assert.ThrowsAsync<ConflictException>(() => IssueAsync(db, organizationId, DocumentType.Invoice, draft.Id));

        Assert.Contains("draft", refused.Message, StringComparison.Ordinal);
        Assert.Empty(await db.InvoicePrints.ToListAsync());
    }

    [Fact]
    public async Task A_voided_invoice_is_refused()
    {
        var (db, organizationId) = await SeedAsync(isVatRegistered: true);
        var invoice = await ApprovedInvoiceAsync(db, organizationId);
        invoice.Void(PrinterId);
        await db.SaveChangesAsync();

        var refused = await Assert.ThrowsAsync<ConflictException>(() => IssueAsync(db, organizationId, DocumentType.Invoice, invoice.Id));

        Assert.Contains("voided", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_missing_document_is_a_404()
    {
        var (db, organizationId) = await SeedAsync(isVatRegistered: true);

        await Assert.ThrowsAsync<NotFoundException>(() => IssueAsync(db, organizationId, DocumentType.Invoice, Guid.NewGuid()));
        await Assert.ThrowsAsync<NotFoundException>(() => IssueAsync(db, organizationId, DocumentType.CreditNote, Guid.NewGuid()));
    }

    [Fact]
    public void The_validator_accepts_only_the_two_counted_types_and_the_two_erp_media()
    {
        var validator = new IssueDocumentPrintCommandValidator();
        var organizationId = Guid.NewGuid();
        var id = Guid.NewGuid();

        Assert.True(validator.Validate(new IssueDocumentPrintCommand(organizationId, DocumentType.Invoice, id, PrintMedium.Pdf)).IsValid);
        Assert.True(validator.Validate(new IssueDocumentPrintCommand(organizationId, DocumentType.CreditNote, id, PrintMedium.Email)).IsValid);

        var otherType = validator.Validate(new IssueDocumentPrintCommand(organizationId, DocumentType.Quotation, id, PrintMedium.Pdf));
        Assert.Contains(otherType.Errors, x => x.PropertyName == nameof(IssueDocumentPrintCommand.DocumentType));

        var tillMedium = validator.Validate(new IssueDocumentPrintCommand(organizationId, DocumentType.Invoice, id, PrintMedium.TillReceipt));
        Assert.Contains(tillMedium.Errors, x => x.PropertyName == nameof(IssueDocumentPrintCommand.Medium));
    }

    // ---- The credit note -----------------------------------------------------------------------

    [Fact]
    public async Task A_credit_note_raised_from_an_invoice_names_its_number_and_date()
    {
        var (db, organizationId) = await SeedAsync(isVatRegistered: true);
        var invoice = await ApprovedInvoiceAsync(db, organizationId);
        var contactId = invoice.ContactId;
        var productId = await db.Products.Select(x => x.Id).FirstAsync();

        var note = CreditNote.Create(organizationId, contactId, new DateOnly(2026, 8, 5), invoice.Code, DocumentType.Invoice, invoice.Id);
        note.AddLine(productId, 1, 100, VatRate.ThirteenPercentVat, 0, null, 1m);
        note.Approve(PrinterId, "CN0001");
        db.CreditNotes.Add(note);
        await db.SaveChangesAsync();

        var dto = await IssueAsync(db, organizationId, DocumentType.CreditNote, note.Id);

        Assert.Equal("Credit Note", dto.Title);
        Assert.Equal("क्रेडिट नोट", dto.TitleNepali);
        var against = Assert.Single(dto.HeaderFields, x => x.Label == "Against Invoice");
        Assert.Equal("INV0001 dated 2026-08-01", against.Value);
        Assert.Equal(1, dto.PrintedCopy!.PrintNumber);
    }

    [Fact]
    public async Task A_standalone_credit_note_names_no_invoice()
    {
        var (db, organizationId) = await SeedAsync(isVatRegistered: true);
        var contactId = await db.Contacts.Select(x => x.Id).FirstAsync();
        var productId = await db.Products.Select(x => x.Id).FirstAsync();

        var note = CreditNote.Create(organizationId, contactId, new DateOnly(2026, 8, 5), "Goodwill", null, null);
        note.AddLine(productId, 1, 100, VatRate.ThirteenPercentVat, 0, null, 1m);
        note.Approve(PrinterId, "CN0002");
        db.CreditNotes.Add(note);
        await db.SaveChangesAsync();

        var dto = await IssueAsync(db, organizationId, DocumentType.CreditNote, note.Id);

        Assert.DoesNotContain(dto.HeaderFields, x => x.Label == "Against Invoice");
        Assert.Equal("Goodwill", dto.Reference);
    }

    // ---- Helpers -------------------------------------------------------------------------------

    private static async Task<PrintableDocumentDto> IssueAsync(
        IAppDbContext db, Guid organizationId, DocumentType type, Guid id, PrintMedium medium = PrintMedium.Pdf, Guid? userId = null) =>
        await new IssueDocumentPrintCommandHandler(db, new FakeCurrentUserService(userId ?? PrinterId), new PrintSender(db))
            .Handle(new IssueDocumentPrintCommand(organizationId, type, id, medium), CancellationToken.None);

    private static async Task<(IAppDbContext Db, Guid OrganizationId)> SeedAsync(bool isVatRegistered)
    {
        var db = TestAppDbContext.Create();
        var organization = Organization.Create(
            "Moonbeam Trading", "Retail", "Kathmandu, Nepal", new DateOnly(2026, 1, 1), isVatRegistered, "moonbeam",
            "info@moonbeam.test", "01-4000000", "PAN12345", "https://moonbeam.test", Guid.NewGuid());
        db.Organizations.Add(organization);

        var printer = Domain.Identity.User.Register("Asha Accountant", $"asha-{Guid.NewGuid():N}@example.com", "9800000001", "hash");
        typeof(Domain.Identity.User).GetProperty(nameof(Domain.Identity.User.Id))!.SetValue(printer, PrinterId);
        db.Users.Add(printer);

        db.Contacts.Add(Contact.Create(organization.Id, ContactType.Customer, "Acme Traders", "C-001", "Pokhara", null, null, null, null, 0));
        db.Products.Add(Product.Create(
            organization.Id, ProductType.Service, "Consulting", "P-001", Guid.NewGuid(), Guid.NewGuid(), null, true, 100, 0,
            VatRate.ThirteenPercentVat, 0, false));
        await db.SaveChangesAsync();

        return (db, organization.Id);
    }

    private static async Task<Invoice> ApprovedInvoiceAsync(IAppDbContext db, Guid organizationId, bool approve = true)
    {
        var contactId = await db.Contacts.Select(x => x.Id).FirstAsync();
        var productId = await db.Products.Select(x => x.Id).FirstAsync();

        var invoice = Invoice.Create(organizationId, contactId, Guid.NewGuid(), new DateOnly(2026, 8, 1), null, null, null);
        invoice.AddLine(productId, 2, 100, VatRate.ThirteenPercentVat, 0, null, 1m);
        if (approve)
        {
            invoice.Approve(PrinterId, "INV0001");
        }

        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();
        return invoice;
    }

    /// <summary>Stands in for the pipeline's one nested request: the render.</summary>
    private sealed class PrintSender(IAppDbContext db) : ISender
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            request is PrintDocumentQuery query
                ? (Task<TResponse>)(object)new PrintDocumentQueryHandler(db, new FakeFileStorage()).Handle(query, cancellationToken)
                : throw new InvalidOperationException($"Unexpected request {request.GetType().Name}.");

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
