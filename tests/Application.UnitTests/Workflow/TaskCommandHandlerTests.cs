using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Contacts.Commands.CreateContact;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Application.Workflow.Commands.CreateTask;
using ErpApp.Application.Workflow.Commands.UpdateTask;
using ErpApp.Application.Workflow.Commands.UpdateTaskStatus;
using ErpApp.Application.Workflow.Queries.ListTasks;
using ErpApp.Domain.Configuration;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Identity;
using ErpApp.Domain.Tenancy;
using ErpApp.Domain.Workflow;

namespace ErpApp.Application.UnitTests.Workflow;

/// <summary>
/// Covers roadmap Phase 13's Task feature -- the second Workflow-context feature, and the first
/// real aggregate (not a pure read) in that bounded context. Focuses on the behaviors the brief
/// specifically calls out as needing real test coverage: parent/assignee existence validation,
/// the forward-only status state machine, IsPrivate visibility enforcement, the Status? filter, and
/// parent-scope isolation (a Task attached to a Contact must never leak into an Organization-scoped
/// list for the same org, or vice versa).
/// </summary>
public class TaskCommandHandlerTests
{
    [Fact]
    public async Task Create_rejects_a_parent_id_that_does_not_resolve_to_an_existing_contact()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var handler = new CreateTaskCommandHandler(db, new FakeCurrentUserService(seed.AdminUserId));

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new CreateTaskCommand(
                seed.OrganizationId, TaskParentType.Contact, Guid.NewGuid(), "Follow up", null, null, null,
                seed.TaskTypeId, TaskPriority.Normal, false),
            CancellationToken.None));
    }

    [Fact]
    public async Task Create_rejects_an_assignee_who_is_not_an_accepted_member_of_the_organization()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var handler = new CreateTaskCommandHandler(db, new FakeCurrentUserService(seed.AdminUserId));

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new CreateTaskCommand(
                seed.OrganizationId, TaskParentType.Contact, seed.ContactId, "Follow up", null, Guid.NewGuid(), null,
                seed.TaskTypeId, TaskPriority.Normal, false),
            CancellationToken.None));
    }

    [Fact]
    public async Task Create_succeeds_against_an_organization_parent_with_no_assignee()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var handler = new CreateTaskCommandHandler(db, new FakeCurrentUserService(seed.AdminUserId));

        var result = await handler.Handle(
            new CreateTaskCommand(
                seed.OrganizationId, TaskParentType.Organization, seed.OrganizationId, "Renew subscription", null,
                null, null, seed.TaskTypeId, TaskPriority.Urgent, false),
            CancellationToken.None);

        Assert.Equal(WorkTaskStatus.Pending, result.Status);
        Assert.Equal(TaskParentType.Organization, result.ParentType);
    }

    [Fact]
    public async Task UpdateTaskStatus_accepts_a_forward_move_and_rejects_a_backward_or_no_op_move()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var taskId = await CreateTaskAsync(db, seed);

        var handler = new UpdateTaskStatusCommandHandler(db);

        var started = await handler.Handle(
            new UpdateTaskStatusCommand(seed.OrganizationId, taskId, WorkTaskStatus.Started), CancellationToken.None);
        Assert.Equal(WorkTaskStatus.Started, started.Status);

        // Backward -- rejected.
        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new UpdateTaskStatusCommand(seed.OrganizationId, taskId, WorkTaskStatus.Pending), CancellationToken.None));

        // No-op (same status) -- also rejected, not silently accepted.
        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new UpdateTaskStatusCommand(seed.OrganizationId, taskId, WorkTaskStatus.Started), CancellationToken.None));

        var done = await handler.Handle(
            new UpdateTaskStatusCommand(seed.OrganizationId, taskId, WorkTaskStatus.Done), CancellationToken.None);
        Assert.Equal(WorkTaskStatus.Done, done.Status);

        // Once Done, UpdateTaskCommand must also reject further edits (EnsureNotDone).
        var updateHandler = new UpdateTaskCommandHandler(db);
        await Assert.ThrowsAsync<ConflictException>(() => updateHandler.Handle(
            new UpdateTaskCommand(
                seed.OrganizationId, taskId, "Renamed", null, null, null, seed.TaskTypeId, TaskPriority.Normal, false),
            CancellationToken.None));
    }

    [Fact]
    public async Task UpdateTaskStatus_allows_skipping_started_straight_from_pending_to_done()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var taskId = await CreateTaskAsync(db, seed);

        var handler = new UpdateTaskStatusCommandHandler(db);
        var result = await handler.Handle(
            new UpdateTaskStatusCommand(seed.OrganizationId, taskId, WorkTaskStatus.Done), CancellationToken.None);

        Assert.Equal(WorkTaskStatus.Done, result.Status);
    }

    [Fact]
    public async Task ListTasks_hides_a_private_task_from_a_user_who_is_neither_creator_nor_assignee()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var assigneeId = await CreateAcceptedMemberAsync(db, seed.OrganizationId, "Assignee", "assignee@example.com");
        var outsiderId = await CreateAcceptedMemberAsync(db, seed.OrganizationId, "Outsider", "outsider@example.com");

        var createHandler = new CreateTaskCommandHandler(db, new FakeCurrentUserService(seed.AdminUserId));
        await createHandler.Handle(
            new CreateTaskCommand(
                seed.OrganizationId, TaskParentType.Contact, seed.ContactId, "Confidential follow-up", null,
                assigneeId, null, seed.TaskTypeId, TaskPriority.Normal, true),
            CancellationToken.None);

        var listQuery = new ListTasksQuery(seed.OrganizationId, TaskParentType.Contact, seed.ContactId, null);

        var asCreator = await new ListTasksQueryHandler(db, new FakeCurrentUserService(seed.AdminUserId))
            .Handle(listQuery, CancellationToken.None);
        Assert.Single(asCreator.Rows);

        var asAssignee = await new ListTasksQueryHandler(db, new FakeCurrentUserService(assigneeId))
            .Handle(listQuery, CancellationToken.None);
        Assert.Single(asAssignee.Rows);

        var asOutsider = await new ListTasksQueryHandler(db, new FakeCurrentUserService(outsiderId))
            .Handle(listQuery, CancellationToken.None);
        Assert.Empty(asOutsider.Rows);
    }

    [Fact]
    public async Task ListTasks_status_filter_narrows_to_only_the_requested_status()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var pendingId = await CreateTaskAsync(db, seed);
        var startedId = await CreateTaskAsync(db, seed);
        await new UpdateTaskStatusCommandHandler(db).Handle(
            new UpdateTaskStatusCommand(seed.OrganizationId, startedId, WorkTaskStatus.Started), CancellationToken.None);

        var handler = new ListTasksQueryHandler(db, new FakeCurrentUserService(seed.AdminUserId));

        var pendingOnly = await handler.Handle(
            new ListTasksQuery(seed.OrganizationId, TaskParentType.Contact, seed.ContactId, WorkTaskStatus.Pending),
            CancellationToken.None);
        Assert.Equal(pendingId, Assert.Single(pendingOnly.Rows).Id);

        var startedOnly = await handler.Handle(
            new ListTasksQuery(seed.OrganizationId, TaskParentType.Contact, seed.ContactId, WorkTaskStatus.Started),
            CancellationToken.None);
        Assert.Equal(startedId, Assert.Single(startedOnly.Rows).Id);

        var all = await handler.Handle(
            new ListTasksQuery(seed.OrganizationId, TaskParentType.Contact, seed.ContactId, null), CancellationToken.None);
        Assert.Equal(2, all.Rows.Count);
    }

    [Fact]
    public async Task ListTasks_never_crosses_between_a_contact_parent_and_the_organization_parent()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var createHandler = new CreateTaskCommandHandler(db, new FakeCurrentUserService(seed.AdminUserId));
        var contactTaskId = (await createHandler.Handle(
            new CreateTaskCommand(
                seed.OrganizationId, TaskParentType.Contact, seed.ContactId, "Call customer", null, null, null,
                seed.TaskTypeId, TaskPriority.Normal, false),
            CancellationToken.None)).Id;
        var orgTaskId = (await createHandler.Handle(
            new CreateTaskCommand(
                seed.OrganizationId, TaskParentType.Organization, seed.OrganizationId, "Renew subscription", null,
                null, null, seed.TaskTypeId, TaskPriority.Normal, false),
            CancellationToken.None)).Id;

        var listHandler = new ListTasksQueryHandler(db, new FakeCurrentUserService(seed.AdminUserId));

        var contactRows = await listHandler.Handle(
            new ListTasksQuery(seed.OrganizationId, TaskParentType.Contact, seed.ContactId, null), CancellationToken.None);
        Assert.Equal(contactTaskId, Assert.Single(contactRows.Rows).Id);

        var orgRows = await listHandler.Handle(
            new ListTasksQuery(seed.OrganizationId, TaskParentType.Organization, seed.OrganizationId, null),
            CancellationToken.None);
        Assert.Equal(orgTaskId, Assert.Single(orgRows.Rows).Id);
    }

    [Fact]
    public async Task ListTasks_never_returns_a_task_from_a_different_organization()
    {
        var db = TestAppDbContext.Create();
        var seedA = await SeedAsync(db);
        var seedB = await SeedAsync(db);

        await CreateTaskAsync(db, seedA);
        await CreateTaskAsync(db, seedB);

        var handler = new ListTasksQueryHandler(db, new FakeCurrentUserService(seedA.AdminUserId));
        var result = await handler.Handle(
            new ListTasksQuery(seedA.OrganizationId, TaskParentType.Contact, seedA.ContactId, null),
            CancellationToken.None);

        Assert.Single(result.Rows);
    }

    private sealed record Seed(Guid OrganizationId, Guid ContactId, Guid TaskTypeId, Guid AdminUserId);

    private static async Task<Seed> SeedAsync(IAppDbContext db)
    {
        var organizationId = Guid.NewGuid();
        var numberGenerator = new FakeDocumentNumberGenerator();

        var contact = await new CreateContactCommandHandler(db, numberGenerator).Handle(
            new CreateContactCommand(organizationId, ContactType.Customer, "Acme Retail", null, null, null, null, null, 0m),
            CancellationToken.None);

        var taskType = TaskType.Create(organizationId, "Follow up", "#0d6efd");
        db.TaskTypes.Add(taskType);

        var adminUserId = await CreateAcceptedMemberAsync(db, organizationId, "Admin User", "admin@example.com");

        return new Seed(organizationId, contact.Id, taskType.Id, adminUserId);
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

    private static async Task<Guid> CreateTaskAsync(IAppDbContext db, Seed seed)
    {
        var result = await new CreateTaskCommandHandler(db, new FakeCurrentUserService(seed.AdminUserId)).Handle(
            new CreateTaskCommand(
                seed.OrganizationId, TaskParentType.Contact, seed.ContactId, "Follow up", null, null, null,
                seed.TaskTypeId, TaskPriority.Normal, false),
            CancellationToken.None);
        return result.Id;
    }
}
