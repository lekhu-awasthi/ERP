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

        // The five below became visible when phase 39 widened this guard to recognise bespoke paging
        // envelopes as well as PagedResult<T>. Every one of them is the rule's own exempt case -- a
        // panel already scoped to a single parent row -- which is the answer, not a shortcut: the
        // point of asking the question of every list is that most lists have a good reason, and the
        // two that did not (Tasks and Deals) are what phase 39 gave a search box.
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
        ["ListSmsLogsQuery"] =
            "A send history ordered newest-first, the same shape as the import and export job "
            + "histories above. Re-entry condition: a tenant sending enough SMS that the last page "
            + "stops being what they want. This is the first of the seven phase 39 uncovered that "
            + "would earn a term, and it is named here rather than left as a silent gap.",
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

    [Fact]
    public void Every_date_range_query_is_also_searchable_and_carries_a_business_date()
    {
        // The two interfaces are not independent: the range exists to scope a list someone is
        // browsing, and every such list is one this phase also gave a search box. A query that
        // declared only IDateRangeFilteredQuery would be a filter with no chrome to drive it.
        var rangedOnly = PaginatedListQueries()
            .Where(t => typeof(IDateRangeFilteredQuery).IsAssignableFrom(t))
            .Where(t => !typeof(ISearchableQuery).IsAssignableFrom(t))
            .Select(t => t.Name)
            .ToList();

        Assert.True(rangedOnly.Count == 0, "Date-ranged but not searchable: " + string.Join(", ", rangedOnly));
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
