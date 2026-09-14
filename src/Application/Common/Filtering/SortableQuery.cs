using System.Linq.Expressions;
using FluentValidation;

namespace ErpApp.Application.Common.Filtering;

/// <summary>
/// Phase 40 — a list query that accepts an ordering, and the rule for which orderings it may accept.
///
/// <para><b>The re-entry condition 34b could not state.</b> Phase 34b built the chrome's <c>Sort by</c>
/// control and shipped it with no consumer, saying so: "a seam, not a feature", to be filled by "the
/// first list whose default ordering someone complains about". That condition cannot be checked —
/// nobody has complained about anything, and waiting for a complaint is how a seam stays empty
/// forever. Phase 34c supplies a condition that <i>can</i> be checked, and it is the stronger one:
/// <b>an ordering may be offered exactly when an index already leads on
/// <c>(OrganizationId, &lt;that column&gt;)</c></b>.</para>
///
/// <para>That is not tidiness. 34c measured the invoice list at 50 000 rows and found
/// <c>(OrganizationId, CreatedAt)</c> is what makes it fast; offering <c>ORDER BY Code</c> beside it
/// would turn the same screen into a sort over the whole filtered set, and the pager would hide how
/// slow it had become because page 1 still returns ten rows. So the menu on a screen is not a design
/// choice — it is a reading of <see cref="Infrastructure"/>'s <c>TenantIndexConvention</c>, which
/// gives a document exactly two: <c>CreatedAt</c> descending (the list index) and its own business
/// date (the range index).</para>
///
/// <para><b>Why a string and not an enum.</b> The value crosses the wire from a
/// <c>&lt;select&gt;</c> and is matched by name in one <c>switch</c> per handler, the same way
/// <c>DocumentType</c> members are bridged by name rather than by ordinal (phase-26a). An unknown
/// value is a 400 from the validator, never a silent fallback: a list that quietly ignores the
/// ordering it was asked for is the read-side gap phase 35a spent a section on.</para>
/// </summary>
public interface ISortableQuery
{
    /// <summary>Null means "this screen's default", which every pre-phase-40 caller keeps getting.</summary>
    string? Sort { get; }
}

/// <summary>
/// The ordering names that cross the wire. Each one names a column an index leads on; adding a member
/// here without adding the index is the mistake this type exists to make visible.
/// </summary>
public static class ListSort
{
    /// <summary>A document's <c>CreatedAt</c>, descending — the default on every document list.</summary>
    public const string Newest = "newest";

    /// <summary>A document's own business date, descending — the column the date-range filter uses.</summary>
    public const string DocumentDate = "date";

    public static readonly IReadOnlySet<string> DocumentOrderings = new HashSet<string>(StringComparer.Ordinal)
    {
        Newest,
        DocumentDate,
    };
}

public static class SortValidation
{
    /// <param name="sortSelector">
    /// An <see cref="Expression"/>, never a captured <c>Func</c> — see
    /// <see cref="SearchValidation.ValidateSearch{T}"/> for what the latter costs.
    /// </param>
    public static void ValidateSort<T>(
        this AbstractValidator<T> validator,
        Expression<Func<T, string?>> sortSelector,
        IReadOnlySet<string> allowed)
    {
        validator.RuleFor(sortSelector)
            .Must(value => value is null || allowed.Contains(value))
            .WithMessage($"Sort must be one of: {string.Join(", ", allowed.Order(StringComparer.Ordinal))}.");
    }
}
