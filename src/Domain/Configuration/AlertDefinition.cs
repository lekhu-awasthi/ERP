using ErpApp.Domain.Common;

namespace ErpApp.Domain.Configuration;

/// <summary>
/// A tenant's scheduled recurring email alert (FR-11.1, erp-module-scan.md Configurations §15).
/// Implements <see cref="ITenantLookupEntity"/> so the generic ListLookupsQuery/DeleteLookupCommand
/// pair covers list and delete, exactly as PrintingTemplate/CustomTemplate do -- Create/Update stay
/// concrete because the extra fields genuinely diverge.
///
/// <para><b>ScheduleTime is tenant-local (Nepal, UTC+05:45), not UTC.</b> The reference product's
/// time picker was confirmed live to show local wall-clock time (it defaulted to 21:55 while UTC
/// was 16:10), and the alert list renders "Daily (19:57)" in the same frame. Storing UTC here would
/// mean the number an admin typed is not the number they read back. The conversion happens once, in
/// the dispatcher, against <see cref="NepalTime"/> -- see that type for why a fixed offset beats a
/// TimeZoneInfo lookup for this product.</para>
///
/// <para>Recipients is stored as one comma-separated string rather than a child collection: the
/// reference product's Recipients control is a single free-text input (confirmed live -- not a chip
/// list, not a multi-select) and its grid column is the singular "RECIPIENT". Nothing joins to,
/// filters by, or aggregates over an individual recipient, so a child table would buy nothing and
/// would drag in Phase 4's full-collection-replace gotcha for free. <see cref="RecipientAddresses"/>
/// is the parsed view; it is deliberately unmapped (see AlertDefinitionConfiguration).</para>
/// </summary>
public sealed class AlertDefinition : ITenantLookupEntity
{
    /// <summary>Separator the Recipients string uses. Semicolons are normalised to this on the way
    /// in (see <see cref="ParseRecipients"/>) because both conventions are common in mail clients
    /// and an admin pasting a semicolon list should not silently get one giant invalid address.</summary>
    public const char RecipientSeparator = ',';

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Name { get; private set; } = null!;
    public AlertMedium Medium { get; private set; }
    public AlertType AlertType { get; private set; }
    public string Recipients { get; private set; } = null!;
    public AlertScheduleFrequency Frequency { get; private set; }
    public TimeOnly ScheduleTime { get; private set; }
    public bool IsActive { get; private set; }

    /// <summary>Who scheduled this. Not an authorization input -- the dispatcher never runs "as"
    /// this user (see docs/phase-20e-status.md, Decision B) -- but an alert is an outbound data
    /// feed to addresses outside the tenant, so who created it is worth keeping.</summary>
    public Guid CreatedByUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Parsed, trimmed, de-duplicated recipient addresses. Unmapped (expression-bodied, no
    /// backing field) -- EF Core never sees it.</summary>
    public IReadOnlyList<string> RecipientAddresses => ParseRecipients(Recipients);

    private AlertDefinition()
    {
    }

    public static AlertDefinition Create(
        Guid organizationId,
        string name,
        AlertMedium medium,
        AlertType alertType,
        string recipients,
        AlertScheduleFrequency frequency,
        TimeOnly scheduleTime,
        Guid createdByUserId)
    {
        return new AlertDefinition
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = name,
            Medium = medium,
            AlertType = alertType,
            Recipients = NormalizeRecipients(recipients),
            Frequency = frequency,
            // Seconds are dropped: the reference product's picker is HH:mm and the dispatcher
            // compares whole minutes. Keeping stray seconds would make the "did this occurrence
            // already fire" comparison depend on a value the UI can never show.
            ScheduleTime = new TimeOnly(scheduleTime.Hour, scheduleTime.Minute),
            IsActive = true,
            CreatedByUserId = createdByUserId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void Update(
        string name,
        AlertMedium medium,
        AlertType alertType,
        string recipients,
        AlertScheduleFrequency frequency,
        TimeOnly scheduleTime,
        bool isActive)
    {
        Name = name;
        Medium = medium;
        AlertType = alertType;
        Recipients = NormalizeRecipients(recipients);
        Frequency = frequency;
        ScheduleTime = new TimeOnly(scheduleTime.Hour, scheduleTime.Minute);
        IsActive = isActive;
    }

    /// <summary>Backs the reference product's row-level "Mark As Inactive" action.</summary>
    public void SetActive(bool isActive)
    {
        IsActive = isActive;
    }

    /// <summary>Splits on either separator, trims, drops blanks, and removes case-insensitive
    /// duplicates -- a duplicate address would otherwise mean the same person gets the same daily
    /// summary twice, which the send ledger's per-recipient uniqueness would happily allow.</summary>
    public static IReadOnlyList<string> ParseRecipients(string? recipients)
    {
        if (string.IsNullOrWhiteSpace(recipients))
        {
            return [];
        }

        return recipients
            .Split([RecipientSeparator, ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string NormalizeRecipients(string recipients) =>
        string.Join($"{RecipientSeparator} ", ParseRecipients(recipients));
}
