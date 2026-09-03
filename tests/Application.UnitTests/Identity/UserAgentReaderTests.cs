using ErpApp.Application.Identity;

namespace ErpApp.Application.UnitTests.Identity;

/// <summary>
/// The agents below are real strings of the shape the live User Log rendered on 2026-09-03
/// ("Windows 10" / "Chrome 152.0.0.0", "Intel Mac OS X 10_15_7", "Android 10", "Firefox 154.0",
/// "Edge 152.0.0.0"), so these tests pin the parser against the output the report is copying.
/// </summary>
public class UserAgentReaderTests
{
    private const string WindowsChrome =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/152.0.0.0 Safari/537.36";

    private const string WindowsEdge =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/152.0.0.0 Safari/537.36 Edg/152.0.0.0";

    private const string WindowsOpera =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/152.0.0.0 Safari/537.36 OPR/110.0.0.0";

    private const string MacSafari =
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.4 Safari/605.1.15";

    [Theory]
    [InlineData(WindowsChrome, "Windows 10")]
    [InlineData(MacSafari, "Intel Mac OS X 10_15_7")]
    [InlineData("Mozilla/5.0 (Linux; Android 10; SM-G975F) AppleWebKit/537.36 Chrome/152.0.0.0 Mobile Safari/537.36", "Android 10")]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 17_4 like Mac OS X) AppleWebKit/605.1.15 Version/17.4 Mobile Safari/604.1", "iPhone OS 17_4")]
    [InlineData("Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 Chrome/152.0.0.0 Safari/537.36", "Linux")]
    [InlineData("Mozilla/5.0 (Windows NT 6.1; Win64; x64) Chrome/109.0.0.0 Safari/537.36", "Windows 7")]
    public void ReadOperatingSystem_renders_the_reports_Device_column(string agent, string expected) =>
        Assert.Equal(expected, UserAgentReader.ReadOperatingSystem(agent));

    [Theory]
    [InlineData(WindowsChrome, "Chrome 152.0.0.0")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:154.0) Gecko/20100101 Firefox/154.0", "Firefox 154.0")]
    [InlineData(MacSafari, "Safari 17.4")]
    public void ReadBrowser_renders_the_reports_Device_Info_column(string agent, string expected) =>
        Assert.Equal(expected, UserAgentReader.ReadBrowser(agent));

    /// <summary>
    /// The ordering trap the reader's own remarks name. An Edge agent contains "Chrome/152.0.0.0"
    /// and "Safari/537.36" as well as "Edg/152.0.0.0"; test Chrome first and every Edge session in
    /// the log is silently mislabelled Chrome -- which compiles, runs, and looks entirely plausible.
    /// </summary>
    [Fact]
    public void ReadBrowser_prefers_Edge_over_the_Chrome_and_Safari_tokens_the_same_agent_carries()
    {
        Assert.Contains("Chrome/", WindowsEdge, StringComparison.Ordinal);
        Assert.Contains("Safari/", WindowsEdge, StringComparison.Ordinal);

        Assert.Equal("Edge 152.0.0.0", UserAgentReader.ReadBrowser(WindowsEdge));
    }

    [Fact]
    public void ReadBrowser_prefers_Opera_over_the_Chrome_token_the_same_agent_carries()
    {
        Assert.Contains("Chrome/", WindowsOpera, StringComparison.Ordinal);

        Assert.Equal("Opera 110.0.0.0", UserAgentReader.ReadBrowser(WindowsOpera));
    }

    [Fact]
    public void ReadBrowser_prefers_Chrome_over_the_Safari_token_every_Chromium_agent_ends_with()
    {
        Assert.Contains("Safari/", WindowsChrome, StringComparison.Ordinal);

        Assert.Equal("Chrome 152.0.0.0", UserAgentReader.ReadBrowser(WindowsChrome));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("curl/8.7.1")]
    public void An_absent_or_unrecognised_agent_reads_as_null_rather_than_a_guess(string? agent)
    {
        Assert.Null(UserAgentReader.ReadOperatingSystem(agent));
        Assert.Null(UserAgentReader.ReadBrowser(agent));
    }
}
