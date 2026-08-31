using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Configuration.Commands.CreatePrintingTemplate;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Common;
using ErpApp.Domain.Configuration;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Configuration;

public class CreatePrintingTemplateCommandHandlerTests
{
    [Fact]
    public async Task Handle_the_first_template_for_a_document_type_becomes_default()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var handler = new CreatePrintingTemplateCommandHandler(db);

        var result = await handler.Handle(
            new CreatePrintingTemplateCommand(organizationId, "Standard", DocumentType.Invoice), CancellationToken.None);

        Assert.True(result.IsDefault);
        var template = await db.PrintingTemplates.SingleAsync(x => x.Id == result.Id);
        Assert.True(template.IsDefault);
    }

    [Fact]
    public async Task Handle_a_second_template_for_the_same_document_type_is_not_default()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        db.PrintingTemplates.Add(PrintingTemplate.Create(organizationId, "Standard", DocumentType.Invoice, isDefault: true));
        await db.SaveChangesAsync();

        var handler = new CreatePrintingTemplateCommandHandler(db);

        var result = await handler.Handle(
            new CreatePrintingTemplateCommand(organizationId, "Modern", DocumentType.Invoice), CancellationToken.None);

        Assert.False(result.IsDefault);
    }

    [Fact]
    public async Task Handle_allows_the_same_name_for_a_different_document_type()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        db.PrintingTemplates.Add(PrintingTemplate.Create(organizationId, "Standard", DocumentType.Invoice, isDefault: true));
        await db.SaveChangesAsync();

        var handler = new CreatePrintingTemplateCommandHandler(db);

        var result = await handler.Handle(
            new CreatePrintingTemplateCommand(organizationId, "Standard", DocumentType.Quotation), CancellationToken.None);

        Assert.True(result.IsDefault);
    }

    [Fact]
    public async Task Handle_throws_conflict_for_duplicate_name_on_the_same_document_type()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        db.PrintingTemplates.Add(PrintingTemplate.Create(organizationId, "Standard", DocumentType.Invoice, isDefault: true));
        await db.SaveChangesAsync();

        var handler = new CreatePrintingTemplateCommandHandler(db);

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new CreatePrintingTemplateCommand(organizationId, "Standard", DocumentType.Invoice), CancellationToken.None));
    }
}
