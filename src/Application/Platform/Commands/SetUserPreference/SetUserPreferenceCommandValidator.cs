using System.Globalization;
using System.Text.Json;
using ErpApp.Domain.Identity;
using FluentValidation;

namespace ErpApp.Application.Platform.Commands.SetUserPreference;

/// <summary>
/// The one place an opaque preference value stops being opaque. <c>UserPreference</c> stores JSON
/// text and knows nothing about it (see its remarks); everything that keeps the table honest lives
/// here, so there is exactly one gate to add a rule to when a new key arrives.
/// </summary>
public sealed class SetUserPreferenceCommandValidator : AbstractValidator<SetUserPreferenceCommand>
{
    /// <summary>A tray this size is already far past usable -- the live product's largest observed
    /// tray held nine tiles. The cap is here so a scripted caller cannot use a per-user setting as
    /// free storage, not because anyone will reach it.</summary>
    public const int MaxQuickLinks = 30;

    public SetUserPreferenceCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();

        RuleFor(x => x.Key)
            .NotEmpty()
            .Must(UserPreferenceKeys.All.Contains)
            .WithMessage(x => $"'{x.Key}' is not a known user preference.");

        RuleFor(x => x.Value)
            .NotNull()
            .MaximumLength(UserPreference.ValueMaxLength);

        // Per-key shape checks. Each runs only for its own key, so an unknown key fails the rule
        // above rather than falling through every branch here.
        RuleFor(x => x.Value)
            .Must(BeAValidQuickLinksTray)
            .When(x => x.Key == UserPreferenceKeys.QuickLinks)
            .WithMessage(
                "Quick Links must be a JSON array of at most " + MaxQuickLinks +
                " links, each with a name and an application-relative url.");

        RuleFor(x => x.Value)
            .Must(BeAValidCalendarChoice)
            .When(x => x.Key == UserPreferenceKeys.Calendar)
            .WithMessage("The calendar preference must be the JSON string \"AD\" or \"BS\".");

        RuleFor(x => x.Value)
            .Must(BeAValidDateRange)
            .When(x => x.Key == UserPreferenceKeys.DateRange)
            .WithMessage("The date range must be a JSON object with a preset and ISO from/to dates, from on or before to.");
    }

    private static bool BeAValidQuickLinksTray(string value)
    {
        List<QuickLinkDto>? links;

        try
        {
            links = JsonSerializer.Deserialize<List<QuickLinkDto>>(value, UserPreferenceJson.Options);
        }
        catch (JsonException)
        {
            return false;
        }

        if (links is null || links.Count > MaxQuickLinks)
        {
            return false;
        }

        return links.All(link =>
            !string.IsNullOrWhiteSpace(link.Name)
            && link.Name.Length <= 100
            && (link.Area is null || link.Area.Length <= 100)
            && Enum.IsDefined(link.Kind)
            && IsApplicationRelativeUrl(link.Url));
    }

    /// <summary>
    /// A stored url that the client will later navigate to is an <b>open-redirect surface</b> if it
    /// is allowed to be absolute: a caller who can write their own preferences could park
    /// <c>https://…</c> in their own tray, and any later feature that renders someone else's tray
    /// (or that re-uses this value) would follow it. So a Quick Link url must be a single-slash
    /// application path -- not <c>//host</c> (protocol-relative), not a scheme, and with no
    /// backslash, which some browsers normalise to a forward slash.
    /// </summary>
    private static bool IsApplicationRelativeUrl(string? url) =>
        !string.IsNullOrWhiteSpace(url)
        && url.Length <= 300
        && url[0] == '/'
        && !url.StartsWith("//", StringComparison.Ordinal)
        && !url.Contains('\\', StringComparison.Ordinal)
        && !url.Contains(':', StringComparison.Ordinal);

    /// <summary>
    /// The stored range is read back and used to filter list queries, so the two dates have to be
    /// real dates in the right order -- a reversed pair would silently return nothing on every list
    /// in the app, which reads like a data-loss bug rather than a bad preference.
    /// </summary>
    private static bool BeAValidDateRange(string value)
    {
        StoredDateRange? range;

        try
        {
            range = JsonSerializer.Deserialize<StoredDateRange>(value, UserPreferenceJson.Options);
        }
        catch (JsonException)
        {
            return false;
        }

        if (range is null || string.IsNullOrWhiteSpace(range.Preset) || range.Preset.Length > 40)
        {
            return false;
        }

        return DateOnly.TryParse(range.From, CultureInfo.InvariantCulture, out var from)
            && DateOnly.TryParse(range.To, CultureInfo.InvariantCulture, out var to)
            && from <= to;
    }

    private sealed record StoredDateRange(string? Preset, string? Label, string? From, string? To);

    private static bool BeAValidCalendarChoice(string value)
    {
        try
        {
            var choice = JsonSerializer.Deserialize<string>(value, UserPreferenceJson.Options);
            return choice is "AD" or "BS";
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
