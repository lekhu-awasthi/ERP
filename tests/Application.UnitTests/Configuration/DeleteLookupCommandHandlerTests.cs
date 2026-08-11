using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Configuration.Commands.DeleteLookup;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Configuration;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Configuration;

/// <summary>Exercises the generic delete handler through CreditTerm -- the pattern is identical for every lookup type.</summary>
public class DeleteLookupCommandHandlerTests
{
    [Fact]
    public async Task Handle_removes_the_row()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var creditTerm = CreditTerm.Create(organizationId, "Net 30", 30);
        db.CreditTerms.Add(creditTerm);
        await db.SaveChangesAsync();

        var handler = new DeleteLookupCommandHandler<CreditTerm>(db);

        await handler.Handle(new DeleteLookupCommand<CreditTerm>(organizationId, creditTerm.Id), CancellationToken.None);

        Assert.False(await db.CreditTerms.AnyAsync(x => x.Id == creditTerm.Id));
    }

    [Fact]
    public async Task Handle_throws_not_found_for_unknown_id()
    {
        var db = TestAppDbContext.Create();
        var handler = new DeleteLookupCommandHandler<CreditTerm>(db);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new DeleteLookupCommand<CreditTerm>(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_throws_not_found_when_row_belongs_to_a_different_organization()
    {
        var db = TestAppDbContext.Create();
        var creditTerm = CreditTerm.Create(Guid.NewGuid(), "Net 30", 30);
        db.CreditTerms.Add(creditTerm);
        await db.SaveChangesAsync();

        var handler = new DeleteLookupCommandHandler<CreditTerm>(db);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new DeleteLookupCommand<CreditTerm>(Guid.NewGuid(), creditTerm.Id), CancellationToken.None));
    }
}
