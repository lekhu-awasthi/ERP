using FluentValidation;

namespace ErpApp.Application.Platform.Queries.GlobalSearch;

public sealed class GlobalSearchQueryValidator : AbstractValidator<GlobalSearchQuery>
{
    /// <summary>
    /// A search runs eighteen <c>Contains</c> queries. One character matches most of a tenant and
    /// tells the user nothing, so the floor is two -- the same floor the reference product's own
    /// debounce imposes in practice. The client does not send a shorter term; this is what stops a
    /// caller who bypasses the client from using the endpoint as a full table scan.
    /// </summary>
    public const int MinimumTermLength = 2;

    public const int MaximumTermLength = 100;

    public GlobalSearchQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();

        RuleFor(x => x.Term)
            .NotEmpty()
            .MinimumLength(MinimumTermLength)
            .MaximumLength(MaximumTermLength);

        RuleFor(x => x.Limit)
            .InclusiveBetween(1, GlobalSearchQueryHandler.MaxLimit)
            .When(x => x.Limit.HasValue);
    }
}
