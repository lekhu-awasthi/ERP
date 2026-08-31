using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Configuration.Commands.UpdatePrintingTemplate;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Common;
using ErpApp.Domain.Configuration;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Configuration;

public class UpdatePrintingTemplateCommandHandlerTests
{
    [Fact]
    public async Task Handle_updates_name_document_type_and_active_flag()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var template = PrintingTemplate.Create(organizationId, "Standard", DocumentType.Invoice, isDefault: true);
        db.PrintingTemplates.Add(template);
        await db.SaveChangesAsync();

        var handler = new UpdatePrintingTemplateCommandHandler(db);

        var result = await handler.Handle(
            new UpdatePrintingTemplateCommand(organizationId, template.Id, "Modern", DocumentType.Invoice, false), CancellationToken.None);

        Assert.Equal("Modern", result.Name);
        Assert.False(result.IsActive);
        Assert.True(result.IsDefault);
    }

    [Fact]
    public async Task Handle_throws_not_found_for_unknown_id()
    {
        var db = TestAppDbContext.Create();
        var handler = new UpdatePrintingTemplateCommandHandler(db);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new UpdatePrintingTemplateCommand(Guid.NewGuid(), Guid.NewGuid(), "Standard", DocumentType.Invoice, true),
            CancellationToken.None));
    }

    [Fact]
    public async Task Handle_throws_conflict_when_renaming_onto_another_template_for_the_same_document_type()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var standard = PrintingTemplate.Create(organizationId, "Standard", DocumentType.Invoice, isDefault: true);
        var modern = PrintingTemplate.Create(organizationId, "Modern", DocumentType.Invoice, isDefault: false);
        db.PrintingTemplates.AddRange(standard, modern);
        await db.SaveChangesAsync();

        var handler = new UpdatePrintingTemplateCommandHandler(db);

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new UpdatePrintingTemplateCommand(organizationId, modern.Id, "Standard", DocumentType.Invoice, true),
            CancellationToken.None));
    }
}
