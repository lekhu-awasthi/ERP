using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Configuration.Commands.CreateReportingTagOption;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Configuration;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Configuration;

public class CreateReportingTagOptionCommandHandlerTests
{
    [Fact]
    public async Task Handle_creates_option_under_existing_category()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var category = ReportingTagCategory.Create(organizationId, "Project");
        db.ReportingTagCategories.Add(category);
        await db.SaveChangesAsync();

        var handler = new CreateReportingTagOptionCommandHandler(db);

        var result = await handler.Handle(
            new CreateReportingTagOptionCommand(organizationId, "Project A", category.Id), CancellationToken.None);

        var option = await db.ReportingTagOptions.SingleAsync(x => x.Id == result.Id);
        Assert.Equal(category.Id, option.CategoryId);
    }

    [Fact]
    public async Task Handle_throws_not_found_when_category_does_not_exist()
    {
        var db = TestAppDbContext.Create();
        var handler = new CreateReportingTagOptionCommandHandler(db);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new CreateReportingTagOptionCommand(Guid.NewGuid(), "Project A", Guid.NewGuid()), CancellationToken.None));
    }
}
