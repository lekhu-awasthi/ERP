using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using ErpApp.Api.Endpoints;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Identity;
using ErpApp.Domain.Tenancy;
using ErpApp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;

namespace ErpApp.Api.IntegrationTests;

/// <summary>
/// Phase 22 (FR-10.3). Drives the Document inbox over <b>real HTTP against the real host</b>,
/// because the two things that matter here surface nowhere else:
///
/// <list type="number">
/// <item>a Minimal API endpoint binding <c>IFormFile</c> gets antiforgery metadata attached
/// automatically, and 500s with "contains anti-forgery metadata, but a middleware was not found"
/// unless <c>.DisableAntiforgery()</c> is applied -- this app has no antiforgery middleware at all.
/// An InMemory-provider unit test never touches real endpoint metadata, so only a real multipart
/// POST proves the upload route works (the Phase 18 bug, restated);</item>
/// <item>the file's bytes come back byte-identical through the authenticated stream endpoint,
/// against a real Kestrel response.</item>
/// </list>
///
/// <para>Also proves the negative path at the wire level: another organization's document id
/// returns 404, never 200 with file bytes.</para>
/// </summary>
public sealed class DocumentInboxUploadTests : IAsyncLifetime
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

                    // Every ValidateOnStart option must be present in all four host-booting suites'
                    // in-memory config or CI goes red (CLAUDE.md's Known Gotchas). Phase 22's
                    // DocumentExtraction options deliberately have no ValidateOnStart, so this
                    // suite needs no key for them -- and the extractor correctly reports itself
                    // unconfigured, which is exactly the state a CI runner should be in.
                    new KeyValuePair<string, string?>(
                        "Turnstile:SecretKey", "1x0000000000000000000000000000000AA"),

                    // Keeps uploaded test files out of the repo's own App_Data folder.
                    new KeyValuePair<string, string?>(
                        "FileStorage:RootPath", Path.Combine(Path.GetTempPath(), $"erpapp-inbox-{Guid.NewGuid():N}")),
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
    public async Task A_real_multipart_upload_round_trips_the_same_bytes_through_the_authenticated_stream()
    {
        var (organizationId, client) = await SeedOrganizationAsync("Acme Retail");
        var bytes = Encoding.UTF8.GetBytes("%PDF-1.4 pretend scanned bill");

        var uploaded = await UploadAsync(client, organizationId, "supplier-bill.pdf", bytes);

        Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode);
        var document = await uploaded.Content.ReadFromJsonAsync<UploadedRow>();
        Assert.NotNull(document);
        Assert.Equal("supplier-bill.pdf", document.FileName);
        Assert.Equal("Pending", document.Status);
        Assert.Equal("NotAttempted", document.ExtractionStatus);
        Assert.False(document.IsLinked);

        var download = await client.GetAsync(
            new Uri($"/api/organizations/{organizationId}/workflow/inbox-documents/{document.Id}/download", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(bytes, await download.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task An_over_size_or_disallowed_upload_is_rejected_with_400()
    {
        var (organizationId, client) = await SeedOrganizationAsync("Acme Retail");

        var disallowed = await UploadAsync(client, organizationId, "malware.exe", Encoding.UTF8.GetBytes("MZ"));
        Assert.Equal(HttpStatusCode.BadRequest, disallowed.StatusCode);

        // One byte over the 10 MB cap.
        var oversize = await UploadAsync(client, organizationId, "huge.pdf", new byte[(10 * 1024 * 1024) + 1]);
        Assert.Equal(HttpStatusCode.BadRequest, oversize.StatusCode);
    }

    [Fact]
    public async Task Another_organizations_document_is_not_downloadable_and_is_absent_from_the_list()
    {
        var (organizationA, clientA) = await SeedOrganizationAsync("Org A");
        var (organizationB, clientB) = await SeedOrganizationAsync("Org B");

        var uploaded = await UploadAsync(clientA, organizationA, "a-bill.pdf", Encoding.UTF8.GetBytes("A"));
        var document = await uploaded.Content.ReadFromJsonAsync<UploadedRow>();
        Assert.NotNull(document);

        // B asking for A's document id under B's own organization: 404, never 200 with file bytes.
        var crossTenant = await clientB.GetAsync(
            new Uri($"/api/organizations/{organizationB}/workflow/inbox-documents/{document.Id}/download", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, crossTenant.StatusCode);

        // B asking under A's organization id: 403 from AuthorizationBehavior's org-membership check,
        // before the handler runs at all.
        var foreignOrganization = await clientB.GetAsync(
            new Uri($"/api/organizations/{organizationA}/workflow/inbox-documents/{document.Id}/download", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Forbidden, foreignOrganization.StatusCode);

        var list = await clientB.GetFromJsonAsync<ListResponse>(
            new Uri($"/api/organizations/{organizationB}/workflow/inbox-documents", UriKind.Relative));
        Assert.NotNull(list);
        Assert.DoesNotContain(list.Items, x => x.Id == document.Id);
        Assert.DoesNotContain(list.Items, x => x.FileName == "a-bill.pdf");
    }

    /// <summary>With no <c>DocumentExtraction:ApiKey</c> configured -- exactly a CI runner's state --
    /// the setting endpoint says so plainly rather than the host failing to boot.</summary>
    [Fact]
    public async Task The_extraction_setting_reports_off_and_unconfigured_on_a_fresh_organization()
    {
        var (organizationId, client) = await SeedOrganizationAsync("Acme Retail");

        var setting = await client.GetFromJsonAsync<ExtractionSetting>(
            new Uri($"/api/organizations/{organizationId}/ai-document-extraction", UriKind.Relative));

        Assert.NotNull(setting);
        Assert.False(setting.Enabled);
        Assert.False(setting.ExtractorConfigured);
    }

    /// <summary>Awaits inside rather than returning the Task -- disposing the
    /// MultipartFormDataContent before the send completes throws ObjectDisposedException deep inside
    /// TestHost's stream plumbing, with a stack trace that says nothing about the real cause.</summary>
    private static async Task<HttpResponseMessage> UploadAsync(
        HttpClient client, Guid organizationId, string fileName, byte[] bytes)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);

        return await client.PostAsync(
            new Uri($"/api/organizations/{organizationId}/workflow/inbox-documents", UriKind.Relative), form);
    }

    private async Task<(Guid OrganizationId, HttpClient Client)> SeedOrganizationAsync(string name)
    {
        Guid organizationId;
        Guid userId;
        string email;

        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            email = $"admin-{Guid.NewGuid():N}@example.com";
            var user = User.Register("Admin User", email, "9800000000", "hash");
            db.Users.Add(user);
            userId = user.Id;

            var organization = Organization.Create(
                name, "Retail", null, DateOnly.FromDateTime(DateTime.UtcNow), false,
                $"ws-{Guid.NewGuid():N}", null, null, null, null, userId);
            db.Organizations.Add(organization);
            db.TenantSettings.Add(TenantSettings.CreateDefault(organization.Id));
            db.OrganizationMemberships.Add(
                OrganizationMembership.CreateAccepted(organization.Id, userId, MembershipRole.Admin));

            await db.SaveChangesAsync();
            organizationId = organization.Id;
        }

        using var tokenScope = _factory!.Services.CreateScope();
        var token = tokenScope.ServiceProvider.GetRequiredService<IJwtTokenGenerator>().GenerateToken(userId, email);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", $"{AuthEndpoints.AuthCookieName}={token.Value}");

        return (organizationId, client);
    }

    private sealed record UploadedRow(
        Guid Id, string FileName, string Status, string ExtractionStatus, bool IsLinked, bool IsExtractable);

    private sealed record ListResponse(IReadOnlyList<UploadedRow> Items, int Page, int PageSize, int TotalCount);

    private sealed record ExtractionSetting(bool Enabled, bool ExtractorConfigured, string? ModelId);
}
