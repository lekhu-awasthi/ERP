using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Configuration.Commands.SetDefaultPrintingTemplate;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Common;
using ErpApp.Domain.Configuration;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Configuration;

public class SetDefaultPrintingTemplateCommandHandlerTests
{
    [Fact]
    public async Task Handle_moves_the_default_flag_to_the_target_and_clears_the_previous_one()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var standard = PrintingTemplate.Create(organizationId, "Standard", DocumentType.Invoice, isDefault: true);
        var modern = PrintingTemplate.Create(organizationId, "Modern", DocumentType.Invoice, isDefault: false);
        db.PrintingTemplates.AddRange(standard, modern);
        await db.SaveChangesAsync();

        var handler = new SetDefaultPrintingTemplateCommandHandler(db);
        await handler.Handle(new SetDefaultPrintingTemplateCommand(organizationId, modern.Id), CancellationToken.None);

        var reloadedStandard = await db.PrintingTemplates.SingleAsync(x => x.Id == standard.Id);
        var reloadedModern = await db.PrintingTemplates.SingleAsync(x => x.Id == modern.Id);
        Assert.False(reloadedStandard.IsDefault);
        Assert.True(reloadedModern.IsDefault);
    }

    [Fact]
    public async Task Handle_does_not_affect_a_default_template_for_a_different_document_type()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var invoiceDefault = PrintingTemplate.Create(organizationId, "Standard", DocumentType.Invoice, isDefault: true);
        var quotationCandidate = PrintingTemplate.Create(organizationId, "Standard", DocumentType.Quotation, isDefault: false);
        db.PrintingTemplates.AddRange(invoiceDefault, quotationCandidate);
        await db.SaveChangesAsync();

        var handler = new SetDefaultPrintingTemplateCommandHandler(db);
        await handler.Handle(new SetDefaultPrintingTemplateCommand(organizationId, quotationCandidate.Id), CancellationToken.None);

        var reloadedInvoiceDefault = await db.PrintingTemplates.SingleAsync(x => x.Id == invoiceDefault.Id);
        Assert.True(reloadedInvoiceDefault.IsDefault);
    }

    [Fact]
    public async Task Handle_throws_not_found_for_unknown_id()
    {
        var db = TestAppDbContext.Create();
        var handler = new SetDefaultPrintingTemplateCommandHandler(db);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new SetDefaultPrintingTemplateCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None));
    }
}
