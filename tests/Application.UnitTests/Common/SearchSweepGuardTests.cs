using System.Reflection;
using ErpApp.Application.Common.Filtering;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using FluentValidation;

namespace ErpApp.Application.UnitTests.Common;

/// <summary>
/// Phase 34b's sweep guard, in the shape phases 27a, 32b and 24 established.
///
/// <para><b>The defect it exists to prevent is the one this phase was created to fix.</b> NFR-6.1
/// asks for one interaction model across every list screen. At the start of this phase <b>one</b> of
/// 46 list screens had a search box and <b>two</b> of 47 list queries accepted a term — not because
/// anyone decided the other 44 should not, but because nothing ever asked the question. A convention
/// nobody can fail is not a convention; this test is what makes the next paginated list either
/// accept a term or say, in {@link Exempt}, why it does not.</para>
///
/// <para>It also enforces the two halves that make a term safe once it exists: a validator that
/// bounds its length, and (for a query that filters by date) a validator that refuses an inverted
/// range — which would otherwise return nothing on every affected list and read as data loss.</para>
/// </summary>
public class SearchSweepGuardTests
{
    private static readonly Assembly ApplicationAssembly = typeof(IRequirePermission).Assembly;

    /// <summary>
    /// Paginated list queries that deliberately take no search term, each with the reason.
    ///
    /// <para>The rule they are all instances of: <b>a query gets a search term when its screen is a
    /// standalone list of records someone browses, and is exempt when it backs an editor, a picker,
    /// or a panel already scoped to a single parent row.</b> In each case below the result set is
    /// either bounded by construction or already narrowed by something other than a term, so a
    /// search box would be chrome over nothing.</para>
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> Exempt = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["ListAccountOpeningBalancesQuery"] =
            "An opening-balance worksheet, not a list: it shows every account so the totals reconcile, "
            + "and hiding rows behind a term would let someone save a partial set believing it complete.",
        ["ListProductOpeningBalancesQuery"] =
            "The same worksheet for opening stock, for the same reason.",
        ["ListCustomFieldDefinitionsQuery"] =
            "An admin panel that deliberately requests MaxPageSize: it returns every definition so the "
            + "form builder can render them all, and a tenant's definitions number in the tens.",
        ["ListEmailLogsQuery"] =
            "The Email Logs tab of one document, already scoped to that (DocumentType, ParentId).",
        ["ListAlertSendLogsQuery"] =
            "The send history of one alert definition, already scoped to that AlertDefinitionId.",
        ["ListAllocatablePaymentsQuery"] =
            "The allocation picker, already scoped to one contact and to payments with a balance left.",
        ["ListExportJobsQuery"] =
            "A job history ordered newest-first, where what anyone wants is the last few runs.",
        ["ListImportJobsQuery"] =
            "The same, for imports.",
        ["ListOrganizationMembersQuery"] =
            "A tenant's member list, bounded by how many people it has invited.",

        // The six below are what remains of the seven that became visible when phase 39 widened this
        // guard to recognise bespoke paging envelopes as well as PagedResult<T>. Every one of them is
        // the rule's own exempt case -- a panel already scoped to a single parent row, or a sequence
        // whose rows only mean anything in order -- which is the answer, not a shortcut: the point of
        // asking the question of every list is that most lists have a good reason, and the two that
        // did not (Tasks and Deals) are what phase 39 gave a search box.
        //
        // The seventh was ListSmsLogsQuery, which phase 39 named as the one whose re-entry condition
        // would be met. Phase 45 met it and gave it a term, so it is no longer here -- an exemption
        // that names its re-entry condition is only worth writing if somebody later honours it.
        ["ListAttachmentsQuery"] =
            "The Documents tab of one record, already scoped to that (ParentType, ParentId).",
        ["ListCommentsQuery"] =
            "The Comments sub-tab of one record's Activity tab, scoped the same way.",
        ["ListActivitiesQuery"] =
            "The Activities sub-tab of one record: an audit feed in time order, where scrolling is "
            + "the point and a term would hide the entries either side of the one matched.",
        ["ListContactPersonnelQuery"] =
            "The Personnel tab of one contact, bounded by how many people that contact has.",
        ["ListSmsCreditLedgerQuery"] =
            "A running credit balance newest-first: the rows only mean anything in sequence, so "
            + "filtering them would leave a balance column that no longer adds up.",
        ["ListSmsTemplatesQuery"] =
            "A tenant's own SMS templates, bounded by how many it has written -- the same reason "
            + "ListOrganizationMembersQuery is exempt.",

        // Phase 56. The rule's own exempt case -- a panel already scoped to a single parent row --
        // plus a second reason that is really a cost: the one field a user would search here is the
        // source document's number, and that is exactly the field GlJournalEntry deliberately does
        // not carry (phase 26a). Matching it would mean a thirteen-way join evaluated before the
        // page is formed, which is phase 50's ListChequesQuery finding (a list that searches a
        // joined column cannot be indexed out of it) with twelve more tables.
        // RE-ENTRY CONDITION: a phase that denormalises the source document's code onto the entry,
        // at which point the term costs nothing.
        ["ListBookTransactionsQuery"] =
            "The book side of one bank account, already scoped to that account and a date range. "
            + "The only field worth searching is the document number, which GlJournalEntry does not "
            + "store (phase 26a) -- see the query's doc comment for the re-entry condition.",
    };

    [Fact]
    public void Every_paginated_list_query_takes_a_search_term_or_states_why_not()
    {
        var missing = new List<string>();

        foreach (var type in PaginatedListQueries())
        {
            if (typeof(ISearchableQuery).IsAssignableFrom(type) || Exempt.ContainsKey(type.Name))
            {
                continue;
            }

            missing.Add(type.Name);
        }

        Assert.True(
            missing.Count == 0,
            "These paginated list queries accept no search term and give no reason. NFR-6.1 asks for "
            + "one interaction model across every list, so either implement ISearchableQuery or add "
            + "the query to SearchSweepGuardTests.Exempt with the reason:\n  "
            + string.Join("\n  ", missing));
    }

    /// <summary>
    /// An exemption naming a query that no longer exists is a reason nobody can check, and it hides
    /// the fact that the guard has stopped covering something. Phase-34a's rule for allow-lists.
    /// </summary>
    [Fact]
    public void Every_exemption_names_a_query_that_still_exists()
    {
        var names = PaginatedListQueries().Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        var stale = Exempt.Keys.Where(k => !names.Contains(k)).ToList();

        Assert.True(stale.Count == 0, "Stale exemptions: " + string.Join(", ", stale));
    }

    [Fact]
    public void Every_searchable_query_has_a_validator_that_bounds_the_term()
    {
        var unbounded = new List<string>();

        foreach (var type in PaginatedListQueries().Where(typeof(ISearchableQuery).IsAssignableFrom))
        {
            var validator = ValidatorFor(type);

            if (validator is null)
            {
                unbounded.Add($"{type.Name} (no validator at all)");
                continue;
            }

            // One character over the cap must fail. Asserting the *behaviour* rather than the
            // presence of a rule is the point: a validator that declares the rule against the wrong
            // property would still pass a "does a rule exist" check.
            var tooLong = Activate(type, new string('x', SearchTerm.MaxLength + 1));

            if (tooLong is not null && validator.Validate(new ValidationContext<object>(tooLong)).IsValid)
            {
                unbounded.Add($"{type.Name} (accepts a {SearchTerm.MaxLength + 1}-character term)");
            }
        }

        Assert.True(
            unbounded.Count == 0,
            "A search term reaches a LIKE on every one of these lists, so its length has to be bounded:\n  "
            + string.Join("\n  ", unbounded));
    }

    /// <summary>
    /// The two interfaces are near-inseparable: a date range exists to scope a list someone is
    /// browsing, and phase 34b gave every such list a search box. A query declaring only
    /// <see cref="IDateRangeFilteredQuery"/> is usually a filter with no chrome to drive it.
    ///
    /// <para><b>Phase 56 falsified the "always" half, and this now honours
    /// <see cref="Exempt"/> rather than asserting a coupling with no exit.</b> As written, the
    /// coupling was not a rule anybody could fail for a reason — it was a fact about the fifteen
    /// lists that existed when it was written, stated as a law. <c>ListBookTransactionsQuery</c> is
    /// a genuine counter-example: it is date-ranged, it is browsed, and the one field worth
    /// searching on it is a document number that <c>GlJournalEntry</c> deliberately does not store
    /// (phase 26a). Teaching the guard is what phase 55's own lesson prescribes — an exemption on a
    /// brand-new screen is how a seam stays empty, but a guard whose predicate has been shown false
    /// should learn rather than acquire a special case with no reason attached.</para>
    ///
    /// <para>The force is unchanged: a date-ranged list still has to <i>either</i> take a term
    /// <i>or</i> appear in <see cref="Exempt"/> with a reason, and
    /// <see cref="Every_exemption_names_a_query_that_still_exists"/> keeps those reasons from
    /// rotting.</para>
    /// </summary>
    [Fact]
    public void Every_date_range_query_is_also_searchable_or_states_why_not()
    {
        var rangedOnly = PaginatedListQueries()
            .Where(t => typeof(IDateRangeFilteredQuery).IsAssignableFrom(t))
            .Where(t => !typeof(ISearchableQuery).IsAssignableFrom(t))
            .Where(t => !Exempt.ContainsKey(t.Name))
            .Select(t => t.Name)
            .ToList();

        Assert.True(
            rangedOnly.Count == 0,
            "Date-ranged but not searchable, and giving no reason. A list someone browses by date is "
            + "one they will want to search; either implement ISearchableQuery or add the query to "
            + "SearchSweepGuardTests.Exempt with the reason:\n  "
            + string.Join("\n  ", rangedOnly));
    }

    private static IEnumerable<Type> PaginatedListQueries() =>
        ApplicationAssembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false })
            .Where(t => t.Name.StartsWith("List", StringComparison.Ordinal))
            .Where(t => t.Name.EndsWith("Query", StringComparison.Ordinal))
            .Where(IsPaginated)
            .OrderBy(t => t.Name, StringComparer.Ordinal);

    /// <summary>
    /// A list query is paginated if it returns <see cref="PagedResult{T}"/> <b>or</b> a bespoke
    /// envelope that pages the same way.
    ///
    /// <para><b>Phase 39 widened this, and the reason is the defect it found.</b> The guard
    /// originally recognised only <c>PagedResult&lt;T&gt;</c>, so the two list queries that predate
    /// that type -- <c>ListTasksQuery</c> and <c>ListDealsQuery</c>, both returning their own
    /// <c>{Rows, Page, PageSize, TotalCount}</c> record -- were invisible to a guard whose entire
    /// purpose is to notice a list nobody gave a search box. They were exactly the two lists phase 39
    /// then had to give one, which is the shape of failure a sweep guard exists to prevent and had
    /// quietly stopped preventing. Recognising the envelope by its <i>shape</i> rather than by its
    /// type means the next bespoke one is covered on the day it is written.</para>
    /// </summary>
    private static bool IsPaginated(Type type)
    {
        var response = type.GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(MediatR.IRequest<>))
            .Select(i => i.GetGenericArguments()[0])
            .FirstOrDefault();

        if (response is null)
        {
            return false;
        }

        if (response.IsGenericType && response.GetGenericTypeDefinition() == typeof(PagedResult<>))
        {
            return true;
        }

        var names = response.GetProperties().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

        return names.Contains("Page") && names.Contains("PageSize") && names.Contains("TotalCount");
    }

    private static IValidator? ValidatorFor(Type queryType) =>
        ApplicationAssembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false })
            .Where(t => typeof(IValidator<>).MakeGenericType(queryType).IsAssignableFrom(t))
            .Select(t => (IValidator?)Activator.CreateInstance(t))
            .FirstOrDefault();

    /// <summary>
    /// Builds a query instance with the given search term and defaults for everything else, so the
    /// guard does not need to know any query's parameter list.
    /// </summary>
    private static object? Activate(Type queryType, string search)
    {
        var constructor = queryType.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();

        var arguments = constructor.GetParameters()
            .Select(p => p.Name == nameof(ISearchableQuery.Search) ? search : DefaultFor(p))
            .ToArray();

        try
        {
            return constructor.Invoke(arguments);
        }
        catch (TargetInvocationException)
        {
            return null;
        }
    }

    private static object? DefaultFor(ParameterInfo parameter)
    {
        // Page/PageSize have to be *valid*, or the paging rule fails and masks the search rule --
        // the test would then pass for the wrong reason on a query that bounds nothing.
        if (parameter.ParameterType == typeof(int))
        {
            return parameter.Name == "Page" ? 1 : PagingDefaults.DefaultPageSize;
        }

        if (parameter.HasDefaultValue)
        {
            return parameter.DefaultValue;
        }

        return parameter.ParameterType == typeof(Guid)
            ? Guid.NewGuid()
            : parameter.ParameterType.IsValueType
                ? Activator.CreateInstance(parameter.ParameterType)
                : null;
    }
}
