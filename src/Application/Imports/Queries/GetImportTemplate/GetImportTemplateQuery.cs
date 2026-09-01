using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Imports;
using FluentValidation;
using MediatR;

namespace ErpApp.Application.Imports.Queries.GetImportTemplate;

/// <summary>
/// Returns the template's shape for an entity type. The Api renders it to a .xlsx; nothing about
/// spreadsheets appears here.
///
/// <para>The point is that the template a user downloads and the parser that reads their upload
/// back are generated from <b>one</b> <see cref="ImportTemplateDefinition"/> owned by the importer
/// itself. A hand-maintained template file next to a hand-maintained parser is the classic way a
/// bulk importer ends up wrong in a way no test catches -- rename a column in one and the other
/// keeps compiling.</para>
///
/// <para>Gated on Manage rather than View: a template is only useful to someone who can actually
/// run an import, and it is the entry point to the whole flow.</para>
/// </summary>
public sealed record GetImportTemplateQuery(Guid OrganizationId, ImportEntityType EntityType)
    : IRequest<ImportTemplateDefinition>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.ImportJobManage;
}

public sealed class GetImportTemplateQueryValidator : AbstractValidator<GetImportTemplateQuery>
{
    public GetImportTemplateQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.EntityType).IsInEnum();
    }
}

public sealed class GetImportTemplateQueryHandler(IEnumerable<IEntityImporter> importers)
    : IRequestHandler<GetImportTemplateQuery, ImportTemplateDefinition>
{
    public Task<ImportTemplateDefinition> Handle(GetImportTemplateQuery request, CancellationToken cancellationToken)
    {
        var importer = importers.SingleOrDefault(i => i.EntityType == request.EntityType)
            ?? throw new NotFoundException($"No import template exists for {request.EntityType}.");

        return Task.FromResult(importer.Template);
    }
}
