using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Crm.Queries.ListSmsTemplates;

public sealed record ListSmsTemplatesQuery(Guid OrganizationId, int Page = 1, int PageSize = PagingDefaults.MaxPageSize)
    : IRequest<SmsTemplateListDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.SmsTemplateView;
}

public sealed record SmsTemplateRowDto(Guid Id, string Title, string Content, DateTimeOffset CreatedAt);

public sealed record SmsTemplateListDto(IReadOnlyList<SmsTemplateRowDto> Rows, int Page, int PageSize, int TotalCount);
