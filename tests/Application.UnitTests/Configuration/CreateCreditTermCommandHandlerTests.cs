using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Configuration.Commands.CreateCreditTerm;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Configuration;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Configuration;

public class CreateCreditTermCommandHandlerTests
{
    [Fact]
    public async Task Handle_creates_credit_term()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var handler = new CreateCreditTermCommandHandler(db);

        var result = await handler.Handle(new CreateCreditTermCommand(organizationId, "Net 30", 30), CancellationToken.None);

        var creditTerm = await db.CreditTerms.SingleAsync(x => x.Id == result.Id);
        Assert.Equal(organizationId, creditTerm.OrganizationId);
        Assert.Equal("Net 30", creditTerm.Name);
        Assert.Equal(30, creditTerm.DueDays);
        Assert.True(creditTerm.IsActive);
    }

    [Fact]
    public async Task Handle_throws_conflict_when_name_already_used_in_organization()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        db.CreditTerms.Add(CreditTerm.Create(organizationId, "Net 30", 30));
        await db.SaveChangesAsync();

        var handler = new CreateCreditTermCommandHandler(db);

        await Assert.ThrowsAsync<ConflictException>(
            () => handler.Handle(new CreateCreditTermCommand(organizationId, "Net 30", 45), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_allows_same_name_in_a_different_organization()
    {
        var db = TestAppDbContext.Create();
        db.CreditTerms.Add(CreditTerm.Create(Guid.NewGuid(), "Net 30", 30));
        await db.SaveChangesAsync();

        var handler = new CreateCreditTermCommandHandler(db);

        var result = await handler.Handle(
            new CreateCreditTermCommand(Guid.NewGuid(), "Net 30", 30), CancellationToken.None);

        Assert.NotEqual(Guid.Empty, result.Id);
    }
}
