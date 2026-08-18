using System.Reflection;

namespace ErpApp.Application.Common.Security;

/// <summary>
/// Enumerates every <see cref="PermissionKeys"/> constant (Phase 14, Role Reference's permission-
/// matrix editor) via reflection over that class's own <c>public const string</c> fields, grouped
/// by module (the key's first dotted segment, e.g. <c>"Sales"</c> in <c>"Sales.Invoice.View"</c>).
///
/// This is a deliberately different call from <see cref="LookupPermissionKeys"/>'s own doc
/// comment, which explains why *that* helper is a plain switch rather than reflection/attributes:
/// LookupPermissionKeys answers a per-case business question ("which key does *this* closed
/// TLookup use") where a handful of genuinely-different cases beats a generic templating
/// abstraction. This class answers a different question -- "what is the *complete* universe of
/// keys that exist right now" -- which is exactly a "list everything" operation, not a per-case
/// decision. PermissionKeys.cs has grown by several constants in nearly every phase since Phase
/// 1c (105+ as of Phase 14); a hand-maintained parallel array here would silently drift the next
/// time a phase adds a key (an omitted key would just never appear in the matrix -- no build
/// error, no test failure, a real tenant-facing bug), where reflection has zero drift risk by
/// construction. The cost (reflecting a few dozen fields) only ever runs when an Admin opens the
/// Role Reference matrix page -- not a hot path -- so the usual "avoid reflection-heavy generic
/// machinery" bias doesn't carry the weight here it does for a per-request LINQ-translated
/// property access.
/// </summary>
public static class PermissionKeyCatalog
{
    public static IReadOnlyList<string> AllKeys { get; } = typeof(PermissionKeys)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.FieldType == typeof(string) && f.IsLiteral && !f.IsInitOnly)
        .Select(f => (string)f.GetRawConstantValue()!)
        .OrderBy(key => key, StringComparer.Ordinal)
        .ToList();

    /// <summary>The key's first dotted segment, e.g. <c>"Sales"</c> in <c>"Sales.Invoice.View"</c>.</summary>
    public static string ModuleOf(string permissionKey) => permissionKey.Split('.')[0];
}
