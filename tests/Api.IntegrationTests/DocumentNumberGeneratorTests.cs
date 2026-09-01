using ErpApp.Application.Common.Numbering;
using ErpApp.Domain.Common;
using ErpApp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;

namespace ErpApp.Api.IntegrationTests;

/// <summary>
/// Proves IDocumentNumberGenerator's atomic-increment implementation (Infrastructure.Persistence.
/// DocumentNumberGenerator) never hands out a duplicate number under concurrent callers --
/// architecture-spec.md §3.1's core requirement ("duplicates are not [tolerated]", gaps are fine).
/// This has to be a real-SQL-Server test (Testcontainers), not an Application.UnitTests InMemory
/// one: the generator's race-safety depends on SQL Server's row-lock behavior for an atomic
/// "UPDATE ... OUTPUT" statement and a real unique-index constraint violation on the lazy-create
/// path, neither of which the InMemory provider can exercise.
/// </summary>
public sealed class DocumentNumberGeneratorTests : IAsyncLifetime
{
    private readonly MsSqlContainer _sqlContainer = new MsSqlBuilder().Build();
    private WebApplicationFactory<Program>? _factory;

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

                    // Turnstile (Phase 20g) validates on start like Jwt/Email do, and no
                    // integration test ever registers a user, so any non-empty value satisfies it.
                    // Cloudflare's documented always-passes dummy secret is used rather than
                    // "test" so that the value is self-explaining if one ever does.
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
    public async Task GetNextNumberAsync_never_returns_a_duplicate_under_concurrent_callers()
    {
        var organizationId = Guid.NewGuid();
        const int callerCount = 20;

        // Each concurrent caller needs its own DI scope (and thus its own AppDbContext instance)
        // -- DbContext is not thread-safe/reentrant, matching how a real concurrent set of HTTP
        // requests would each get their own scoped instance.
        var results = await Task.WhenAll(Enumerable.Range(0, callerCount).Select(async _ =>
        {
            await using var scope = _factory!.Services.CreateAsyncScope();
            var generator = scope.ServiceProvider.GetRequiredService<IDocumentNumberGenerator>();
            return await generator.GetNextNumberAsync(organizationId, DocumentType.Invoice, CancellationToken.None);
        }));

        Assert.Equal(callerCount, results.Length);
        Assert.Equal(callerCount, results.Distinct().Count());
    }

    [Fact]
    public async Task GetNextNumberAsync_lazily_creates_exactly_one_rule_row_under_concurrent_first_callers()
    {
        var organizationId = Guid.NewGuid();
        const int callerCount = 10;

        await Task.WhenAll(Enumerable.Range(0, callerCount).Select(async _ =>
        {
            await using var scope = _factory!.Services.CreateAsyncScope();
            var generator = scope.ServiceProvider.GetRequiredService<IDocumentNumberGenerator>();
            await generator.GetNextNumberAsync(organizationId, DocumentType.PurchaseBill, CancellationToken.None);
        }));

        await using var verifyScope = _factory!.Services.CreateAsyncScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ruleCount = db.DocumentNumberingRules.Count(
            r => r.OrganizationId == organizationId && r.DocumentType == DocumentType.PurchaseBill);

        Assert.Equal(1, ruleCount);
    }
}
