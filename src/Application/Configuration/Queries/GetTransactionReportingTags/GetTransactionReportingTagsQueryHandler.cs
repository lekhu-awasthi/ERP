using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Configuration.Queries.GetTransactionReportingTags;

public sealed class GetTransactionReportingTagsQueryHandler(IAppDbContext db)
    : IRequestHandler<GetTransactionReportingTagsQuery, IReadOnlyList<TransactionReportingTagDto>>
{
    public async Task<IReadOnlyList<TransactionReportingTagDto>> Handle(
        GetTransactionReportingTagsQuery request, CancellationToken cancellationToken)
    {
        var rows = await (
            from t in db.TransactionReportingTags
            join o in db.ReportingTagOptions on t.TagOptionId equals o.Id
            join c in db.ReportingTagCategories on o.CategoryId equals c.Id
            where t.OrganizationId == request.OrganizationId
                && t.DocumentType == request.DocumentType && t.DocumentId == request.DocumentId
            orderby c.Name, o.Name
            select new TransactionReportingTagDto(o.Id, o.Name, c.Id, c.Name))
            .ToListAsync(cancellationToken);

        return rows;
    }
}
