using System.Text.Json;
using System.Text.Json.Serialization;

namespace ErpApp.Application.Platform;

/// <summary>
/// One tile in a user's Quick Links tray. Deliberately the <b>same shape as a navigation result from
/// global search</b> (<c>GlobalSearchResultDto</c>'s UI half): the reference product's two features
/// share one vocabulary of navigation targets -- its Quick Links POST body is literally its search
/// endpoint's <c>nav_type: "UI"</c> row minus the discriminator -- and copying that means the
/// "add this to my Quick Links" affordance can be fed straight from a search result later without a
/// translation layer.
///
/// <para><b>A Quick Link is a screen, never a record.</b> Confirmed live: the product's
/// "Search For Quick Links" picker offers list and add screens only, and there is no way to pin a
/// contact or an invoice. So <see cref="Url"/> is an application route, and
/// <see cref="TargetKind"/> distinguishes a list from a create form rather than naming an entity.</para>
/// </summary>
public sealed record QuickLinkDto(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("area")] string Area,
    [property: JsonPropertyName("kind")] QuickLinkKind Kind,
    [property: JsonPropertyName("url")] string Url);

/// <summary>List screen or create form -- the reference product's <c>LIST</c> and <c>ADD</c>.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum QuickLinkKind
{
    List = 0,
    Add = 1,
}

/// <summary>
/// The JSON settings the Quick Links value is written and read with. Pinned in one place so the
/// validator, the handler and the tests cannot disagree about casing -- a mismatch there would store
/// a tray the client silently reads back as a list of nulls.
/// </summary>
public static class UserPreferenceJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
