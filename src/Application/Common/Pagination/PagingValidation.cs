using System.Linq.Expressions;
using FluentValidation;

namespace ErpApp.Application.Common.Pagination;

/// <summary>
/// One shared rule pair every paginated query's validator calls (`this.ValidatePaging(x =>
/// x.Page, x => x.PageSize)`) instead of hand-rolling the same two RuleFor calls 29 times.
/// Reject, never clamp, per phase-16c-status.md's boundary-correctness decision -- a caller that
/// sends Page=0 or PageSize=500 gets a 400 naming the field, not a silently-substituted value.
/// </summary>
public static class PagingValidation
{
    public static void ValidatePaging<T>(
        this AbstractValidator<T> validator,
        Expression<Func<T, int>> pageSelector,
        Expression<Func<T, int>> pageSizeSelector)
    {
        validator.RuleFor(pageSelector)
            .GreaterThanOrEqualTo(1)
            .WithMessage("Page must be 1 or greater.");
        validator.RuleFor(pageSizeSelector)
            .InclusiveBetween(1, PagingDefaults.MaxPageSize)
            .WithMessage($"PageSize must be between 1 and {PagingDefaults.MaxPageSize}.");
    }
}
