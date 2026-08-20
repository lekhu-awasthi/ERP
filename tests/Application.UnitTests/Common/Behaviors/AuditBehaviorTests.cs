using ErpApp.Application.Accounting.Commands.ApproveJournalVoucher;
using ErpApp.Application.Accounting.Commands.CreateJournalVoucher;
using ErpApp.Application.Common.Behaviors;
using ErpApp.Application.Tenancy.Commands.SetOrganizationLockDate;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.UnitTests.Common.Behaviors;

/// <summary>
/// Covers AuditBehavior (roadmap Phase 16d, architecture-spec.md §3.9). JournalVoucher's Create/
/// Approve commands stand in for the IAuditableRequest/ILockDateSensitiveDocument split every
/// other ApprovableTransaction type in this codebase follows identically -- same precedent
/// LockDateBehaviorTests uses for its own coverage.
/// </summary>
public class AuditBehaviorTests
{
    private static Organization CreateOrganization()
    {
        return Organization.Create(
            "Acme Traders", "Retail", null, new DateOnly(2026, 1, 1), true,
            "acme-traders", null, null, null, null, Guid.NewGuid());
    }

    [Fact]
    public async Task Handle_writes_an_audit_row_for_a_create_command_using_the_responses_id()
    {
        var db = TestAppDbContext.Create();
        var organization = CreateOrganization();
        db.Organizations.Add(organization);
        await db.SaveChangesAsync();

        var userId = Guid.NewGuid();
        var behavior = new AuditBehavior<CreateJournalVoucherCommand, CreateJournalVoucherResult>(
            db, new FakeCurrentUserService(userId));
        var command = new CreateJournalVoucherCommand(organization.Id, new DateOnly(2026, 2, 1), null, []);
        var newId = Guid.NewGuid();
        var expected = new CreateJournalVoucherResult(newId, "JV-0001", JournalVoucherStatus.Draft);

        var result = await behavior.Handle(command, () => Task.FromResult(expected), CancellationToken.None);

        Assert.Same(expected, result);
        var audit = Assert.Single(db.Audits.Local);
        Assert.Equal(organization.Id, audit.OrganizationId);
        Assert.Equal(userId, audit.UserId);
        Assert.Equal("Create", audit.Action);
        Assert.Equal(DocumentType.JournalVoucher, audit.DocumentType);
        Assert.Equal(newId, audit.DocumentId);
    }

    [Fact]
    public async Task Handle_writes_an_audit_row_for_an_approve_command_using_the_commands_own_id()
    {
        var db = TestAppDbContext.Create();
        var organization = CreateOrganization();
        db.Organizations.Add(organization);
        var voucher = JournalVoucher.Create(organization.Id, new DateOnly(2026, 2, 1), null);
        db.JournalVouchers.Add(voucher);
        await db.SaveChangesAsync();

        var userId = Guid.NewGuid();
        var behavior = new AuditBehavior<ApproveJournalVoucherCommand, ApproveJournalVoucherResult>(
            db, new FakeCurrentUserService(userId));
        var command = new ApproveJournalVoucherCommand(organization.Id, voucher.Id);
        var expected = new ApproveJournalVoucherResult(voucher.Id, "JV-0001", JournalVoucherStatus.Approved, DateTimeOffset.UtcNow);

        var result = await behavior.Handle(command, () => Task.FromResult(expected), CancellationToken.None);

        Assert.Same(expected, result);
        var audit = Assert.Single(db.Audits.Local);
        Assert.Equal(userId, audit.UserId);
        Assert.Equal("Approve", audit.Action);
        Assert.Equal(DocumentType.JournalVoucher, audit.DocumentType);
        Assert.Equal(voucher.Id, audit.DocumentId);
    }

    [Fact]
    public async Task Handle_writes_no_audit_row_for_a_command_that_implements_neither_marker_interface()
    {
        var db = TestAppDbContext.Create();
        var organization = CreateOrganization();
        db.Organizations.Add(organization);
        await db.SaveChangesAsync();

        var behavior = new AuditBehavior<SetOrganizationLockDateCommand, SetOrganizationLockDateResult>(
            db, new FakeCurrentUserService(Guid.NewGuid()));
        var command = new SetOrganizationLockDateCommand(organization.Id, new DateOnly(2026, 1, 31));
        var expected = new SetOrganizationLockDateResult(organization.Id, new DateOnly(2026, 1, 31));

        var result = await behavior.Handle(command, () => Task.FromResult(expected), CancellationToken.None);

        Assert.Same(expected, result);
        Assert.Empty(db.Audits.Local);
    }

    [Fact]
    public async Task Handle_writes_no_audit_row_for_a_request_that_is_not_organization_scoped()
    {
        var db = TestAppDbContext.Create();
        var behavior = new AuditBehavior<Unit, Unit>(db, new FakeCurrentUserService(Guid.NewGuid()));
        var nextCalled = false;

        await behavior.Handle(Unit.Value, () => { nextCalled = true; return Task.FromResult(Unit.Value); }, CancellationToken.None);

        Assert.True(nextCalled);
        Assert.Empty(db.Audits.Local);
    }
}
