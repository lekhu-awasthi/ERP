using System.Reflection;
using System.Text.Json;
using ErpApp.Domain.Common;

namespace ErpApp.Domain.UnitTests.Common;

/// <summary>
/// Phase 39 -- the server's half of the contract in
/// <c>web/src/app/shared/rich-text/rich-text-cases.json</c>.
///
/// <para><b>Why this exists.</b> There are two rich-text sanitisers in this codebase and there have
/// to be: the server's decides what is stored, the client's decides what the user sees while
/// typing, and if they disagree the field appears to change itself on save. Two implementations
/// pinned to one another by a shared table is phase-26b's arrangement for <c>BsCalendar</c> and
/// <c>bs-date.ts</c>, and this is the second pair.</para>
///
/// <para>The file is linked in as an <b>embedded resource</b> rather than located at run time, so
/// the test cannot silently pass by failing to find it -- msbuild resolves the path, and a file
/// moved or deleted is a build error rather than a green test over nothing. The two self-checks
/// below are the same pair every sweep guard in this codebase carries.</para>
/// </summary>
public class RichTextSharedCasesTests
{
    private sealed record Case(string Why, string Input, string Expected);

    private static IReadOnlyList<Case> Cases()
    {
        using var stream = typeof(RichTextSharedCasesTests).Assembly
            .GetManifestResourceStream("ErpApp.Domain.UnitTests.rich-text-cases.json")
            ?? throw new InvalidOperationException("rich-text-cases.json is not embedded.");

        using var document = JsonDocument.Parse(stream);

        return document.RootElement.GetProperty("cases").EnumerateArray()
            .Select(x => new Case(
                x.GetProperty("why").GetString()!,
                x.GetProperty("input").GetString()!,
                x.GetProperty("expected").GetString()!))
            .ToList();
    }

    public static TheoryData<string, string, string> SharedCases()
    {
        var data = new TheoryData<string, string, string>();

        foreach (var (why, input, expected) in Cases())
        {
            data.Add(why, input, expected);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(SharedCases))]
    public void The_server_sanitizer_agrees_with_the_shared_table(string why, string input, string expected)
    {
        Assert.Equal(expected, RichText.Sanitize(input) ?? string.Empty);
        Assert.False(string.IsNullOrWhiteSpace(why), "Every case states why it is there.");
    }

    /// <summary>A table the test cannot read would make every assertion above vacuous -- phase-34a's
    /// rule, after a guard passed on an empty string Vite had handed it.</summary>
    [Fact]
    public void The_shared_table_is_not_empty()
    {
        Assert.True(Cases().Count >= 25, "The shared rich-text table has lost most of its cases.");
    }
}
