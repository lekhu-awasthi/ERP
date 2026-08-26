using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Configuration.Commands.UpdateCostTerm;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Configuration;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Configuration;

public class UpdateCostTermCommandHandlerTests
{
    [Fact]
    public async Task Handle_updates_name_category_and_active_flag()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var costTerm = CostTerm.Create(organizationId, "Freight", CostTermCategory.AdditionalCost);
        db.CostTerms.Add(costTerm);
        await db.SaveChangesAsync();

        var handler = new UpdateCostTermCommandHandler(db);

        var result = await handler.Handle(
            new UpdateCostTermCommand(organizationId, costTerm.Id, "Machine Hours", CostTermCategory.ProductionCost, false),
            CancellationToken.None);

        Assert.Equal("Machine Hours", result.Name);
        Assert.Equal(CostTermCategory.ProductionCost, result.Category);
        Assert.False(result.IsActive);

        var reloaded = await db.CostTerms.SingleAsync(x => x.Id == costTerm.Id);
        Assert.Equal("Machine Hours", reloaded.Name);
    }

    [Fact]
    public async Task Handle_throws_not_found_for_unknown_id()
    {
        var db = TestAppDbContext.Create();
        var handler = new UpdateCostTermCommandHandler(db);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new UpdateCostTermCommand(Guid.NewGuid(), Guid.NewGuid(), "Freight", CostTermCategory.AdditionalCost, true),
            CancellationToken.None));
    }

    [Fact]
    public async Task Handle_throws_not_found_for_a_cost_term_in_another_organization()
    {
        var db = TestAppDbContext.Create();
        var costTerm = CostTerm.Create(Guid.NewGuid(), "Freight", CostTermCategory.AdditionalCost);
        db.CostTerms.Add(costTerm);
        await db.SaveChangesAsync();

        var handler = new UpdateCostTermCommandHandler(db);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new UpdateCostTermCommand(Guid.NewGuid(), costTerm.Id, "Freight", CostTermCategory.AdditionalCost, true),
            CancellationToken.None));
    }

    [Fact]
    public async Task Handle_throws_conflict_when_renaming_onto_another_term_in_the_same_category()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var freight = CostTerm.Create(organizationId, "Freight", CostTermCategory.AdditionalCost);
        var insurance = CostTerm.Create(organizationId, "Insurance", CostTermCategory.AdditionalCost);
        db.CostTerms.AddRange(freight, insurance);
        await db.SaveChangesAsync();

        var handler = new UpdateCostTermCommandHandler(db);

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new UpdateCostTermCommand(organizationId, freight.Id, "Insurance", CostTermCategory.AdditionalCost, true),
            CancellationToken.None));
    }

    [Fact]
    public async Task Handle_allows_moving_a_term_into_a_category_that_has_the_same_name_free()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var freight = CostTerm.Create(organizationId, "Freight", CostTermCategory.AdditionalCost);
        db.CostTerms.Add(freight);
        await db.SaveChangesAsync();

        var handler = new UpdateCostTermCommandHandler(db);

        var result = await handler.Handle(
            new UpdateCostTermCommand(organizationId, freight.Id, "Freight", CostTermCategory.ProductionCost, true),
            CancellationToken.None);

        Assert.Equal(CostTermCategory.ProductionCost, result.Category);
    }
}
