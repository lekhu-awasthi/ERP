using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Configuration.Commands.CreateCostTerm;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Configuration;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Configuration;

public class CreateCostTermCommandHandlerTests
{
    [Fact]
    public async Task Handle_creates_cost_term_in_the_given_category()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var handler = new CreateCostTermCommandHandler(db);

        var result = await handler.Handle(
            new CreateCostTermCommand(organizationId, "Freight", CostTermCategory.AdditionalCost), CancellationToken.None);

        var costTerm = await db.CostTerms.SingleAsync(x => x.Id == result.Id);
        Assert.Equal(CostTermCategory.AdditionalCost, costTerm.Category);
        Assert.True(costTerm.IsActive);
    }

    [Fact]
    public async Task Handle_allows_the_same_name_in_the_other_category()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        db.CostTerms.Add(CostTerm.Create(organizationId, "Freight", CostTermCategory.AdditionalCost));
        await db.SaveChangesAsync();

        var handler = new CreateCostTermCommandHandler(db);

        var result = await handler.Handle(
            new CreateCostTermCommand(organizationId, "Freight", CostTermCategory.ProductionCost), CancellationToken.None);

        Assert.NotEqual(Guid.Empty, result.Id);
    }

    [Fact]
    public async Task Handle_allows_the_same_name_in_another_organization()
    {
        var db = TestAppDbContext.Create();
        db.CostTerms.Add(CostTerm.Create(Guid.NewGuid(), "Freight", CostTermCategory.AdditionalCost));
        await db.SaveChangesAsync();

        var handler = new CreateCostTermCommandHandler(db);

        var result = await handler.Handle(
            new CreateCostTermCommand(Guid.NewGuid(), "Freight", CostTermCategory.AdditionalCost), CancellationToken.None);

        Assert.NotEqual(Guid.Empty, result.Id);
    }

    [Fact]
    public async Task Handle_throws_conflict_for_duplicate_name_in_the_same_category()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        db.CostTerms.Add(CostTerm.Create(organizationId, "Freight", CostTermCategory.AdditionalCost));
        await db.SaveChangesAsync();

        var handler = new CreateCostTermCommandHandler(db);

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new CreateCostTermCommand(organizationId, "Freight", CostTermCategory.AdditionalCost), CancellationToken.None));
    }
}
