using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Configuration.Commands.SetDefaultCustomTemplate;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Configuration;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Configuration;

public class SetDefaultCustomTemplateCommandHandlerTests
{
    [Fact]
    public async Task Handle_moves_the_default_flag_to_the_target_and_clears_the_previous_one()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var standard = CustomTemplate.Create(organizationId, "Standard Letter", CustomTemplateType.TermsAndConditions, "Hello,", isDefault: true);
        var formal = CustomTemplate.Create(organizationId, "Formal Letter", CustomTemplateType.TermsAndConditions, "Dear Sir/Madam,", isDefault: false);
        db.CustomTemplates.AddRange(standard, formal);
        await db.SaveChangesAsync();

        var handler = new SetDefaultCustomTemplateCommandHandler(db);
        await handler.Handle(new SetDefaultCustomTemplateCommand(organizationId, formal.Id), CancellationToken.None);

        var reloadedStandard = await db.CustomTemplates.SingleAsync(x => x.Id == standard.Id);
        var reloadedFormal = await db.CustomTemplates.SingleAsync(x => x.Id == formal.Id);
        Assert.False(reloadedStandard.IsDefault);
        Assert.True(reloadedFormal.IsDefault);
    }

    [Fact]
    public async Task Handle_throws_not_found_for_unknown_id()
    {
        var db = TestAppDbContext.Create();
        var handler = new SetDefaultCustomTemplateCommandHandler(db);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new SetDefaultCustomTemplateCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None));
    }
}
