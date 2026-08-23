using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Crm.Commands.SendSms;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Crm;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Crm;

/// <summary>
/// Covers roadmap Phase 18's SendSmsCommand -- the one command in this feature set with a real
/// atomicity requirement (docs/phase-18-status.md exit criteria #5): a mid-batch failure must leave
/// zero partial SmsLog rows and an unchanged credit ledger balance. See
/// SendSmsCommandHandler's own doc comment for how atomicity is achieved (writes happen only after
/// every recipient's send succeeds, in one SaveChangesAsync call).
/// </summary>
public class SendSmsCommandHandlerTests
{
    [Fact]
    public async Task Send_with_All_audience_writes_one_log_per_contact_and_debits_the_ledger()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db, initialCredits: 100);

        var handler = new SendSmsCommandHandler(db, new FakeSmsSender(), new FakeCurrentUserService(seed.UserId));
        var result = await handler.Handle(
            new SendSmsCommand(seed.OrganizationId, SmsAudienceMode.All, null, null, null, "Promo", "Hi $[name]$!"),
            CancellationToken.None);

        Assert.Equal(3, result.RecipientCount);
        Assert.Equal(3, result.CreditsUsed);
        Assert.Equal(97, result.RemainingBalance);

        var logs = await db.SmsLogs.Where(x => x.OrganizationId == seed.OrganizationId).ToListAsync();
        Assert.Equal(3, logs.Count);
        Assert.All(logs, x => Assert.Equal(result.BatchId, x.BatchId));

        var balance = await db.SmsCreditLedgerEntries
            .Where(x => x.OrganizationId == seed.OrganizationId)
            .SumAsync(x => x.ChangeAmount);
        Assert.Equal(97, balance);
    }

    [Fact]
    public async Task Send_excludes_contacts_with_no_phone_number()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db, initialCredits: 100);
        var noPhoneContact = await CreateContactAsync(db, seed.OrganizationId, "No Phone Co", phone: null, groupId: null);

        var handler = new SendSmsCommandHandler(db, new FakeSmsSender(), new FakeCurrentUserService(seed.UserId));
        var result = await handler.Handle(
            new SendSmsCommand(seed.OrganizationId, SmsAudienceMode.All, null, null, null, "Promo", "Hi $[name]$!"),
            CancellationToken.None);

        Assert.Equal(3, result.RecipientCount);
        Assert.DoesNotContain(await db.SmsLogs.ToListAsync(), x => x.ContactId == noPhoneContact);
    }

    [Fact]
    public async Task Send_with_ContactGroup_audience_only_includes_that_groups_contacts()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db, initialCredits: 100);

        var handler = new SendSmsCommandHandler(db, new FakeSmsSender(), new FakeCurrentUserService(seed.UserId));
        var result = await handler.Handle(
            new SendSmsCommand(seed.OrganizationId, SmsAudienceMode.ContactGroup, seed.GroupId, null, null, "Promo", "Hi $[name]$!"),
            CancellationToken.None);

        Assert.Equal(1, result.RecipientCount);
        var log = Assert.Single(await db.SmsLogs.ToListAsync());
        Assert.Equal(seed.GroupedContactId, log.ContactId);
    }

    [Fact]
    public async Task Send_with_Custom_audience_only_includes_the_selected_contacts()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db, initialCredits: 100);

        var handler = new SendSmsCommandHandler(db, new FakeSmsSender(), new FakeCurrentUserService(seed.UserId));
        var result = await handler.Handle(
            new SendSmsCommand(
                seed.OrganizationId, SmsAudienceMode.Custom, null, [seed.GroupedContactId], null, "Promo", "Hi $[name]$!"),
            CancellationToken.None);

        Assert.Equal(1, result.RecipientCount);
    }

    [Fact]
    public async Task Send_resolves_merge_fields_to_genuinely_different_text_per_recipient()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db, initialCredits: 100);

        var handler = new SendSmsCommandHandler(db, new FakeSmsSender(), new FakeCurrentUserService(seed.UserId));
        await handler.Handle(
            new SendSmsCommand(seed.OrganizationId, SmsAudienceMode.All, null, null, null, "Promo", "Hi $[name]$!"),
            CancellationToken.None);

        var logs = await db.SmsLogs.Where(x => x.OrganizationId == seed.OrganizationId).ToListAsync();
        var distinctContent = logs.Select(x => x.Content).Distinct().ToList();

        Assert.Equal(logs.Count, distinctContent.Count);
        Assert.DoesNotContain(logs, x => x.Content.Contains("$[name]$"));
    }

    [Fact]
    public async Task Send_rejects_with_insufficient_credit_and_writes_nothing()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db, initialCredits: 2);

        var sender = new FakeSmsSender();
        var handler = new SendSmsCommandHandler(db, sender, new FakeCurrentUserService(seed.UserId));

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new SendSmsCommand(seed.OrganizationId, SmsAudienceMode.All, null, null, null, "Promo", "Hi $[name]$!"),
            CancellationToken.None));

        Assert.Empty(sender.Sent);
        Assert.Empty(await db.SmsLogs.ToListAsync());

        var balance = await db.SmsCreditLedgerEntries
            .Where(x => x.OrganizationId == seed.OrganizationId)
            .SumAsync(x => x.ChangeAmount);
        Assert.Equal(2, balance);
    }

    [Fact]
    public async Task Send_mid_batch_failure_leaves_zero_partial_rows_and_an_unchanged_ledger_balance()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db, initialCredits: 100);

        // Three recipients (see SeedAsync) -- fail on the 2nd send, simulating a gateway error
        // partway through the batch.
        var sender = new FakeSmsSender(failOnCallNumber: 2);
        var handler = new SendSmsCommandHandler(db, sender, new FakeCurrentUserService(seed.UserId));

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new SendSmsCommand(seed.OrganizationId, SmsAudienceMode.All, null, null, null, "Promo", "Hi $[name]$!"),
            CancellationToken.None));

        // The 1st send succeeded against the fake gateway (recorded in FakeSmsSender.Sent), but
        // nothing was ever added to the DbContext -- SaveChangesAsync is only called once, after
        // every recipient succeeds.
        Assert.Single(sender.Sent);
        Assert.Empty(await db.SmsLogs.ToListAsync());

        var balance = await db.SmsCreditLedgerEntries
            .Where(x => x.OrganizationId == seed.OrganizationId)
            .SumAsync(x => x.ChangeAmount);
        Assert.Equal(100, balance);
    }

    private sealed record Seed(Guid OrganizationId, Guid UserId, Guid GroupId, Guid GroupedContactId);

    private static async Task<Seed> SeedAsync(IAppDbContext db, int initialCredits)
    {
        var organizationId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var group = ContactGroup.Create(organizationId, "VIP", null);
        db.ContactGroups.Add(group);
        await db.SaveChangesAsync(CancellationToken.None);

        var groupedContactId = await CreateContactAsync(db, organizationId, "Ram Traders", "9800000001", group.Id);
        await CreateContactAsync(db, organizationId, "Shyam Suppliers", "9800000002", null);
        await CreateContactAsync(db, organizationId, "Hari Enterprises", "9800000003", null);

        db.SmsCreditLedgerEntries.Add(
            SmsCreditLedgerEntry.CreateManualAdjustment(organizationId, initialCredits, "Initial credit", userId));
        await db.SaveChangesAsync(CancellationToken.None);

        return new Seed(organizationId, userId, group.Id, groupedContactId);
    }

    private static async Task<Guid> CreateContactAsync(
        IAppDbContext db, Guid organizationId, string name, string? phone, Guid? groupId)
    {
        var contact = Contact.Create(organizationId, ContactType.Customer, name, $"C-{Guid.NewGuid():N}"[..10], null, null, phone, null, groupId, 0m);
        db.Contacts.Add(contact);
        await db.SaveChangesAsync(CancellationToken.None);
        return contact.Id;
    }
}
