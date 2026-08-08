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
                config.AddInMemoryCollection(
                [
                    new KeyValuePair<string, string?>(
                        "ConnectionStrings:Default", _sqlContainer.GetConnectionString()),
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

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
