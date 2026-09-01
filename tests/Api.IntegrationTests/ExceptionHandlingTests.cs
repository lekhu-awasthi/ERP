using System.Net;
using System.Net.Http.Json;
using System.Text;
using ErpApp.Api.Endpoints;
using ErpApp.Application.Common.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;

namespace ErpApp.Api.IntegrationTests;

/// <summary>
/// Proves UseAppExceptionHandler (src/Api/Middleware/ExceptionHandling.cs) maps a JSON
/// model-binding failure to a clean 400, not the framework's raw 500 -- a request body
/// deserializing "" into a nullable Guid request-DTO property (e.g. CreditNoteRequest.ReferrerId)
/// throws during ASP.NET Core's minimal-API parameter binding, before the MediatR pipeline (and
/// its own FluentValidation 400 handling) ever runs, so it previously fell through to the
/// handler's unmatched-exception branch.
/// </summary>
public sealed class ExceptionHandlingTests : IAsyncLifetime
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
        var db = scope.ServiceProvider.GetRequiredService<Infrastructure.Persistence.AppDbContext>();
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
    public async Task Empty_string_for_a_nullable_Guid_body_property_returns_400_not_500()
    {
        using var scope = _factory!.Services.CreateScope();
        var jwtTokenGenerator = scope.ServiceProvider.GetRequiredService<IJwtTokenGenerator>();
        var token = jwtTokenGenerator.GenerateToken(Guid.NewGuid(), "test@example.com");

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", $"{AuthEndpoints.AuthCookieName}={token.Value}");

        var organizationId = Guid.NewGuid();
        var body = $$"""
            {
                "contactId": "{{Guid.NewGuid()}}",
                "date": "2026-01-01",
                "reference": null,
                "lines": [],
                "referrerType": null,
                "referrerId": ""
            }
            """;

        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        var response = await client.PostAsync($"/api/organizations/{organizationId}/credit-notes", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status400BadRequest, problem!.Status);
    }
}
