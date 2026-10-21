using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Printing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Configuration.Commands.UpdatePrintingTemplateLayout;

public sealed class UpdatePrintingTemplateLayoutCommandHandler(IAppDbContext db)
    : IRequestHandler<UpdatePrintingTemplateLayoutCommand, PrintingTemplateLayoutResult>
{
    public async Task<PrintingTemplateLayoutResult> Handle(
        UpdatePrintingTemplateLayoutCommand request, CancellationToken cancellationToken)
    {
        var template = await db.PrintingTemplates.SingleOrDefaultAsync(
            x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Printing template not found.");

        // The validator has refused a null for each of these.
        var hidden = request.HiddenElements!;
        var customFields = request.PrintedCustomFieldIds!;

        await PrintingTemplateChecks.EnsureValidAsync(
            db, request.OrganizationId, template.DocumentType, request.Layout!.Value, hidden, customFields, cancellationToken);

        template.UpdateLayout(request.Layout!.Value, hidden, customFields, request.FooterText, request.CompactMargins!.Value);
        // Phase 95 -- left out of the body, it keeps what it was; never read as "off".
        if (request.PrintSerialNumbers is { } printSerialNumbers)
        {
            template.SetPrintSerialNumbers(printSerialNumbers);
        }

        await db.SaveChangesAsync(cancellationToken);

        return new PrintingTemplateLayoutResult(
            template.Id, template.Layout, template.HiddenElements, template.PrintedCustomFieldIds, template.FooterText,
            template.CompactMargins, template.PrintSerialNumbers);
    }
}
