using ErpApp.Domain.Common;
using ErpApp.Domain.Workflow;
using ErpApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Api.IntegrationTests;

/// <summary>
/// Proves Audit's "no code path can update or delete a row" exit criterion (roadmap Phase 16d) as
/// a real mechanism -- AppDbContext.SaveChangesAsync's own override -- not just the absence of an
/// Update/Delete handler. Runs against the InMemory provider directly (no Testcontainers/Docker
/// needed, unlike this project's other tests): the override only inspects ChangeTracker state, a
/// behavior identical across every EF Core provider.
/// </summary>
public sealed class AuditImmutabilityTests
{
    private static AppDbContext CreateContext() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task SaveChangesAsync_throws_when_an_audit_row_is_marked_modified()
    {
        await using var db = CreateContext();
        var audit = Audit.Create(Guid.NewGuid(), Guid.NewGuid(), "Create", DocumentType.Invoice, Guid.NewGuid());
        db.Audits.Add(audit);
        await db.SaveChangesAsync();

        db.Entry(audit).State = EntityState.Modified;

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task SaveChangesAsync_throws_when_an_audit_row_is_removed()
    {
        await using var db = CreateContext();
        var audit = Audit.Create(Guid.NewGuid(), Guid.NewGuid(), "Void", DocumentType.Invoice, Guid.NewGuid());
        db.Audits.Add(audit);
        await db.SaveChangesAsync();

        db.Audits.Remove(audit);

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task SaveChangesAsync_allows_inserting_a_new_audit_row()
    {
        await using var db = CreateContext();
        var audit = Audit.Create(Guid.NewGuid(), Guid.NewGuid(), "Approve", DocumentType.Invoice, Guid.NewGuid());
        db.Audits.Add(audit);

        var affected = await db.SaveChangesAsync();

        Assert.Equal(1, affected);
    }
}
