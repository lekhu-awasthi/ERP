using System.Reflection;
using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Security;

namespace ErpApp.Application.UnitTests.Locations;

/// <summary>
/// Phase 35b's sweep guard, in the shape phases 27a, 32b, 34b and 35a established.
///
/// <para><b>The defect it exists to prevent is phase 35a's, one layer up.</b> Phase 32 shipped a
/// guard proving every location-bearing type could <i>write</i> a location, and fourteen of fifteen
/// detail queries dropped it on the way back out — nothing failed, because nothing asked. The
/// reports are the same shape of question asked of a larger set: 43 of 49 live report screens carry
/// a Billing Location filter, and before this phase exactly <b>one</b> query in this codebase
/// accepted one. A convention nobody can fail is not a convention.</para>
///
/// <para><b>The universe is derived, not listed.</b> A report query is one whose permission key
/// lives in the <c>Reports.</c> namespace — which is what makes a new report face this question the
/// moment it is written, instead of vanishing off the bottom of a hand-kept list.</para>
/// </summary>
public class ReportLocationSweepGuardTests
{
    private static readonly Assembly ApplicationAssembly = typeof(IRequirePermission).Assembly;

    /// <summary>
    /// Report queries that deliberately take no Billing Location filter, each with the reason.
    ///
    /// <para><b>Six come straight from the live census</b> (confirm-live 2026-09-10, all 49 report
    /// filter bars read): the reference product does not offer the control on them. <b>Three more are
    /// this codebase's own</b>, and their reason is stronger than the product's — the row they read
    /// carries no location at all, so the filter could not be honestly implemented without inventing
    /// a join to a document that may not exist.</para>
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> Exempt = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // --- the live census's six ---
        ["VatSummaryReportQuery"] =
            "An IRD return, filed once per PAN. No Billing Location control live (census 2026-09-10).",
        ["TdsReportQuery"] =
            "An IRD return. No Billing Location control live.",
        ["AnnexThirteenReportQuery"] =
            "An IRD annex. No Billing Location control live -- and note Annex 5, the sales-side annex, "
            + "DOES carry one and is therefore not exempt. Observed, not reconciled: the census read "
            + "all 49 screens precisely so this asymmetry could be followed rather than tidied away.",
        ["RatioAnalysisQuery"] =
            "Ratios over the whole organization's statements. A ratio of one branch's numbers to "
            + "another's would not be the same report. No control live.",
        ["ExceptionalReportQuery"] =
            "A tenant-wide exception scan whose entire filter bar is a date range, live.",
        ["UserLogQuery"] =
            "Login events. There is no document behind a row, and UserLoginEvent carries no location.",

        // --- this codebase's own three ---
        ["MigratedSalesRegisterQuery"] =
            "MigratedSalesRegisterEntry is deliberately not a document (phase 21c): no lifecycle, no "
            + "LocationId column, and the cutover spreadsheet it is imported from has no location "
            + "column to populate one from. Absent from both live tenants read, so the census says "
            + "nothing either way.",
        ["MigratedPurchaseRegisterQuery"] =
            "The Purchase Book counterpart, same reasoning.",
        ["SystemAuditReportQuery"] =
            "An Audit row records a command as (UserId, Action, DocumentType, DocumentId) and nothing "
            + "else -- it has no location, and it is written for commands over rows that are not "
            + "documents at all. Giving it one means either stamping a location at audit-write time "
            + "(where an Approve command does not carry one) or the 17-way join back that Decision B "
            + "rejected for the GL. Carried as phase 35b item #2 rather than guessed at.",
    };

    [Fact]
    public void Every_report_query_takes_a_billing_location_filter_or_states_why_not()
    {
        var reportQueries = ReportQueries().ToList();

        // Phase-34a's rule: a guard must assert its input is non-empty, or every assertion over it
        // is vacuous. This codebase has 52 report screens; the reflection finding fewer than 40
        // report queries means the derivation broke, not that the reports went away.
        Assert.True(
            reportQueries.Count >= 40,
            $"Only {reportQueries.Count} report queries found -- the derivation is broken, so every "
            + "assertion below would pass for the wrong reason.");

        var missing = reportQueries
            .Where(t => !typeof(ILocationFilteredReport).IsAssignableFrom(t))
            .Where(t => !Exempt.ContainsKey(t.Name))
            .Select(t => t.Name)
            .ToList();

        Assert.True(
            missing.Count == 0,
            "43 of the reference product's 49 report screens carry a Billing Location filter. These "
            + "queries take none and give no reason -- either implement ILocationFilteredReport or "
            + "add the query to ReportLocationSweepGuardTests.Exempt with the reason:\n  "
            + string.Join("\n  ", missing));
    }

    /// <summary>
    /// An exemption naming a query that no longer exists is a reason nobody can check, and it hides
    /// the fact that the guard has stopped covering something. Phase-34a's rule for allow-lists.
    /// </summary>
    [Fact]
    public void Every_exemption_names_a_report_query_that_still_exists()
    {
        var names = ReportQueries().Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        var stale = Exempt.Keys.Where(k => !names.Contains(k)).ToList();

        Assert.True(stale.Count == 0, "Stale exemptions: " + string.Join(", ", stale));
    }

    /// <summary>
    /// The filter has to default to "All locations". A required parameter would break every existing
    /// caller and, worse, would make "no location chosen" indistinguishable from "HeadOffice" the way
    /// phase 35a's detail DTOs did on the document forms.
    /// </summary>
    [Fact]
    public void Every_location_filtered_report_defaults_to_all_locations()
    {
        var wrong = new List<string>();

        foreach (var type in ReportQueries().Where(typeof(ILocationFilteredReport).IsAssignableFrom))
        {
            var parameter = type.GetConstructors()
                .OrderByDescending(c => c.GetParameters().Length)
                .First()
                .GetParameters()
                .FirstOrDefault(p => p.Name == nameof(ILocationFilteredReport.LocationId));

            if (parameter is null)
            {
                wrong.Add($"{type.Name} (no LocationId constructor parameter)");
            }
            else if (!parameter.HasDefaultValue || parameter.DefaultValue is not null)
            {
                wrong.Add($"{type.Name} (LocationId does not default to null)");
            }
        }

        Assert.True(
            wrong.Count == 0,
            "A Billing Location filter's default is \"All locations\", which is null:\n  " + string.Join("\n  ", wrong));
    }

    /// <summary>
    /// A report's <b>permission scope</b> is a second narrowing, separate from the user's filter, and
    /// it is applied by the handler rather than declared by the query -- so the query-level checks
    /// above cannot see it. What they can see is that the handler is able to ask:
    /// <c>LocationAccessScope.ForReportsAsync</c> needs an <c>ICurrentUserService</c>, and a handler
    /// that does not take one cannot possibly honour
    /// <c>TenantSettings.LocationWiseReportPermission</c>.
    ///
    /// <para>This is a weaker claim than "the handler applies it" -- deliberately, because that one
    /// is not decidable by reflection. The behaviour itself is pinned by
    /// <c>ReportLocationFilterTests</c> per family and by the phase's E2E against SQL Server. What
    /// this catches is the cheap, likely regression: a new report written without the dependency at
    /// all, which no test would otherwise notice until a tenant turned the toggle on.</para>
    /// </summary>
    [Fact]
    public void Every_location_filtered_reports_handler_can_resolve_the_permission_scope()
    {
        var missing = new List<string>();

        foreach (var query in ReportQueries().Where(typeof(ILocationFilteredReport).IsAssignableFrom))
        {
            var handler = HandlerFor(query);

            if (handler is null)
            {
                missing.Add($"{query.Name} (no handler found)");
                continue;
            }

            var takesCurrentUser = handler.GetConstructors()
                .SelectMany(c => c.GetParameters())
                .Any(p => p.ParameterType == typeof(ICurrentUserService));

            if (!takesCurrentUser)
            {
                missing.Add($"{handler.Name} (takes no ICurrentUserService)");
            }
        }

        Assert.True(
            missing.Count == 0,
            "TenantSettings.LocationWiseReportPermission narrows report rows to the locations a caller "
            + "is granted at. These handlers cannot read it:\n  " + string.Join("\n  ", missing));
    }

    private static Type? HandlerFor(Type queryType) =>
        ApplicationAssembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false })
            .FirstOrDefault(t => t.GetInterfaces().Any(
                i => i.IsGenericType
                    && i.GetGenericTypeDefinition() == typeof(MediatR.IRequestHandler<,>)
                    && i.GetGenericArguments()[0] == queryType));

    /// <summary>
    /// Every report query that is permission-gated at all, derived from its key's namespace.
    ///
    /// <para>The three manufacturing reports are the one place the namespace lies: they share
    /// <c>Manufacturing.ProductionReport.View</c>, one View-only key for all three, by phase 25's
    /// decision. That is a fact about how the key was named, not about whether they are reports, so
    /// they are named here rather than silently excluded.</para>
    /// </summary>
    private static IEnumerable<Type> ReportQueries() =>
        ApplicationAssembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false })
            .Where(t => typeof(IRequirePermission).IsAssignableFrom(t))
            .Where(t => t.Name.EndsWith("Query", StringComparison.Ordinal))
            .Where(IsReportKey)
            .OrderBy(t => t.Name, StringComparer.Ordinal);

    private static bool IsReportKey(Type type)
    {
        var key = KeyOf(type);

        return key is not null
            && (key.StartsWith("Reports.", StringComparison.Ordinal)
                || key == PermissionKeys.ProductionReportView);
    }

    /// <summary>
    /// <c>PermissionKey</c> is an instance property, so reading it needs an instance -- built from
    /// defaults the same way <c>SearchSweepGuardTests</c> builds one, since the guard must not know
    /// any query's parameter list.
    /// </summary>
    private static string? KeyOf(Type type)
    {
        try
        {
            var constructor = type.GetConstructors().OrderByDescending(c => c.GetParameters().Length).FirstOrDefault();

            if (constructor is null)
            {
                return null;
            }

            var instance = constructor.Invoke([.. constructor.GetParameters().Select(DefaultFor)]);

            return ((IRequirePermission)instance).PermissionKey;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static object? DefaultFor(ParameterInfo parameter)
    {
        if (parameter.HasDefaultValue)
        {
            return parameter.DefaultValue;
        }

        if (parameter.ParameterType == typeof(int))
        {
            return parameter.Name == "Page" ? 1 : 25;
        }

        if (parameter.ParameterType == typeof(string))
        {
            return string.Empty;
        }

        return parameter.ParameterType.IsValueType ? Activator.CreateInstance(parameter.ParameterType) : null;
    }
}
