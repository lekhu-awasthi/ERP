using System.Text.Json;
using ErpApp.Application.Common.Filtering;
using ErpApp.Domain.Configuration;
using ErpApp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;

namespace ErpApp.Api.IntegrationTests;

/// <summary>
/// Phase 47 (phase 40 carried item #5) — the server half of
/// <c>web/src/app/shared/pagination/search-cases.json</c>.
///
/// <para><b>Why this suite and not Application.UnitTests.</b> The property the table is mostly about
/// is case-insensitivity, and that comes from SQL Server's <i>collation</i>, not from the
/// expression: single-argument <c>Contains</c> is case-insensitive on SQL Server and
/// case-<b>sensitive</b> on the InMemory provider every handler test runs against. A parity test on
/// InMemory would therefore pin a behaviour production does not have — the standing gotcha this file
/// exists inside. It has to be the real database.</para>
///
/// <para>The expression under test is the one every searchable handler writes by hand
/// (<c>x.Column.Contains(term)</c>, after <see cref="SearchTerm.Normalize"/>), against a real column
/// through the real provider. That is deliberately not a helper: <c>SearchTerm</c> says why there
/// cannot be one — a static call inside a LINQ predicate is untranslatable, and so is the
/// <c>StringComparison</c> overload.</para>
/// </summary>
public sealed class SearchParityTests : IAsyncLifetime
{
    private readonly MsSqlContainer _sqlContainer = new MsSqlBuilder().Build();
    private WebApplicationFactory<Program>? _factory;

    private sealed record Case(string Why, string Haystack, string Needle, bool Matches);

    public async Task InitializeAsync()
    {
        await _sqlContainer.StartAsync();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(
                [
                    new KeyValuePair<string, string?>(
                        "ConnectionStrings:Default", _sqlContainer.GetConnectionString()),
                    new KeyValuePair<string, string?>("Jwt:SigningKey", "integration-test-signing-key-not-for-real-use"),
                    new KeyValuePair<string, string?>("Email:From", "test@example.com"),
                    new KeyValuePair<string, string?>("Email:SmtpServer", "localhost"),
                    new KeyValuePair<string, string?>("Email:Port", "25"),
                    new KeyValuePair<string, string?>("Email:Username", "test"),
                    new KeyValuePair<string, string?>("Email:Password", "test"),
                    new KeyValuePair<string, string?>(
                        "Turnstile:SecretKey", "1x0000000000000000000000000000000AA"),
                ]);
            });
        });

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        await _sqlContainer.DisposeAsync();
    }

    [Fact]
    public async Task The_server_search_agrees_with_the_shared_table()
    {
        var cases = LoadCases();

        // Every sweep guard in this codebase asserts its own input is non-empty first: an embedded
        // resource that failed to load would make the whole theory pass over nothing (phase-34a).
        Assert.True(cases.Count >= 20, $"the shared table came back with {cases.Count} cases");

        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // One organization per case, so two cases storing the same value cannot collide on the
        // (OrganizationId, Name) uniqueness a Bank carries.
        var organizations = cases.ToDictionary(c => c, _ => Guid.NewGuid());

        foreach (var (kase, organizationId) in organizations)
        {
            db.Banks.Add(Bank.Create(organizationId, kase.Haystack));
        }

        await db.SaveChangesAsync();

        var wrong = new List<string>();

        foreach (var (kase, organizationId) in organizations)
        {
            var query = db.Banks.Where(x => x.OrganizationId == organizationId);

            // Exactly what a searchable handler does: normalise first, and compose a second Where
            // only when there is a term -- never `term == null || x.Name.Contains(term)`, which
            // hands EF a null to translate on the unrestricted branch (phase-33).
            if (SearchTerm.Normalize(kase.Needle) is { } term)
            {
                query = query.Where(x => x.Name.Contains(term));
            }

            var matched = await query.AnyAsync();

            if (matched != kase.Matches)
            {
                wrong.Add(
                    $"{kase.Why}: '{kase.Needle}' against '{kase.Haystack}' -- the table says "
                    + $"{kase.Matches}, SQL Server says {matched}");
            }
        }

        Assert.True(
            wrong.Count == 0,
            "The server's list search and the client's lookup filter have to answer the same "
            + "question, or the same typing means two things on two screens:\n  "
            + string.Join("\n  ", wrong));
    }

    private static IReadOnlyList<Case> LoadCases()
    {
        using var stream = typeof(SearchParityTests).Assembly
            .GetManifestResourceStream("ErpApp.Api.IntegrationTests.search-cases.json")
            ?? throw new InvalidOperationException("search-cases.json is not embedded.");

        using var document = JsonDocument.Parse(stream);

        return document.RootElement.GetProperty("cases").EnumerateArray()
            .Select(x => new Case(
                x.GetProperty("why").GetString()!,
                x.GetProperty("haystack").GetString()!,
                x.GetProperty("needle").GetString()!,
                x.GetProperty("matches").GetBoolean()))
            .ToList();
    }
}
