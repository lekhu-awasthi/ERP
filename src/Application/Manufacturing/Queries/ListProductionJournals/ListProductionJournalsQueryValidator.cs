using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Manufacturing.Queries.ListProductionJournals;

public sealed class ListProductionJournalsQueryValidator : AbstractValidator<ListProductionJournalsQuery>
{
    public ListProductionJournalsQueryValidator() => this.ValidatePaging(x => x.Page, x => x.PageSize);
}
