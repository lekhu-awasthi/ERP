using System.Linq.Expressions;
using FluentValidation;

namespace ErpApp.Application.Common.Filtering;

/// <summary>
/// Phase 34b (NFR-6.1) — a list query that accepts a free-text term.
///
/// <para><b>Why a marker interface rather than 34 unrelated <c>Search</c> parameters.</b> The
/// property alone would be a convention, and a convention is exactly what NFR-6.1 says this codebase
/// does not have: before this phase <b>one</b> of 46 list screens had a search box and <b>two</b> of
/// 47 list queries accepted a term. The interface is what {@link SearchSweepGuardTests} can assert
/// over, so the next paginated list added to this codebase either accepts a term or names itself in
/// that guard's allow-list with a reason — the sweep-guard shape phases 27a and 32b established.</para>
///
/// <para><b>What it deliberately does not do</b> is carry the matching itself. Each handler writes
/// its own <c>.Where()</c> over the columns that mean something for that aggregate, because a shared
/// matcher would have to take a selector, and a captured <c>Func</c> selector is the phase-9 and
/// phase-25 gotcha — it 500s every endpoint it guards and no handler test can see it. See
/// <see cref="Matches"/> for the one piece that <i>is</i> worth sharing.</para>
/// </summary>
public interface ISearchableQuery
{
    string? Search { get; }
}

/// <summary>
/// Phase 34b — a list query scoped by the shell's global date range.
///
/// <para>The range comes from the top bar and is stored per user (see the client's
/// <c>date-range.service.ts</c> for the confirm-live finding that made it a list filter rather than a
/// dashboard control). Only aggregates with a business date implement this: master data and
/// configuration lookups do not, which is also what the reference product does — on the live tenant
/// <c>products</c> and <c>contact-groups</c> receive no range while <c>invoices</c>, <c>contacts</c>,
/// <c>accounts</c> and <c>journal-vouchers</c> do.</para>
/// </summary>
public interface IDateRangeFilteredQuery
{
    DateOnly? FromDate { get; }
    DateOnly? ToDate { get; }
}

/// <summary>
/// The two rules every searchable list shares, so the term cannot mean different things on different
/// screens.
/// </summary>
public static class SearchTerm
{
    /// <summary>
    /// Long enough for a document number, a contact name or a reference; short enough that the term
    /// cannot be used to push a large payload through a GET on 34 endpoints.
    /// </summary>
    public const int MaxLength = 100;

    /// <summary>
    /// Whitespace-only is not a search — it is an empty box the user tabbed through, and treating it
    /// as a term would make every list return nothing the moment someone typed a space.
    /// </summary>
    public static string? Normalize(string? raw) => string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();

    // There is deliberately no shared `Matches(column, term)` helper here, and the reason is worth
    // stating so nobody adds one back:
    //
    //   * A static method call inside a LINQ-to-Entities predicate is not translatable at all -- EF
    //     would have to evaluate it client-side, which it refuses to do for a `Where`.
    //   * `string.Contains(term, StringComparison.OrdinalIgnoreCase)` is not translatable either;
    //     only the single-argument overload is, and SQL Server renders it as `LIKE '%term%'`.
    //
    // So every handler writes `x.Code.Contains(term)` inline. Two consequences to know about:
    //
    //   * Matching is case-**insensitive** on SQL Server (the database's default collation does it,
    //     not the expression) and case-**sensitive** on the InMemory provider every handler test
    //     runs against. A handler test therefore has to search with the stored casing, or it is
    //     asserting a behaviour the real database does not have.
    //   * `EF.Functions.Like` is not an alternative: InMemory cannot translate it (standing gotcha).
}

/// <summary>
/// The validator half, called the way <c>PagingValidation.ValidatePaging</c> is — one line per
/// validator instead of the same <c>RuleFor</c> written 34 times.
/// </summary>
public static class SearchValidation
{
    /// <param name="searchSelector">
    /// An <see cref="Expression"/>, never a captured <c>Func</c>: FluentValidation infers the
    /// property name from the tree, and a <c>Func</c> makes it throw "Could not infer property name"
    /// at request time on every endpoint the validator guards — 500s that no handler test can see
    /// (phase-25).
    /// </param>
    public static void ValidateSearch<T>(
        this AbstractValidator<T> validator,
        Expression<Func<T, string?>> searchSelector)
    {
        validator.RuleFor(searchSelector)
            .MaximumLength(SearchTerm.MaxLength)
            .WithMessage($"Search must be {SearchTerm.MaxLength} characters or fewer.");
    }

    /// <summary>A range whose end precedes its start silently returns nothing on every list it filters.</summary>
    public static void ValidateDateRange<T>(
        this AbstractValidator<T> validator,
        Expression<Func<T, DateOnly?>> fromSelector,
        Expression<Func<T, DateOnly?>> toSelector)
    {
        var from = fromSelector.Compile();

        validator.RuleFor(toSelector)
            .Must((request, to) => from(request) is not { } start || to is not { } end || start <= end)
            .WithMessage("ToDate must be on or after FromDate.");
    }
}
