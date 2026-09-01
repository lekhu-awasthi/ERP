using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Exports.Commands.CancelExportJob;
using ErpApp.Application.Exports.Commands.CreateExportJob;
using ErpApp.Application.Exports.Queries.GetExportJobArtifact;
using ErpApp.Application.Exports.Queries.ListExportJobs;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Common;
using ErpApp.Domain.Exports;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Exports;

/// <summary>
/// The request side: who may start an export, who may download one, and what the pipeline records.
/// Every case goes through the real six-behavior pipeline with real <c>RolePermission</c> rows --
/// <b>Decision F is an access-control claim, so a test that stubbed the check would prove
/// nothing.</b>
/// </summary>
public class ExportJobRequestTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Enqueue_records_who_asked_and_returns_a_queued_job()
    {
        using var host = new ExportTestHost(Now);
        var tenant = await ExportTestSeed.SeedAsync(host.NewDbContext());
        host.CurrentUser.UserId = tenant.AdminUserId;

        var result = await host.Send(new CreateExportJobCommand(tenant.OrganizationId));

        Assert.Equal(ExportJobStatus.Queued, result.Status);
        Assert.Equal(5, result.TotalCategoryCount);
        Assert.Equal(tenant.AdminUserId, result.InitiatedByUserId);
        Assert.False(result.HasArtifact);
        Assert.Null(result.FileName);
    }

    /// <summary>A full-tenant export is the largest single data-egress action in the product, so it
    /// leaves an audit row. <c>AuditBehavior</c> gives this for free off the "Create" prefix plus
    /// <c>IAuditableRequest</c> -- see DocumentType.DataExport.</summary>
    [Fact]
    public async Task Enqueue_writes_an_audit_row()
    {
        using var host = new ExportTestHost(Now);
        var tenant = await ExportTestSeed.SeedAsync(host.NewDbContext());
        host.CurrentUser.UserId = tenant.AdminUserId;

        var result = await host.Send(new CreateExportJobCommand(tenant.OrganizationId));

        var audit = await host.NewDbContext().Audits
            .SingleAsync(a => a.OrganizationId == tenant.OrganizationId);

        Assert.Equal("Create", audit.Action);
        Assert.Equal(DocumentType.DataExport, audit.DocumentType);
        Assert.Equal(result.Id, audit.DocumentId);
        Assert.Equal(tenant.AdminUserId, audit.UserId);
    }

    [Fact]
    public async Task A_second_export_is_refused_while_one_is_still_running()
    {
        using var host = new ExportTestHost(Now);
        var tenant = await ExportTestSeed.SeedAsync(host.NewDbContext());
        host.CurrentUser.UserId = tenant.AdminUserId;

        await host.Send(new CreateExportJobCommand(tenant.OrganizationId));

        var error = await Assert.ThrowsAsync<ConflictException>(() =>
            host.Send(new CreateExportJobCommand(tenant.OrganizationId)));
        Assert.Contains("already running", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The negative permission proof, against a <b>nonexistent id</b> so that a 403 rather than a 404
    /// proves the check fired before the handler ever ran.
    /// </summary>
    [Fact]
    public async Task Download_without_the_View_key_is_forbidden_before_the_handler_runs()
    {
        using var host = new ExportTestHost(Now);
        var tenant = await ExportTestSeed.SeedAsync(
            host.NewDbContext(), "A", PermissionKeys.ExportJobManage);
        host.CurrentUser.UserId = tenant.AdminUserId;

        var error = await Assert.ThrowsAsync<ForbiddenException>(() =>
            host.Send(new GetExportJobArtifactQuery(tenant.OrganizationId, Guid.NewGuid())));

        Assert.Contains(PermissionKeys.ExportJobView, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Enqueue_without_the_Manage_key_is_forbidden()
    {
        using var host = new ExportTestHost(Now);
        var tenant = await ExportTestSeed.SeedAsync(
            host.NewDbContext(), "A", PermissionKeys.ExportJobView);
        host.CurrentUser.UserId = tenant.AdminUserId;

        var error = await Assert.ThrowsAsync<ForbiddenException>(() =>
            host.Send(new CreateExportJobCommand(tenant.OrganizationId)));

        Assert.Contains(PermissionKeys.ExportJobManage, error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>Decision F's hard boundary.</b> Org B's Admin holds the very same Admin role and therefore
    /// the very same permission keys -- the only thing standing between them and another tenant's
    /// entire data set is the org-membership check plus the handler's own OrganizationId filter.
    /// Proved both ways round: by A's job id, and by A's organization id.
    /// </summary>
    [Fact]
    public async Task A_member_of_another_organization_cannot_download_this_ones_export()
    {
        using var host = new ExportTestHost(Now);
        var db = host.NewDbContext();
        var tenantA = await ExportTestSeed.SeedAsync(db, "A");
        var tenantB = await ExportTestSeed.SeedAsync(db, "B");
        var jobId = await ExportTestSeed.QueueJobAsync(db, tenantA, Now);

        await host.NewProcessor().ProcessNextAsync(CancellationToken.None);

        host.CurrentUser.UserId = tenantB.AdminUserId;

        // Asking under A's organization id: rejected as a non-member, before the handler.
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            host.Send(new GetExportJobArtifactQuery(tenantA.OrganizationId, jobId)));

        // Asking under their own organization id with A's job id: the handler's own hand-written
        // OrganizationId filter is what refuses this one -- there is no global query filter here.
        await Assert.ThrowsAsync<NotFoundException>(() =>
            host.Send(new GetExportJobArtifactQuery(tenantB.OrganizationId, jobId)));

        // ...and their own listing does not mention it.
        var listed = await host.Send(new ListExportJobsQuery(tenantB.OrganizationId));
        Assert.Empty(listed.Items);
    }

    /// <summary>Any Admin of the same organization may download a colleague's export -- the
    /// deliberate half of Decision F, and the reason it is a decision rather than an oversight.</summary>
    [Fact]
    public async Task Another_admin_of_the_same_organization_may_download_it()
    {
        using var host = new ExportTestHost(Now);
        var db = host.NewDbContext();
        var tenant = await ExportTestSeed.SeedAsync(db);
        var jobId = await ExportTestSeed.QueueJobAsync(db, tenant, Now);
        await host.NewProcessor().ProcessNextAsync(CancellationToken.None);

        var colleague = ErpApp.Domain.Identity.User.Register(
            "Sita Sharma", $"sita-{Guid.NewGuid():N}@acme.test", "9800000001", "hash");
        db.Users.Add(colleague);
        db.OrganizationMemberships.Add(ErpApp.Domain.Tenancy.OrganizationMembership.CreateAccepted(
            tenant.OrganizationId, colleague.Id, ErpApp.Domain.Tenancy.MembershipRole.Admin));
        await db.SaveChangesAsync();

        host.CurrentUser.UserId = colleague.Id;
        var artifact = await host.Send(new GetExportJobArtifactQuery(tenant.OrganizationId, jobId));

        Assert.False(string.IsNullOrWhiteSpace(artifact.StorageKey));
        Assert.EndsWith(".xlsx", artifact.FileName, StringComparison.Ordinal);
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", artifact.ContentType);
    }

    [Fact]
    public async Task Cancelling_a_queued_export_retires_it_immediately()
    {
        using var host = new ExportTestHost(Now);
        var tenant = await ExportTestSeed.SeedAsync(host.NewDbContext());
        host.CurrentUser.UserId = tenant.AdminUserId;

        var created = await host.Send(new CreateExportJobCommand(tenant.OrganizationId));
        await host.Send(new CancelExportJobCommand(tenant.OrganizationId, created.Id));

        var job = await host.NewDbContext().ExportJobs.AsNoTracking().SingleAsync(j => j.Id == created.Id);
        Assert.Equal(ExportJobStatus.Cancelled, job.Status);

        // ...and nothing is left for the runner to pick up.
        Assert.False(await host.NewProcessor().ProcessNextAsync(CancellationToken.None));

        var again = await Assert.ThrowsAsync<ConflictException>(() =>
            host.Send(new CancelExportJobCommand(tenant.OrganizationId, created.Id)));
        Assert.Contains("already finished", again.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_listing_never_carries_the_storage_key()
    {
        using var host = new ExportTestHost(Now);
        var db = host.NewDbContext();
        var tenant = await ExportTestSeed.SeedAsync(db);
        await ExportTestSeed.QueueJobAsync(db, tenant, Now);
        await host.NewProcessor().ProcessNextAsync(CancellationToken.None);

        host.CurrentUser.UserId = tenant.AdminUserId;
        var listed = await host.Send(new ListExportJobsQuery(tenant.OrganizationId));

        var row = Assert.Single(listed.Items);
        Assert.True(row.HasArtifact);
        Assert.DoesNotContain(
            typeof(ExportJobSummary).GetProperties(),
            p => p.Name.Contains("StorageKey", StringComparison.OrdinalIgnoreCase));
    }
}
