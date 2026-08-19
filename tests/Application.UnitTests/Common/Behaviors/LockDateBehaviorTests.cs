using ErpApp.Application.Accounting.Commands.ApproveJournalVoucher;
using ErpApp.Application.Accounting.Commands.CreateJournalVoucher;
using ErpApp.Application.Common.Behaviors;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.UnitTests.Common.Behaviors;

/// <summary>
/// Covers Organization.LockDate enforcement (roadmap Phase 16a, NFR-3.4) -- the one shared
/// pipeline behavior every create/edit/approve/void of a lock-date-sensitive document flows
/// through. JournalVoucher's Create/Approve commands stand in for the ILockDateSensitive/
/// ILockDateSensitiveDocument split every other type in this codebase follows identically.
/// </summary>
public class LockDateBehaviorTests
{
    [Fact]
    public async Task Handle_throws_conflict_for_a_create_command_dated_on_the_lock_date()
    {
        var db = TestAppDbContext.Create();
        var organization = Organization.Create(
            "Acme Traders", "Retail", null, new DateOnly(2026, 1, 1), true,
            "acme-traders", null, null, null, null, Guid.NewGuid());
        organization.SetLockDate(new DateOnly(2026, 1, 31));
        db.Organizations.Add(organization);
        await db.SaveChangesAsync();

        var behavior = new LockDateBehavior<CreateJournalVoucherCommand, CreateJournalVoucherResult>(db);
        var command = new CreateJournalVoucherCommand(organization.Id, new DateOnly(2026, 1, 31), null, []);

        await Assert.ThrowsAsync<ConflictException>(() => behavior.Handle(
            command, () => throw new InvalidOperationException("next() should not have been called."), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_throws_conflict_for_a_create_command_dated_before_the_lock_date()
    {
        var db = TestAppDbContext.Create();
        var organization = Organization.Create(
            "Acme Traders", "Retail", null, new DateOnly(2026, 1, 1), true,
            "acme-traders", null, null, null, null, Guid.NewGuid());
        organization.SetLockDate(new DateOnly(2026, 1, 31));
        db.Organizations.Add(organization);
        await db.SaveChangesAsync();

        var behavior = new LockDateBehavior<CreateJournalVoucherCommand, CreateJournalVoucherResult>(db);
        var command = new CreateJournalVoucherCommand(organization.Id, new DateOnly(2026, 1, 15), null, []);

        await Assert.ThrowsAsync<ConflictException>(() => behavior.Handle(
            command, () => throw new InvalidOperationException("next() should not have been called."), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_allows_a_create_command_dated_after_the_lock_date()
    {
        var db = TestAppDbContext.Create();
        var organization = Organization.Create(
            "Acme Traders", "Retail", null, new DateOnly(2026, 1, 1), true,
            "acme-traders", null, null, null, null, Guid.NewGuid());
        organization.SetLockDate(new DateOnly(2026, 1, 31));
        db.Organizations.Add(organization);
        await db.SaveChangesAsync();

        var behavior = new LockDateBehavior<CreateJournalVoucherCommand, CreateJournalVoucherResult>(db);
        var command = new CreateJournalVoucherCommand(organization.Id, new DateOnly(2026, 2, 1), null, []);
        var expected = new CreateJournalVoucherResult(Guid.NewGuid(), "DRAFT", JournalVoucherStatus.Draft);

        var result = await behavior.Handle(command, () => Task.FromResult(expected), CancellationToken.None);

        Assert.Same(expected, result);
    }

    [Fact]
    public async Task Handle_allows_any_date_when_no_lock_date_is_set()
    {
        var db = TestAppDbContext.Create();
        var organization = Organization.Create(
            "Acme Traders", "Retail", null, new DateOnly(2026, 1, 1), true,
            "acme-traders", null, null, null, null, Guid.NewGuid());
        db.Organizations.Add(organization);
        await db.SaveChangesAsync();

        var behavior = new LockDateBehavior<CreateJournalVoucherCommand, CreateJournalVoucherResult>(db);
        var command = new CreateJournalVoucherCommand(organization.Id, new DateOnly(2020, 1, 1), null, []);
        var expected = new CreateJournalVoucherResult(Guid.NewGuid(), "DRAFT", JournalVoucherStatus.Draft);

        var result = await behavior.Handle(command, () => Task.FromResult(expected), CancellationToken.None);

        Assert.Same(expected, result);
    }

    [Fact]
    public async Task Handle_resolves_the_documents_own_date_for_an_approve_command_and_rejects_it_when_backdated()
    {
        var db = TestAppDbContext.Create();
        var organization = Organization.Create(
            "Acme Traders", "Retail", null, new DateOnly(2026, 1, 1), true,
            "acme-traders", null, null, null, null, Guid.NewGuid());
        organization.SetLockDate(new DateOnly(2026, 1, 31));
        db.Organizations.Add(organization);
        var voucher = JournalVoucher.Create(organization.Id, new DateOnly(2026, 1, 10), null);
        db.JournalVouchers.Add(voucher);
        await db.SaveChangesAsync();

        var behavior = new LockDateBehavior<ApproveJournalVoucherCommand, ApproveJournalVoucherResult>(db);
        var command = new ApproveJournalVoucherCommand(organization.Id, voucher.Id);

        await Assert.ThrowsAsync<ConflictException>(() => behavior.Handle(
            command, () => throw new InvalidOperationException("next() should not have been called."), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_allows_an_approve_command_for_a_document_dated_after_the_lock_date()
    {
        var db = TestAppDbContext.Create();
        var organization = Organization.Create(
            "Acme Traders", "Retail", null, new DateOnly(2026, 1, 1), true,
            "acme-traders", null, null, null, null, Guid.NewGuid());
        organization.SetLockDate(new DateOnly(2026, 1, 31));
        db.Organizations.Add(organization);
        var voucher = JournalVoucher.Create(organization.Id, new DateOnly(2026, 2, 1), null);
        db.JournalVouchers.Add(voucher);
        await db.SaveChangesAsync();

        var behavior = new LockDateBehavior<ApproveJournalVoucherCommand, ApproveJournalVoucherResult>(db);
        var command = new ApproveJournalVoucherCommand(organization.Id, voucher.Id);
        var expected = new ApproveJournalVoucherResult(voucher.Id, "JV-0001", JournalVoucherStatus.Approved, DateTimeOffset.UtcNow);

        var result = await behavior.Handle(command, () => Task.FromResult(expected), CancellationToken.None);

        Assert.Same(expected, result);
    }

    [Fact]
    public async Task Handle_passes_through_a_request_that_implements_neither_lock_date_interface()
    {
        var db = TestAppDbContext.Create();
        var behavior = new LockDateBehavior<Unit, Unit>(db);
        var nextCalled = false;

        await behavior.Handle(Unit.Value, () => { nextCalled = true; return Task.FromResult(Unit.Value); }, CancellationToken.None);

        Assert.True(nextCalled);
    }
}
