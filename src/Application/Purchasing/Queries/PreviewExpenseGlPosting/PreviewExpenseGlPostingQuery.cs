using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Purchasing.Queries.PreviewExpenseGlPosting;

public sealed record PreviewExpenseGlPostingQuery(
    Guid OrganizationId, IReadOnlyList<ExpenseLineInput> Lines, bool TdsApplicable, Guid? TdsTypeId)
    : IRequest<IReadOnlyList<GlLinePreviewDto>>, IRequirePermission, IOrganizationScoped, ILocationAgnosticRequest
{
    public string PermissionKey => PermissionKeys.ExpenseView;
}

public sealed record GlLinePreviewDto(Guid AccountId, decimal Debit, decimal Credit);
