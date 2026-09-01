using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Testcontainers.MsSql;

namespace ErpApp.Api.IntegrationTests;

/// <summary>
/// Spins up a real SQL Server container (Testcontainers) and boots the Api through
/// WebApplicationFactory&lt;Program&gt; against it, per roadmap Phase 0 task 6 — this is the
/// realistic-EF-Core-tests harness the rest of the app's integration tests will build on.
/// Requires a working Docker daemon on the machine running the tests.
/// </summary>
public sealed class HealthEndpointTests : IAsyncLifetime
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
                // Test-only values for every option that ValidateOnStart requires
                // (Jwt/Email/Turnstile) --
                // these never come from developer user-secrets (not present in CI) and this
                // test never actually issues a JWT or sends an email, so dummy values are fine.
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
    public async Task Health_endpoint_returns_200()
    {
        var client = _factory!.CreateClient();

        var response = await client.GetAsync("/health");
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Expected 200 OK, got {(int)response.StatusCode} {response.StatusCode}. Body: {body}");
    }
}
