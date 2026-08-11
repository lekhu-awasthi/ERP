using ErpApp.Application.Accounting;
using ErpApp.Application.Accounting.Commands.ApproveJournalVoucher;
using ErpApp.Application.Accounting.Commands.CreateAccount;
using ErpApp.Application.Accounting.Commands.CreateAccountGroup;
using ErpApp.Application.Accounting.Commands.CreateJournalVoucher;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using ErpApp.Domain.Identity;
using ErpApp.Domain.Tenancy;
using ErpApp.Infrastructure.Persistence;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;

namespace ErpApp.Api.IntegrationTests;

/// <summary>
/// End-to-end AccountGroup -> Account -> JournalVoucher -> Approve chain against a real
/// Testcontainers SQL Server, proving the whole pipeline for real: the MediatR/Authorization
/// pipeline (a seeded Admin membership must actually be granted every Accounting.* permission
/// key by RolePermissionConfiguration's seed data), IDocumentNumberGenerator assigning a real
/// number at Approve (not Create), and GlJournalEntry.Post producing a persisted, balanced GL
/// entry -- roadmap Phase 4's exit criteria, proven once against the real database rather than
/// only against the Application.UnitTests InMemory provider.
/// </summary>
public sealed class AccountingFlowTests : IAsyncLifetime
{
    private readonly MsSqlContainer _sqlContainer = new MsSqlBuilder().Build();
    private WebApplicationFactory<Program>? _factory;
    private Guid _userId;

    public async Task InitializeAsync()
    {
        await _sqlContainer.StartAsync();
        _userId = Guid.NewGuid();

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
                ]);
            });

            // Replaces the real HttpContext-backed ICurrentUserService (see Api.Services.
            // CurrentUserService) -- this test drives commands directly through ISender, not
            // through an authenticated HTTP request, so there is no JWT/HttpContext to read a
            // UserId claim from.
            builder.ConfigureTestServices(services =>
                services.AddScoped<ICurrentUserService>(_ => new FixedCurrentUserService(_userId)));
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
    public async Task ApproveJournalVoucher_assigns_a_real_number_and_posts_a_balanced_GlJournalEntry()
    {
        Guid organizationId;

        await using (var seedScope = _factory!.Services.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();

            // OrganizationMembership.UserId is a real FK into identity.Users -- unlike the
            // Application.UnitTests InMemory fixtures (no FK enforcement), real SQL Server
            // requires an actual User row to exist first.
            var user = User.Register("Admin User", $"admin-{Guid.NewGuid():N}@example.com", "9800000000", "hash");
            db.Users.Add(user);
            _userId = user.Id;

            var organization = Organization.Create(
                "Acme Retail", "Retail", null, DateOnly.FromDateTime(DateTime.UtcNow), false,
                $"acme-{Guid.NewGuid():N}", null, null, null, null, _userId);
            db.Organizations.Add(organization);

            var membership = OrganizationMembership.CreateAccepted(organization.Id, _userId, MembershipRole.Admin);
            db.OrganizationMemberships.Add(membership);

            await db.SaveChangesAsync();
            organizationId = organization.Id;
        }

        await using var scope = _factory!.Services.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var assetGroup = await sender.Send(
            new CreateAccountGroupCommand(organizationId, "Current Assets", AccountRootType.Asset, null));
        var incomeGroup = await sender.Send(
            new CreateAccountGroupCommand(organizationId, "Sales Income", AccountRootType.Income, null));

        var cashAccount = await sender.Send(new CreateAccountCommand(organizationId, "Cash in Hand", assetGroup.Id));
        var salesAccount = await sender.Send(new CreateAccountCommand(organizationId, "Sales Revenue", incomeGroup.Id));

        var lines = new List<JournalVoucherLineInput>
        {
            new(cashAccount.Id, 1000m, 0m),
            new(salesAccount.Id, 0m, 1000m),
        };

        var journalVoucher = await sender.Send(
            new CreateJournalVoucherCommand(organizationId, DateOnly.FromDateTime(DateTime.UtcNow), "Cash sale", lines));

        Assert.Equal(JournalVoucher.DraftCode, journalVoucher.Code);

        var approved = await sender.Send(new ApproveJournalVoucherCommand(organizationId, journalVoucher.Id));

        Assert.Equal(JournalVoucherStatus.Approved, approved.Status);
        Assert.NotEqual(JournalVoucher.DraftCode, approved.Code);

        await using var verifyScope = _factory!.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var glEntry = await verifyDb.GlJournalEntries
            .Include(x => x.Lines)
            .SingleAsync(x => x.SourceDocumentType == DocumentType.JournalVoucher && x.SourceDocumentId == journalVoucher.Id);

        Assert.Equal(2, glEntry.Lines.Count);
        Assert.Equal(1000m, glEntry.Lines.Sum(x => x.Debit));
        Assert.Equal(glEntry.Lines.Sum(x => x.Debit), glEntry.Lines.Sum(x => x.Credit));
    }

    private sealed class FixedCurrentUserService(Guid userId) : ICurrentUserService
    {
        public Guid UserId { get; } = userId;
    }
}
