using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Configuration;
using MediatR;

namespace ErpApp.Application.Printing.Queries.ListPrintElements;

/// <summary>
/// Phase 73 -- for every printable type, the elements a template may switch and the ones it may not, read from
/// <see cref="PrintElements"/>. The layout editor draws its checkboxes from this rather than from a copy of the table in
/// the browser, so the screen cannot offer to hide what the save would refuse (phase 39's two-languages lesson: one
/// rule, one place).
/// </summary>
public sealed record ListPrintElementsQuery(Guid OrganizationId)
    : IRequest<IReadOnlyList<PrintElementsForTypeDto>>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.PrintingTemplateView;
}

public sealed record PrintElementsForTypeDto(
    DocumentType DocumentType,
    IReadOnlyList<PrintElement> Elements,
    IReadOnlyList<PrintElement> Locked,
    // Phase 79 -- the layouts this type is offered, each with what it has no room for, so the editor's radios and
    // its disabled checkboxes come from the same table the save checks.
    IReadOnlyList<PrintLayoutOptionDto> Layouts);

/// <summary>Phase 79 -- one layout a type may choose, and which of the type's elements it never draws.</summary>
public sealed record PrintLayoutOptionDto(PrintLayout Layout, IReadOnlyList<PrintElement> Omits);

public sealed class ListPrintElementsQueryHandler
    : IRequestHandler<ListPrintElementsQuery, IReadOnlyList<PrintElementsForTypeDto>>
{
    public Task<IReadOnlyList<PrintElementsForTypeDto>> Handle(ListPrintElementsQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<PrintElementsForTypeDto> result = DocumentMechanisms.Printable
            .Select(type => new PrintElementsForTypeDto(
                type,
                PrintElements.For(type),
                PrintElements.Locked(type),
                PrintLayouts.OfferedFor(type)
                    .Select(layout => new PrintLayoutOptionDto(
                        layout, PrintLayouts.Omits(layout).Where(PrintElements.For(type).Contains).ToList()))
                    .ToList()))
            .ToList();

        return Task.FromResult(result);
    }
}
