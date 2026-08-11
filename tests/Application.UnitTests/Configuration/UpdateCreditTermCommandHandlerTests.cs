using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Configuration.Commands.UpdateCreditTerm;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Configuration;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Configuration;

public class UpdateCreditTermCommandHandlerTests
{
    [Fact]
    public async Task Handle_updates_name_due_days_and_active_flag()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var creditTerm = CreditTerm.Create(organizationId, "Net 30", 30);
        db.CreditTerms.Add(creditTerm);
        await db.SaveChangesAsync();

        var handler = new UpdateCreditTermCommandHandler(db);

        var result = await handler.Handle(
            new UpdateCreditTermCommand(organizationId, creditTerm.Id, "Net 45", 45, false), CancellationToken.None);

        Assert.Equal("Net 45", result.Name);
        Assert.Equal(45, result.DueDays);
        Assert.False(result.IsActive);

        var reloaded = await db.CreditTerms.SingleAsync(x => x.Id == creditTerm.Id);
        Assert.Equal("Net 45", reloaded.Name);
    }

    [Fact]
    public async Task Handle_throws_not_found_for_unknown_id()
    {
        var db = TestAppDbContext.Create();
        var handler = new UpdateCreditTermCommandHandler(db);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new UpdateCreditTermCommand(Guid.NewGuid(), Guid.NewGuid(), "Net 45", 45, true), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_throws_conflict_when_renaming_to_another_credit_terms_name()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var netThirty = CreditTerm.Create(organizationId, "Net 30", 30);
        var netSixty = CreditTerm.Create(organizationId, "Net 60", 60);
        db.CreditTerms.AddRange(netThirty, netSixty);
        await db.SaveChangesAsync();

        var handler = new UpdateCreditTermCommandHandler(db);

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new UpdateCreditTermCommand(organizationId, netThirty.Id, "Net 60", 30, true), CancellationToken.None));
    }
}
