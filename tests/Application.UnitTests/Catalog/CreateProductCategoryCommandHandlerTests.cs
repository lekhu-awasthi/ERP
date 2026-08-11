using ErpApp.Application.Catalog.Commands.CreateProductCategory;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Catalog;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Catalog;

public class CreateProductCategoryCommandHandlerTests
{
    [Fact]
    public async Task Handle_creates_root_category()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var handler = new CreateProductCategoryCommandHandler(db);

        var result = await handler.Handle(
            new CreateProductCategoryCommand(organizationId, "Electronics", null), CancellationToken.None);

        var category = await db.ProductCategories.SingleAsync(x => x.Id == result.Id);
        Assert.Null(category.ParentCategoryId);
    }

    [Fact]
    public async Task Handle_throws_conflict_when_name_already_used_in_organization()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        db.ProductCategories.Add(ProductCategory.Create(organizationId, "Electronics", null));
        await db.SaveChangesAsync();

        var handler = new CreateProductCategoryCommandHandler(db);

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new CreateProductCategoryCommand(organizationId, "Electronics", null), CancellationToken.None));
    }
}
