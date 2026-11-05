using System.Text.RegularExpressions;
using ErpApp.Application.Common.Security;

namespace ErpApp.Application.UnitTests.Common;

/// <summary>
/// The server's half of the shell's permission-aware nav. Every route in <c>web/src/app/app.routes.ts</c>
/// names the key its page's first read requires in a <c>permissionGuard(...)</c>, and the client has
/// no list of real keys to check them against. A misspelt key fails nowhere on the client: the guard
/// redirects every user Home and the nav hides the screen from everyone, Admin included. So the
/// strings are checked here, against <see cref="PermissionKeyCatalog"/>.
/// </summary>
public partial class RoutePermissionKeysTests
{
    [GeneratedRegex(@"permissionGuard\(([^)]*)\)")]
    private static partial Regex GuardCall();

    [GeneratedRegex(@"'([^']+)'")]
    private static partial Regex QuotedKey();

    [Fact]
    public void Every_key_a_route_names_is_a_real_permission_key()
    {
        var source = File.ReadAllText(RoutesFile());

        var named = GuardCall().Matches(source)
            .SelectMany(call => QuotedKey().Matches(call.Groups[1].Value).Select(m => m.Groups[1].Value))
            .ToList();

        // Non-empty first, or a renamed file or helper turns this into a test of nothing.
        Assert.True(named.Count > 100, $"Expected the route table's permission keys, found {named.Count}.");

        var known = PermissionKeyCatalog.AllKeys.ToHashSet(StringComparer.Ordinal);
        Assert.Empty(named.Where(key => !known.Contains(key)).Distinct());
    }

    private static string RoutesFile()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ErpApp.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "web", "src", "app", "app.routes.ts");
    }
}
