using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Crm.Queries.ListSmsLogs;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Crm;

namespace ErpApp.Application.UnitTests.Crm;

/// <summary>
/// Phase 45 (39 carried item #4) -- the search term this query was exempted from in phase 39, with
/// its re-entry condition named. It matches the row's own Title, Content and PhoneNumber and not
/// the joined ContactName, which is the one thing worth asserting in both directions: a test that
/// only proved matches would pass just as happily if the term matched everything.
///
/// <para><b>Casing.</b> Every search here uses the stored casing. Single-argument
/// <c>string.Contains</c> is case-<i>insensitive</i> on SQL Server (its collation does it) and
/// case-<i>sensitive</i> on the InMemory provider these tests run against, so a test searching
/// "promo" for a stored "Promo" would pin a behaviour production does not have (CLAUDE.md,
/// phase-34b).</para>
/// </summary>
public class ListSmsLogsQueryHandlerTests
{
    [Fact]
    public async Task No_term_returns_every_row()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var result = await Handle(db, seed.OrganizationId, search: null);

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(3, result.Rows.Count);
    }

    [Fact]
    public async Task A_term_matches_the_title()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        // Two of the three rows are one batch under one title -- so this also shows the term
        // narrowing to a subset rather than to a single row.
        var result = await Handle(db, seed.OrganizationId, "Dashain");

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Rows, x => Assert.Equal("Dashain Offer", x.Title));
    }

    [Fact]
    public async Task A_term_matches_the_resolved_content()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        // The point of a per-recipient history: two rows of one batch carry genuinely different
        // resolved text, so the content is what tells them apart. "15% off" appears in exactly one
        // row's content and in no Title, PhoneNumber or contact name, so a match can only have come
        // from the Content column.
        var result = await Handle(db, seed.OrganizationId, "15% off");

        Assert.Equal(1, result.TotalCount);
        Assert.Contains("15% off", Assert.Single(result.Rows).Content);
    }

    [Fact]
    public async Task A_term_matches_the_phone_number()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var result = await Handle(db, seed.OrganizationId, "9800000003");

        Assert.Equal("9800000003", Assert.Single(result.Rows).PhoneNumber);
    }

    /// <summary>
    /// ContactName is joined in after the page is read, so it is not a column the term can reach --
    /// and following ListDealsQuery/ListInvoicesQuery that is deliberate, not an oversight. The
    /// seeded contact "Hari Enterprises" appears in no Title, Content or PhoneNumber.
    /// </summary>
    [Fact]
    public async Task A_term_does_not_match_the_joined_contact_name()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var result = await Handle(db, seed.OrganizationId, "Hari Enterprises");

        Assert.Equal(0, result.TotalCount);
        Assert.Empty(result.Rows);
    }

    [Fact]
    public async Task A_whitespace_only_term_is_not_a_search()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var result = await Handle(db, seed.OrganizationId, "   ");

        Assert.Equal(3, result.TotalCount);
    }

    /// <summary>The term narrows within the contact scope rather than escaping it -- the same query
    /// backs a Contact's own SMS History sub-tab.</summary>
    [Fact]
    public async Task A_term_composes_with_the_contact_filter()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var matches = await Handle(db, seed.OrganizationId, "Dashain", seed.RamContactId);
        var acrossContacts = await Handle(db, seed.OrganizationId, "Dashain");
        var otherContact = await Handle(db, seed.OrganizationId, "Dashain", seed.HariContactId);

        Assert.Equal(1, matches.TotalCount);
        Assert.Equal(2, acrossContacts.TotalCount);
        Assert.Equal(0, otherContact.TotalCount);
    }

    [Fact]
    public async Task A_term_never_reaches_another_organization()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var otherOrganizationId = Guid.NewGuid();
        db.SmsLogs.Add(SmsLog.Create(
            otherOrganizationId, Guid.NewGuid(), Guid.NewGuid(), null, "Dashain Offer",
            "Hello from another tenant", "9800000009", 1, Guid.NewGuid()));
        await db.SaveChangesAsync(CancellationToken.None);

        var result = await Handle(db, seed.OrganizationId, "Dashain");

        Assert.Equal(2, result.TotalCount);
    }

    private static Task<SmsLogListDto> Handle(
        IAppDbContext db, Guid organizationId, string? search, Guid? contactId = null)
    {
        var handler = new ListSmsLogsQueryHandler(db);
        return handler.Handle(
            new ListSmsLogsQuery(organizationId, contactId, 1, 50, search), CancellationToken.None);
    }

    private sealed record Seed(Guid OrganizationId, Guid RamContactId, Guid HariContactId);

    private static async Task<Seed> SeedAsync(IAppDbContext db)
    {
        var organizationId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var ram = await CreateContactAsync(db, organizationId, "Ram Traders", "9800000001");
        var shyam = await CreateContactAsync(db, organizationId, "Shyam Suppliers", "9800000002");
        var hari = await CreateContactAsync(db, organizationId, "Hari Enterprises", "9800000003");

        var batchId = Guid.NewGuid();
        db.SmsLogs.Add(SmsLog.Create(
            organizationId, batchId, ram, null, "Dashain Offer", "Namaste Ram Traders, 20% off.",
            "9800000001", 1, userId));
        db.SmsLogs.Add(SmsLog.Create(
            organizationId, batchId, shyam, null, "Dashain Offer", "Namaste Shyam Suppliers, 15% off.",
            "9800000002", 1, userId));
        db.SmsLogs.Add(SmsLog.Create(
            organizationId, Guid.NewGuid(), hari, null, "Payment Reminder", "Your balance is due.",
            "9800000003", 1, userId));
        await db.SaveChangesAsync(CancellationToken.None);

        return new Seed(organizationId, ram, hari);
    }

    private static async Task<Guid> CreateContactAsync(
        IAppDbContext db, Guid organizationId, string name, string phone)
    {
        var contact = Contact.Create(
            organizationId, ContactType.Customer, name, $"C-{Guid.NewGuid():N}"[..10], null, null, phone, null, null, 0m);
        db.Contacts.Add(contact);
        await db.SaveChangesAsync(CancellationToken.None);
        return contact.Id;
    }
}
