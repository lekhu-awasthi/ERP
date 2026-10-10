using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Configuration;

/// <summary>Phase 75 -- one location's template for one document type.</summary>
public sealed record PrintingTemplateLocationChoiceDto(Guid LocationId, DocumentType DocumentType, Guid PrintingTemplateId);

/// <summary>
/// Phase 75 -- every location's template choices, for the Printing Templates page. Unpaged: at most one row per
/// location and printable type, so a tenant with ten locations has at most 170.
/// </summary>
public sealed record ListPrintingTemplateLocationChoicesQuery(Guid OrganizationId)
    : IRequest<IReadOnlyList<PrintingTemplateLocationChoiceDto>>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.PrintingTemplateView;
}

public sealed class ListPrintingTemplateLocationChoicesQueryHandler(IAppDbContext db)
    : IRequestHandler<ListPrintingTemplateLocationChoicesQuery, IReadOnlyList<PrintingTemplateLocationChoiceDto>>
{
    public async Task<IReadOnlyList<PrintingTemplateLocationChoiceDto>> Handle(
        ListPrintingTemplateLocationChoicesQuery request, CancellationToken cancellationToken)
    {
        var rows = await db.PrintingTemplateLocationChoices
            .Where(x => x.OrganizationId == request.OrganizationId)
            .OrderBy(x => x.LocationId).ThenBy(x => x.DocumentType)
            .Select(x => new { x.LocationId, x.DocumentType, x.PrintingTemplateId })
            .ToListAsync(cancellationToken);

        return rows.Select(x => new PrintingTemplateLocationChoiceDto(x.LocationId, x.DocumentType, x.PrintingTemplateId)).ToList();
    }
}
