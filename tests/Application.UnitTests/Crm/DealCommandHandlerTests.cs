using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Contacts.Commands.CreateContact;
using ErpApp.Application.Crm.Commands.CreateDeal;
using ErpApp.Application.Crm.Commands.MarkDealLost;
using ErpApp.Application.Crm.Commands.MarkDealWon;
using ErpApp.Application.Crm.Commands.MoveDealToStage;
using ErpApp.Application.Crm.Commands.UpdateDeal;
using ErpApp.Application.Crm.Queries.ListDeals;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Configuration;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Crm;
using ErpApp.Domain.Identity;
using ErpApp.Domain.Tenancy;

namespace ErpApp.Application.UnitTests.Crm;

/// <summary>
/// Covers roadmap Phase 15's Deal feature -- the CRM module's first feature, mirroring
/// TaskCommandHandlerTests' (Phase 13) exact coverage shape: the terminal-status guard, IsPrivate
/// visibility (extended here across multiple assignees, since Deal.Assignees is genuinely plural
/// unlike WorkTask's single scalar AssignedToUserId), Contact-Type restriction, the Status? filter,
/// and org-scope isolation.
/// </summary>
public class DealCommandHandlerTests
{
    [Fact]
    public async Task Create_rejects_a_supplier_contact()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var supplier = await new CreateContactCommandHandler(db, new FakeDocumentNumberGenerator()).Handle(
            new CreateContactCommand(seed.OrganizationId, ContactType.Supplier, "Acme Supplies", null, null, null, null, null, 0m),
            CancellationToken.None);

        var handler = new CreateDealCommandHandler(db, new FakeCurrentUserService(seed.AdminUserId));

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new CreateDealCommand(
                seed.OrganizationId, supplier.Id, "New deal", [], null, null, 1000m, null, false),
            CancellationToken.None));
    }

    [Theory]
    [InlineData(ContactType.Customer)]
    [InlineData(ContactType.Lead)]
    public async Task Create_succeeds_against_a_customer_or_lead_contact(ContactType contactType)
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var contact = await new CreateContactCommandHandler(db, new FakeDocumentNumberGenerator()).Handle(
            new CreateContactCommand(seed.OrganizationId, contactType, "Acme Retail", null, null, null, null, null, 0m),
            CancellationToken.None);

        var handler = new CreateDealCommandHandler(db, new FakeCurrentUserService(seed.AdminUserId));
        var result = await handler.Handle(
            new CreateDealCommand(
                seed.OrganizationId, contact.Id, "New deal", [], null, null, 1000m, null, false),
            CancellationToken.None);

        Assert.Equal(DealStatus.Pending, result.Status);
    }

    [Fact]
    public async Task MarkWon_is_terminal_and_blocks_further_edits()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var dealId = await CreateDealAsync(db, seed, []);

        var markWonHandler = new MarkDealWonCommandHandler(db);
        var result = await markWonHandler.Handle(new MarkDealWonCommand(seed.OrganizationId, dealId), CancellationToken.None);
        Assert.Equal(DealStatus.Won, result.Status);
        Assert.NotNull(result.ClosingDate);

        await Assert.ThrowsAsync<ConflictException>(() => markWonHandler.Handle(
            new MarkDealWonCommand(seed.OrganizationId, dealId), CancellationToken.None));

        var updateHandler = new UpdateDealCommandHandler(db);
        await Assert.ThrowsAsync<ConflictException>(() => updateHandler.Handle(
            new UpdateDealCommand(seed.OrganizationId, dealId, "Renamed", [], null, null, 500m, null, false),
            CancellationToken.None));

        var moveHandler = new MoveDealToStageCommandHandler(db);
        await Assert.ThrowsAsync<ConflictException>(() => moveHandler.Handle(
            new MoveDealToStageCommand(seed.OrganizationId, dealId, seed.DealStageId), CancellationToken.None));
    }

    [Fact]
    public async Task MarkLost_is_terminal()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var dealId = await CreateDealAsync(db, seed, []);

        var handler = new MarkDealLostCommandHandler(db);
        var result = await handler.Handle(new MarkDealLostCommand(seed.OrganizationId, dealId), CancellationToken.None);
        Assert.Equal(DealStatus.Lost, result.Status);

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new MarkDealLostCommand(seed.OrganizationId, dealId), CancellationToken.None));
    }

    [Fact]
    public async Task MoveToStage_succeeds_while_pending()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var dealId = await CreateDealAsync(db, seed, []);

        var handler = new MoveDealToStageCommandHandler(db);
        var result = await handler.Handle(
            new MoveDealToStageCommand(seed.OrganizationId, dealId, seed.DealStageId), CancellationToken.None);

        Assert.Equal(seed.DealStageId, result.DealStageId);
    }

    [Fact]
    public async Task ListDeals_hides_a_private_deal_from_a_user_who_is_neither_creator_nor_an_assignee()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var assignee1 = await CreateAcceptedMemberAsync(db, seed.OrganizationId, "Assignee One", "assignee1@example.com");
        var assignee2 = await CreateAcceptedMemberAsync(db, seed.OrganizationId, "Assignee Two", "assignee2@example.com");
        var outsiderId = await CreateAcceptedMemberAsync(db, seed.OrganizationId, "Outsider", "outsider@example.com");

        var createHandler = new CreateDealCommandHandler(db, new FakeCurrentUserService(seed.AdminUserId));
        await createHandler.Handle(
            new CreateDealCommand(
                seed.OrganizationId, seed.ContactId, "Confidential deal", [assignee1, assignee2], null, null, 5000m, null, true),
            CancellationToken.None);

        var listQuery = new ListDealsQuery(seed.OrganizationId, seed.ContactId, null);

        var asCreator = await new ListDealsQueryHandler(db, new FakeCurrentUserService(seed.AdminUserId))
            .Handle(listQuery, CancellationToken.None);
        Assert.Single(asCreator.Rows);

        var asAssignee1 = await new ListDealsQueryHandler(db, new FakeCurrentUserService(assignee1))
            .Handle(listQuery, CancellationToken.None);
        Assert.Single(asAssignee1.Rows);

        var asAssignee2 = await new ListDealsQueryHandler(db, new FakeCurrentUserService(assignee2))
            .Handle(listQuery, CancellationToken.None);
        Assert.Single(asAssignee2.Rows);
        Assert.Equal(2, asAssignee2.Rows[0].Assignees.Count);

        var asOutsider = await new ListDealsQueryHandler(db, new FakeCurrentUserService(outsiderId))
            .Handle(listQuery, CancellationToken.None);
        Assert.Empty(asOutsider.Rows);
    }

    [Fact]
    public async Task ListDeals_status_filter_narrows_to_only_the_requested_status()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var pendingId = await CreateDealAsync(db, seed, []);
        var wonId = await CreateDealAsync(db, seed, []);
        await new MarkDealWonCommandHandler(db).Handle(new MarkDealWonCommand(seed.OrganizationId, wonId), CancellationToken.None);

        var handler = new ListDealsQueryHandler(db, new FakeCurrentUserService(seed.AdminUserId));

        var pendingOnly = await handler.Handle(
            new ListDealsQuery(seed.OrganizationId, null, DealStatus.Pending), CancellationToken.None);
        Assert.Equal(pendingId, Assert.Single(pendingOnly.Rows).Id);

        var wonOnly = await handler.Handle(
            new ListDealsQuery(seed.OrganizationId, null, DealStatus.Won), CancellationToken.None);
        Assert.Equal(wonId, Assert.Single(wonOnly.Rows).Id);

        var all = await handler.Handle(new ListDealsQuery(seed.OrganizationId, null, null), CancellationToken.None);
        Assert.Equal(2, all.Rows.Count);
    }

    [Fact]
    public async Task ListDeals_never_returns_a_deal_from_a_different_organization()
    {
        var db = TestAppDbContext.Create();
        var seedA = await SeedAsync(db);
        var seedB = await SeedAsync(db);

        await CreateDealAsync(db, seedA, []);
        await CreateDealAsync(db, seedB, []);

        var handler = new ListDealsQueryHandler(db, new FakeCurrentUserService(seedA.AdminUserId));
        var result = await handler.Handle(new ListDealsQuery(seedA.OrganizationId, null, null), CancellationToken.None);

        Assert.Single(result.Rows);
    }

    private sealed record Seed(Guid OrganizationId, Guid ContactId, Guid DealStageId, Guid AdminUserId);

    private static async Task<Seed> SeedAsync(IAppDbContext db)
    {
        var organizationId = Guid.NewGuid();
        var numberGenerator = new FakeDocumentNumberGenerator();

        var contact = await new CreateContactCommandHandler(db, numberGenerator).Handle(
            new CreateContactCommand(organizationId, ContactType.Customer, "Acme Retail", null, null, null, null, null, 0m),
            CancellationToken.None);

        var dealStage = DealStage.Create(organizationId, "Qualified", 1, "#0d6efd");
        db.DealStages.Add(dealStage);

        var adminUserId = await CreateAcceptedMemberAsync(db, organizationId, "Admin User", "admin@example.com");

        return new Seed(organizationId, contact.Id, dealStage.Id, adminUserId);
    }

    private static async Task<Guid> CreateAcceptedMemberAsync(
        IAppDbContext db, Guid organizationId, string fullName, string email)
    {
        var user = User.Register(fullName, email, "9800000000", "hash");
        db.Users.Add(user);
        db.OrganizationMemberships.Add(OrganizationMembership.CreateAccepted(organizationId, user.Id, MembershipRole.Member));
        await db.SaveChangesAsync(CancellationToken.None);
        return user.Id;
    }

    private static async Task<Guid> CreateDealAsync(IAppDbContext db, Seed seed, IReadOnlyList<Guid> assigneeUserIds)
    {
        var result = await new CreateDealCommandHandler(db, new FakeCurrentUserService(seed.AdminUserId)).Handle(
            new CreateDealCommand(
                seed.OrganizationId, seed.ContactId, "New deal", assigneeUserIds, null, null, 1000m, null, false),
            CancellationToken.None);
        return result.Id;
    }
}
