namespace ErpApp.Application.Platform;

/// <summary>
/// The closed vocabulary of per-user setting keys. <see cref="UserPreference"/> rows store an opaque
/// key and an opaque JSON value; this class is where the two stop being opaque.
///
/// <para><b>Why a closed set rather than free-form keys.</b> A per-user key-value store with no
/// vocabulary is a place for any client to write anything, which makes it impossible to reason about
/// what the table holds, impossible to migrate, and a small storage-amplification surface for a
/// hostile caller. <c>SetUserPreferenceCommandValidator</c> refuses a key that is not in
/// <see cref="All"/>, so adding a preference is a one-line change here plus a validator test --
/// still no migration, which was the whole point of a JSON value.</para>
/// </summary>
public static class UserPreferenceKeys
{
    /// <summary>
    /// The user's Quick Links tray for this organization: a JSON array of
    /// <c>QuickLinkDto</c>, in display order. Whole-list replace on every save, matching the
    /// reference product's own <c>POST /quick-links</c> (observed -- there is no per-link endpoint).
    /// </summary>
    public const string QuickLinks = "quick-links";

    /// <summary>
    /// The AD/BS calendar choice -- phase 23's Decision C, now that a store exists for it. The value
    /// is the JSON string <c>"AD"</c> or <c>"BS"</c>. The client keeps its <c>localStorage</c> copy
    /// as a synchronous cache so the first paint never flashes the wrong calendar; this row is what
    /// makes the choice follow the user to another machine.
    /// </summary>
    public const string Calendar = "calendar";

    /// <summary>
    /// Phase 34b -- the top bar's global date range, as a JSON object
    /// <c>{"preset":"last-30","label":"Last 30 days","from":"2026-08-11","to":"2026-09-10"}</c>.
    ///
    /// <para>The reference product keeps this in <c>localStorage</c> (<c>TOP_DATE_FROM</c>,
    /// <c>TOP_DATE_TO</c>, <c>TOP_DATE_NAME</c> -- read live on 2026-09-10), so its users lose the
    /// setting on every new machine. This store is exactly the shape that problem wants, so the
    /// range lives here instead. A relative preset is re-derived from today on load rather than
    /// restored literally: "Last 7 days" stored a week ago must not come back meaning that week.</para>
    /// </summary>
    public const string DateRange = "date-range";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.Ordinal) { QuickLinks, Calendar, DateRange };
}
