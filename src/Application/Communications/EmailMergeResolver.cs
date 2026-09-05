using System.Globalization;
using ErpApp.Domain.Configuration;

namespace ErpApp.Application.Communications;

/// <summary>
/// Substitutes <c>$[TOKEN]$</c> merge fields. See <see cref="EmailMergeFields"/> for the catalogue
/// and for why the accepted token set is wider than the offered one.
///
/// <para><b>An unknown token is left standing, not blanked.</b> <c>$[TOATL]$</c> reaching a
/// customer's inbox as itself is obviously a mistake somebody will fix; the same typo silently
/// resolving to an empty string produces "Invoice Amount: " and looks like a bug in the accounting.
/// The dialog previews the resolved text before anything is sent (live behaviour, confirmed), so a
/// standing token is caught by the person best placed to catch it.</para>
///
/// <para><b>Amounts and dates are formatted here, once.</b> Amounts use invariant-culture
/// <c>N2</c> — thousands separators, two decimals — matching the live <c>NPR 50,850.00</c>; the
/// currency code is its own token so a template decides whether to show it. Dates use
/// <c>dd-MM-yyyy</c> <b>AD</b>, which is what the reference product renders in this dialog even on
/// a tenant that shows BS everywhere else (docs/phase-30-status.md, Step 1.4, finding 3). That is a
/// deliberate exception to phase 27b's <c>RequestCalendar</c> sweep and is recorded as one: an
/// email is read by a counterparty who may not share the tenant's calendar preference, and unlike a
/// PDF the reader cannot see which calendar they are looking at.</para>
/// </summary>
public static class EmailMergeResolver
{
    public const string TokenPrefix = "$[";
    public const string TokenSuffix = "]$";

    /// <summary>AD, day-first — see the type-level remarks for why this does not go through
    /// <c>RequestCalendar</c>.</summary>
    public const string DateFormat = "dd-MM-yyyy";

    public static string FormatAmount(decimal value) => value.ToString("N2", CultureInfo.InvariantCulture);

    public static string FormatDate(DateOnly? value) =>
        value?.ToString(DateFormat, CultureInfo.InvariantCulture) ?? string.Empty;

    /// <summary>
    /// Replaces every known token in <paramref name="text"/>. <paramref name="values"/> is keyed by
    /// bare token name; <paramref name="context"/> supplies the live aliases.
    /// </summary>
    public static string Apply(
        string? text, IReadOnlyDictionary<string, string> values, EmailTemplateContext context)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var resolved = text;

        foreach (var (token, value) in values)
        {
            resolved = resolved.Replace($"{TokenPrefix}{token}{TokenSuffix}", value, StringComparison.Ordinal);
        }

        // Aliases second, so an explicitly supplied value always wins over the alias of the same
        // field -- they resolve to the same string anyway, but the ordering makes that a fact
        // rather than a coincidence.
        foreach (var (alias, canonical) in EmailMergeFields.AliasesFor(context))
        {
            if (values.TryGetValue(canonical, out var value))
            {
                resolved = resolved.Replace($"{TokenPrefix}{alias}{TokenSuffix}", value, StringComparison.Ordinal);
            }
        }

        return resolved;
    }

    /// <summary>
    /// Tokens still standing after <see cref="Apply"/> — every <c>$[…]$</c> pair left in the text.
    /// Used by the prepare query to warn the composer, and by tests. Returns bare token names.
    /// </summary>
    public static IReadOnlyList<string> UnresolvedTokens(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var tokens = new List<string>();
        var index = 0;

        while (true)
        {
            var start = text.IndexOf(TokenPrefix, index, StringComparison.Ordinal);
            if (start < 0)
            {
                break;
            }

            var end = text.IndexOf(TokenSuffix, start + TokenPrefix.Length, StringComparison.Ordinal);
            if (end < 0)
            {
                break;
            }

            var token = text[(start + TokenPrefix.Length)..end];
            if (token.Length > 0 && !tokens.Contains(token, StringComparer.Ordinal))
            {
                tokens.Add(token);
            }

            index = end + TokenSuffix.Length;
        }

        return tokens;
    }
}
