using ErpApp.Application.Accounting;
using ErpApp.Application.Accounting.Commands.ApproveJournalVoucher;
using ErpApp.Application.Accounting.Commands.CreateAccount;
using ErpApp.Application.Accounting.Commands.CreateAccountGroup;
using ErpApp.Application.Accounting.Commands.CreateJournalVoucher;
using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Contacts.Commands.CreateContact;
using ErpApp.Application.Payments;
using ErpApp.Application.Payments.Commands.ApplyPaymentAllocation;
using ErpApp.Application.Payments.Commands.ApprovePayment;
using ErpApp.Application.Payments.Commands.CreatePayment;
using ErpApp.Application.Payments.Posting;
using ErpApp.Application.Payments.Queries.ListAllocatablePayments;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Payments;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Payments;

public class ListAllocatablePaymentsQueryHandlerTests
{
    [Fact]
    public async Task Handle_lists_an_under_allocated_approved_payment_in_the_unallocated_tab()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var created = await new CreatePaymentCommandHandler(db).Handle(
            new CreatePaymentCommand(
                seed.OrganizationId, seed.CustomerId, PaymentDirection.Received, new DateOnly(2026, 1, 1), null,
                seed.CashAccountId, 500m, null, []),
            CancellationToken.None);
        await new ApprovePaymentCommandHandler(db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new PaymentPostingRule())
            .Handle(new ApprovePaymentCommand(seed.OrganizationId, created.Id), CancellationToken.None);

        var result = await new ListAllocatablePaymentsQueryHandler(db).Handle(
            new ListAllocatablePaymentsQuery(seed.OrganizationId, PaymentDirection.Received), CancellationToken.None);

        var row = Assert.Single(result.Items);
        Assert.Equal(DocumentType.Payment, row.SourceType);
        Assert.Equal(created.Id, row.Id);
        Assert.Equal(500m, row.Balance);
    }

    [Fact]
    public async Task Handle_lists_an_approved_journal_voucher_lines_contact_tagged_credit()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var created = await new CreateJournalVoucherCommandHandler(db).Handle(
            new CreateJournalVoucherCommand(seed.OrganizationId, new DateOnly(2026, 1, 1), null,
                [
                    new JournalVoucherLineInput(seed.CashAccountId, 300m, 0m),
                    new JournalVoucherLineInput(seed.ArAccountId, 0m, 300m, seed.CustomerId),
                ]),
            CancellationToken.None);
        await new ApproveJournalVoucherCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new JournalVoucherPostingRule())
            .Handle(new ApproveJournalVoucherCommand(seed.OrganizationId, created.Id), CancellationToken.None);

        var result = await new ListAllocatablePaymentsQueryHandler(db).Handle(
            new ListAllocatablePaymentsQuery(seed.OrganizationId, PaymentDirection.Received), CancellationToken.None);

        var row = Assert.Single(result.Items);
        Assert.Equal(DocumentType.JournalVoucher, row.SourceType);
        Assert.Equal(created.Id, row.ParentDocumentId);
        Assert.Equal(300m, row.Balance);
    }

    [Fact]
    public async Task Handle_excludes_a_journal_voucher_line_whose_contact_type_does_not_match_direction()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var created = await new CreateJournalVoucherCommandHandler(db).Handle(
            new CreateJournalVoucherCommand(seed.OrganizationId, new DateOnly(2026, 1, 1), null,
                [
                    new JournalVoucherLineInput(seed.CashAccountId, 300m, 0m),
                    new JournalVoucherLineInput(seed.ArAccountId, 0m, 300m, seed.CustomerId),
                ]),
            CancellationToken.None);
        await new ApproveJournalVoucherCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new JournalVoucherPostingRule())
            .Handle(new ApproveJournalVoucherCommand(seed.OrganizationId, created.Id), CancellationToken.None);

        // A Customer-tagged line shouldn't surface on the Supplier (Paid/AP) Allocate screen.
        var result = await new ListAllocatablePaymentsQueryHandler(db).Handle(
            new ListAllocatablePaymentsQuery(seed.OrganizationId, PaymentDirection.Paid), CancellationToken.None);

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task Handle_moves_a_fully_allocated_journal_voucher_line_to_the_allocated_tab()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var created = await new CreateJournalVoucherCommandHandler(db).Handle(
            new CreateJournalVoucherCommand(seed.OrganizationId, new DateOnly(2026, 1, 1), null,
                [
                    new JournalVoucherLineInput(seed.CashAccountId, 300m, 0m),
                    new JournalVoucherLineInput(seed.ArAccountId, 0m, 300m, seed.CustomerId),
                ]),
            CancellationToken.None);
        await new ApproveJournalVoucherCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new JournalVoucherPostingRule())
            .Handle(new ApproveJournalVoucherCommand(seed.OrganizationId, created.Id), CancellationToken.None);

        // CreditNote isn't existence-checked by EnsureAllocationTargetsExistAsync (only
        // Invoice/PurchaseBill are) -- a real target document isn't needed to exercise this
        // query's own "moves to the Allocated tab" logic.
        var line = await db.JournalVoucherLines.SingleAsync(x => x.JournalVoucherId == created.Id && x.ContactId != null);
        await new ApplyPaymentAllocationCommandHandler(db).Handle(
            new ApplyPaymentAllocationCommand(
                seed.OrganizationId, DocumentType.JournalVoucher, line.Id, created.Id, DocumentType.CreditNote, Guid.NewGuid(), 300m),
            CancellationToken.None);

        var unallocated = await new ListAllocatablePaymentsQueryHandler(db).Handle(
            new ListAllocatablePaymentsQuery(seed.OrganizationId, PaymentDirection.Received), CancellationToken.None);
        var allocated = await new ListAllocatablePaymentsQueryHandler(db).Handle(
            new ListAllocatablePaymentsQuery(seed.OrganizationId, PaymentDirection.Received, ShowAllocated: true), CancellationToken.None);

        Assert.Empty(unallocated.Items);
        Assert.Single(allocated.Items);
    }

    private sealed record Seed(
        Guid OrganizationId, FakeDocumentNumberGenerator NumberGenerator, Guid CustomerId, Guid CashAccountId, Guid ArAccountId);

    private static async Task<Seed> SeedAsync(IAppDbContext db)
    {
        var organizationId = Guid.NewGuid();
        var numberGenerator = new FakeDocumentNumberGenerator();

        var customer = await new CreateContactCommandHandler(db, numberGenerator).Handle(
            new CreateContactCommand(organizationId, ContactType.Customer, "Acme Traders", null, null, null, null, null, 0m),
            CancellationToken.None);

        var assetGroup = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Current Assets", AccountRootType.Asset, null), CancellationToken.None);

        var ar = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Accounts Receivable", assetGroup.Id), CancellationToken.None);
        var cash = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Cash in Hand", assetGroup.Id), CancellationToken.None);

        var settings = TenantSettings.CreateDefault(organizationId);
        settings.SetAccountingDefaults(null, ar.Id, null, null, null, null, null);
        db.TenantSettings.Add(settings);
        await db.SaveChangesAsync(CancellationToken.None);

        return new Seed(organizationId, numberGenerator, customer.Id, cash.Id, ar.Id);
    }
}
