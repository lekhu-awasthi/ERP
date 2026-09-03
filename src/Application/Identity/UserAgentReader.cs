using System.Text.RegularExpressions;

namespace ErpApp.Application.Identity;

/// <summary>
/// Splits a raw User-Agent header into the two things the User Log report shows: the operating
/// system (its <b>Device</b> column -- "Windows 10", "Intel Mac OS X 10_15_7", "Android 10") and
/// the browser with its version (its <b>Device Info</b> column -- "Chrome 152.0.0.0",
/// "Firefox 154.0", "Edge 152.0.0.0", "Safari 1.44121.4"). Both column shapes were read live on
/// 2026-09-03; the OS strings are reproduced <i>verbatim from the header</i>, underscores and all,
/// because that is exactly what the reference product prints.
///
/// <para>Deliberately a small ordered set of patterns rather than a user-agent database. A login
/// log needs to say "a Chrome on Windows, from this address" well enough for a human to spot the
/// session they do not recognise; it does not need to identify every crawler ever shipped, and a
/// dependency that must be kept current to stay accurate is a poor trade for that. Anything
/// unrecognised returns null and the report renders a blank cell, which is the honest answer.</para>
///
/// <para><b>Order is the whole algorithm.</b> Every Chromium browser also claims "Chrome", and
/// every one of them also claims "Safari", so Edge and Opera must be tested before Chrome and
/// Chrome before Safari. A test pins each of those orderings, because getting it wrong still
/// compiles and still returns a plausible-looking answer.</para>
/// </summary>
public static class UserAgentReader
{
    private const RegexOptions Options = RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture;

    /// <summary>Guards against a hostile header burning CPU in the regex engine.</summary>
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(200);

    private static readonly (string Name, Regex Pattern)[] Browsers =
    [
        // Edge and Opera first: both also carry "Chrome/" in their agents.
        ("Edge", new Regex(@"Edg(?:e|A|iOS)?/(?<v>[\d.]+)", Options, MatchTimeout)),
        ("Opera", new Regex(@"OPR/(?<v>[\d.]+)", Options, MatchTimeout)),
        ("Chrome", new Regex(@"(?:Chrome|CriOS)/(?<v>[\d.]+)", Options, MatchTimeout)),
        ("Firefox", new Regex(@"(?:Firefox|FxiOS)/(?<v>[\d.]+)", Options, MatchTimeout)),
        // Safari last: every Chromium agent ends with a Safari token too.
        ("Safari", new Regex(@"Version/(?<v>[\d.]+).*?Safari/", Options, MatchTimeout)),
    ];

    private static readonly Regex WindowsNt = new(@"Windows NT (?<v>[\d.]+)", Options, MatchTimeout);
    private static readonly Regex MacOs = new(@"(?<v>(?:Intel |PPC )?Mac OS X[\d._ ]*)", Options, MatchTimeout);
    private static readonly Regex Android = new(@"Android (?<v>[\d.]+)", Options, MatchTimeout);
    private static readonly Regex IosDevice = new(@"(?<d>iPhone|iPad|iPod) OS (?<v>[\d_]+)", Options, MatchTimeout);

    /// <summary>
    /// Windows NT version numbers are not the names anyone knows the releases by. Windows 11
    /// reports "Windows NT 10.0" as well and is indistinguishable from 10 in the header, which is
    /// why "Windows 10" is the honest rendering of that value -- and it is what the reference
    /// product prints.
    /// </summary>
    private static readonly Dictionary<string, string> WindowsReleases = new()
    {
        ["10.0"] = "Windows 10",
        ["6.3"] = "Windows 8.1",
        ["6.2"] = "Windows 8",
        ["6.1"] = "Windows 7",
        ["6.0"] = "Windows Vista",
        ["5.1"] = "Windows XP",
    };

    /// <summary>The report's <b>Device</b> column: the operating system, or null if unrecognised.</summary>
    public static string? ReadOperatingSystem(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return null;
        }

        try
        {
            if (WindowsNt.Match(userAgent) is { Success: true } windows)
            {
                var version = windows.Groups["v"].Value;
                return WindowsReleases.TryGetValue(version, out var release) ? release : $"Windows NT {version}";
            }

            if (IosDevice.Match(userAgent) is { Success: true } ios)
            {
                return $"{ios.Groups["d"].Value} OS {ios.Groups["v"].Value}";
            }

            if (MacOs.Match(userAgent) is { Success: true } mac)
            {
                return mac.Groups["v"].Value.Trim();
            }

            if (Android.Match(userAgent) is { Success: true } android)
            {
                return $"Android {android.Groups["v"].Value}";
            }

            return userAgent.Contains("Linux", StringComparison.Ordinal) ? "Linux" : null;
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
    }

    /// <summary>The report's <b>Device Info</b> column: browser and version, or null if unrecognised.</summary>
    public static string? ReadBrowser(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return null;
        }

        try
        {
            foreach (var (name, pattern) in Browsers)
            {
                if (pattern.Match(userAgent) is { Success: true } match)
                {
                    return $"{name} {match.Groups["v"].Value}";
                }
            }

            return null;
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
    }
}
