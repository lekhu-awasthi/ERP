namespace ErpApp.Domain.Identity;

/// <summary>
/// One setting a <b>user</b> made for <b>themselves</b>, inside one organization -- phase 33's
/// answer to the store phase 23 declined to build.
///
/// <para><b>Why this exists now when it did not then.</b> Phase 23's Decision C weighed a table, a
/// configuration, a command, a query and permission plumbing against a single boolean (the AD/BS
/// calendar toggle) and put the boolean in browser <c>localStorage</c>. That was the right trade for
/// one boolean. Phase 33's Quick Links tray cannot make the same trade: it is server-stored in the
/// reference product (observed -- <c>GET/POST /quick-links</c>), and a shortcut tray that vanished
/// when you opened the app on a different machine would be a worse feature than none. So the whole
/// cost phase 23 declined is paid regardless, and every later per-user setting rides it for free.</para>
///
/// <para><b>Why a row per key rather than one blob per user.</b> A single JSON document per user
/// would make every preference a read-modify-write of the same row, so saving Quick Links and
/// flipping the calendar toggle in two tabs would silently discard one of them. A row keyed
/// <c>(OrganizationId, UserId, <see cref="Key"/>)</c> makes each preference independently
/// writable, which is what "last writer wins" should mean -- last writer of *that setting*, not of
/// everything the user has ever set.</para>
///
/// <para><b>Why the value is opaque JSON.</b> The two day-one consumers could not be less alike --
/// an ordered list of navigation targets, and a two-valued enum -- and the vocabulary will keep
/// growing. Typed columns would make every new preference a migration; a JSON string makes it a
/// constant in <c>UserPreferenceKeys</c> and a DTO in the Application layer. The Domain deliberately
/// knows nothing about what any particular key means, which is why there is no validation of
/// <see cref="Value"/> here beyond its length.</para>
///
/// <para><b>Why it carries an OrganizationId.</b> A user belongs to several organizations and their
/// Quick Links are per-tenant in the reference product (cadehi's tray and moonbeam's differ). Making
/// the row tenant-scoped also keeps it inside the one authorization mechanism this codebase has:
/// <c>AuthorizationBehavior</c> only verifies membership for an <c>IOrganizationScoped</c> request.</para>
/// </summary>
public sealed class UserPreference
{
    /// <summary>Long enough for a large Quick Links tray with room to spare, bounded so a hostile
    /// caller cannot write an unbounded row. The command validator enforces the same limit, so a
    /// caller gets a 400 rather than a truncation or a database error.</summary>
    public const int ValueMaxLength = 8000;

    public const int KeyMaxLength = 100;

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid UserId { get; private set; }

    /// <summary>One of <c>UserPreferenceKeys</c>' constants. Free-form here; the Application layer's
    /// validator is what refuses an unknown key, for the same reason the value is opaque.</summary>
    public string Key { get; private set; } = null!;

    /// <summary>The setting, serialized. Meaningless to the Domain -- see the type's remarks.</summary>
    public string Value { get; private set; } = null!;

    public DateTimeOffset UpdatedAt { get; private set; }

    private UserPreference()
    {
    }

    public static UserPreference Create(Guid organizationId, Guid userId, string key, string value, DateTimeOffset updatedAt)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("A user preference needs a key.");
        }

        if (value is null)
        {
            throw new InvalidOperationException("A user preference needs a value; clear it by deleting the row.");
        }

        if (value.Length > ValueMaxLength)
        {
            throw new InvalidOperationException($"A user preference value cannot exceed {ValueMaxLength} characters.");
        }

        return new UserPreference
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            UserId = userId,
            Key = key.Trim(),
            Value = value,
            UpdatedAt = updatedAt,
        };
    }

    /// <summary>Replaces the stored value wholesale. There is no partial update and no merge: every
    /// consumer of this table reads the whole setting and writes the whole setting back, which is
    /// also exactly what the reference product's Quick Links POST does.</summary>
    public void SetValue(string value, DateTimeOffset updatedAt)
    {
        if (value is null)
        {
            throw new InvalidOperationException("A user preference needs a value; clear it by deleting the row.");
        }

        if (value.Length > ValueMaxLength)
        {
            throw new InvalidOperationException($"A user preference value cannot exceed {ValueMaxLength} characters.");
        }

        Value = value;
        UpdatedAt = updatedAt;
    }
}
