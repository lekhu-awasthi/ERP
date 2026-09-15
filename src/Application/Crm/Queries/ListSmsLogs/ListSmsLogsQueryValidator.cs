using ErpApp.Application.Common.Filtering;
using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Crm.Queries.ListSmsLogs;

/// <summary>
/// Phase 45 -- this query had <b>no validator at all</b> before the search term was added, so its
/// Page/PageSize reached the handler unbounded. That is phase 34b's finding repeating: asking one
/// question of every paginated list is worth more than the question, because it surfaces the lists
/// nobody ever asked anything of. Both halves are here, not just the term.
/// </summary>
public sealed class ListSmsLogsQueryValidator : AbstractValidator<ListSmsLogsQuery>
{
    public ListSmsLogsQueryValidator()
    {
        this.ValidatePaging(x => x.Page, x => x.PageSize);
        this.ValidateSearch(x => x.Search);
    }
}
