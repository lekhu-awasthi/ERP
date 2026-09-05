using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Configuration.Commands.CreateCustomTemplate;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Configuration;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Configuration;

public class CreateCustomTemplateCommandHandlerTests
{
    [Fact]
    public async Task Handle_the_first_template_for_a_type_becomes_default()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var handler = new CreateCustomTemplateCommandHandler(db);

        var result = await handler.Handle(
            new CreateCustomTemplateCommand(organizationId, "Standard Letter", CustomTemplateType.TermsAndConditions, "Hello $[ContactName]$,"),
            CancellationToken.None);

        Assert.True(result.IsDefault);
        var template = await db.CustomTemplates.SingleAsync(x => x.Id == result.Id);
        Assert.True(template.IsDefault);
    }

    [Fact]
    public async Task Handle_a_second_template_for_the_same_type_is_not_default()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        db.CustomTemplates.Add(CustomTemplate.Create(organizationId, "Standard Letter", CustomTemplateType.TermsAndConditions, "Hello,", isDefault: true));
        await db.SaveChangesAsync();

        var handler = new CreateCustomTemplateCommandHandler(db);

        var result = await handler.Handle(
            new CreateCustomTemplateCommand(organizationId, "Formal Letter", CustomTemplateType.TermsAndConditions, "Dear Sir/Madam,"),
            CancellationToken.None);

        Assert.False(result.IsDefault);
    }

    [Fact]
    public async Task Handle_throws_conflict_for_duplicate_name_on_the_same_type()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        db.CustomTemplates.Add(CustomTemplate.Create(organizationId, "Standard Letter", CustomTemplateType.TermsAndConditions, "Hello,", isDefault: true));
        await db.SaveChangesAsync();

        var handler = new CreateCustomTemplateCommandHandler(db);

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new CreateCustomTemplateCommand(organizationId, "Standard Letter", CustomTemplateType.TermsAndConditions, "Hi,"), CancellationToken.None));
    }
}
