using System.Reflection;
using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.UnitTests.Common;

/// <summary>
/// Phase 88's sweep guard -- the mirror of phase 12's rule.
///
/// <para>Phase 12 made every <see cref="IOrganizationScoped"/> request implement <see cref="IRequirePermission"/>,
/// because <c>AuthorizationBehavior</c> is the only thing that checks the caller belongs to the organization at all.
/// Nobody asked the mirror question: which requests <i>name</i> an organization without declaring themselves scoped to
/// it? One did. <c>GetDefaultPaymentAllocationsQuery</c> (the payment form's FIFO Suggest) took an
/// <c>OrganizationId</c> from the route from phase 5 to phase 87 with neither marker, so the behavior never ran and
/// any signed-in user could read another organization's open documents and balances.</para>
///
/// <para>The predicate is the property, not the name of the type or its folder, so a request added anywhere is
/// covered. The allow-list is empty and is meant to stay so; a request that genuinely acts on an organization the
/// caller need not belong to (none exists) would go there with its reason.</para>
/// </summary>
public class OrganizationScopeSweepGuardTests
{
    private static readonly Assembly ApplicationAssembly = typeof(IRequirePermission).Assembly;

    private static readonly IReadOnlyDictionary<string, string> AllowList = new Dictionary<string, string>();

    [Fact]
    public void Every_request_naming_an_organization_is_scoped_to_it_and_permission_gated()
    {
        var requests = RequestTypes()
            .Where(t => t.GetProperty("OrganizationId", BindingFlags.Public | BindingFlags.Instance) is
                { PropertyType: var type } && (type == typeof(Guid) || type == typeof(Guid?)))
            .ToList();

        // Non-empty, or the predicate has silently stopped matching anything (phase 34a's vacuous guard).
        Assert.True(requests.Count > 300, $"Only {requests.Count} requests carry an OrganizationId; the predicate is wrong.");

        var unscoped = requests
            .Where(t => !AllowList.ContainsKey(t.Name))
            .Where(t => !typeof(IOrganizationScoped).IsAssignableFrom(t) || !typeof(IRequirePermission).IsAssignableFrom(t))
            .Select(t => t.FullName)
            .ToList();

        Assert.True(
            unscoped.Count == 0,
            "These requests carry an OrganizationId but are not both IOrganizationScoped and IRequirePermission, so "
            + "AuthorizationBehavior never checks the caller belongs to that organization:"
            + Environment.NewLine + string.Join(Environment.NewLine, unscoped));
    }

    [Fact]
    public void The_allow_list_names_only_requests_that_still_exist()
    {
        var names = RequestTypes().Select(t => t.Name).ToHashSet();
        Assert.All(AllowList.Keys, name => Assert.Contains(name, names));
    }

    private static IEnumerable<Type> RequestTypes() =>
        ApplicationAssembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(t => typeof(IBaseRequest).IsAssignableFrom(t));
}
