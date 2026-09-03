using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Configuration.Commands.CreateReportingTagCategory;
using ErpApp.Application.Configuration.Commands.CreateReportingTagOption;
using ErpApp.Application.Configuration.Commands.SetTransactionReportingTags;
using ErpApp.Application.Configuration.Queries.GetTransactionReportingTags;
using ErpApp.Application.Contacts.Commands.CreateContact;
using ErpApp.Application.Sales.Commands.CreateQuotation;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Sales;

namespace ErpApp.Application.UnitTests.Configuration;

public class SetTransactionReportingTagsCommandHandlerTests
{
    [Fact]
    public async Task Handle_replaces_the_full_tag_set_rather_than_appending()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var handler = new SetTransactionReportingTagsCommandHandler(db);
        await handler.Handle(
            new SetTransactionReportingTagsCommand(
                seed.OrganizationId, DocumentType.Quotation, seed.QuotationId, [seed.TagOptionAId, seed.TagOptionBId]),
            CancellationToken.None);

        await handler.Handle(
            new SetTransactionReportingTagsCommand(
                seed.OrganizationId, DocumentType.Quotation, seed.QuotationId, [seed.TagOptionBId]),
            CancellationToken.None);

        var tags = await new GetTransactionReportingTagsQueryHandler(db).Handle(
            new GetTransactionReportingTagsQuery(seed.OrganizationId, DocumentType.Quotation, seed.QuotationId),
            CancellationToken.None);

        var tag = Assert.Single(tags);
        Assert.Equal(seed.TagOptionBId, tag.TagOptionId);
    }

    [Fact]
    public async Task Handle_rejects_a_tag_option_that_does_not_belong_to_the_organization()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var handler = new SetTransactionReportingTagsCommandHandler(db);
        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new SetTransactionReportingTagsCommand(seed.OrganizationId, DocumentType.Quotation, seed.QuotationId, [Guid.NewGuid()]),
            CancellationToken.None));
    }

    [Fact]
    public async Task Handle_rejects_a_document_id_that_does_not_exist()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var handler = new SetTransactionReportingTagsCommandHandler(db);
        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new SetTransactionReportingTagsCommand(seed.OrganizationId, DocumentType.Quotation, Guid.NewGuid(), [seed.TagOptionAId]),
            CancellationToken.None));
    }

    [Fact]
    public void Command_permission_key_rides_on_the_document_types_own_edit_permission()
    {
        var quotationCommand = new SetTransactionReportingTagsCommand(Guid.NewGuid(), DocumentType.Quotation, Guid.NewGuid(), []);
        Assert.Equal("Sales.Quotation.Edit", quotationCommand.PermissionKey);

        var invoiceCommand = new SetTransactionReportingTagsCommand(Guid.NewGuid(), DocumentType.Invoice, Guid.NewGuid(), []);
        Assert.Equal("Sales.Invoice.Edit", invoiceCommand.PermissionKey);

        var journalVoucherCommand = new SetTransactionReportingTagsCommand(
            Guid.NewGuid(), DocumentType.JournalVoucher, Guid.NewGuid(), []);
        Assert.Equal("Accounting.JournalVoucher.Edit", journalVoucherCommand.PermissionKey);
    }

    /// <summary>
    /// Phase 27a: both Opening Balances tabs carry an inline "Add Reporting Tags" link in their row
    /// form, so an opening balance and an opening stock line are taggable even though neither is a
    /// transactional document. Both ride the single OpeningBalance.Edit key, matching
    /// CreateOrUpdateOpeningStockLineCommand, which has always done the same.
    /// </summary>
    [Fact]
    public void Both_opening_balance_kinds_are_taggable_and_share_one_key()
    {
        Assert.Equal(
            "Accounting.OpeningBalance.Edit",
            new SetTransactionReportingTagsCommand(
                Guid.NewGuid(), DocumentType.OpeningBalance, Guid.NewGuid(), []).PermissionKey);

        Assert.Equal(
            "Accounting.OpeningBalance.Edit",
            new SetTransactionReportingTagsCommand(
                Guid.NewGuid(), DocumentType.OpeningStock, Guid.NewGuid(), []).PermissionKey);
    }

    /// <summary>Phase 27a: a DocumentType that is not a document at all is still refused -- the
    /// sweep widened the list, it did not remove the floor.</summary>
    [Fact]
    public void Command_refuses_a_document_type_that_is_not_a_document()
    {
        var dataExport = new SetTransactionReportingTagsCommand(
            Guid.NewGuid(), DocumentType.DataExport, Guid.NewGuid(), []);
        Assert.Throws<ArgumentOutOfRangeException>(() => dataExport.PermissionKey);

        var migrated = new SetTransactionReportingTagsCommand(
            Guid.NewGuid(), DocumentType.MigratedSalesEntry, Guid.NewGuid(), []);
        Assert.Throws<ArgumentOutOfRangeException>(() => migrated.PermissionKey);
    }

    private sealed record Seed(Guid OrganizationId, Guid QuotationId, Guid TagOptionAId, Guid TagOptionBId);

    private static async Task<Seed> SeedAsync(IAppDbContext db)
    {
        var organizationId = Guid.NewGuid();
        var numberGenerator = new FakeDocumentNumberGenerator();

        var customer = await new CreateContactCommandHandler(db, numberGenerator).Handle(
            new CreateContactCommand(organizationId, ContactType.Customer, "Acme Traders", null, null, null, null, null, 0m),
            CancellationToken.None);

        var quotation = await new CreateQuotationCommandHandler(db).Handle(
            new CreateQuotationCommand(organizationId, customer.Id, new DateOnly(2026, 1, 1), null, null, []),
            CancellationToken.None);

        var category = await new CreateReportingTagCategoryCommandHandler(db).Handle(
            new CreateReportingTagCategoryCommand(organizationId, "Project"), CancellationToken.None);
        var tagA = await new CreateReportingTagOptionCommandHandler(db).Handle(
            new CreateReportingTagOptionCommand(organizationId, "Project A", category.Id), CancellationToken.None);
        var tagB = await new CreateReportingTagOptionCommandHandler(db).Handle(
            new CreateReportingTagOptionCommand(organizationId, "Project B", category.Id), CancellationToken.None);

        return new Seed(organizationId, quotation.Id, tagA.Id, tagB.Id);
    }
}
