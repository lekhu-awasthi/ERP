using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Storage;
using ErpApp.Application.Exports.Commands.CancelExportJob;
using ErpApp.Application.Exports.Commands.CreateExportJob;
using ErpApp.Application.Exports.Queries.GetExportJobArtifact;
using ErpApp.Application.Exports.Queries.ListExportJobs;
using MediatR;

namespace ErpApp.Api.Endpoints;

/// <summary>Full-tenant data export (roadmap Phase 21b, FR-2.8 / NFR-4.3). Every route is org-scoped
/// and its command/query carries its own permission key, so nothing here re-checks anything by
/// hand.</summary>
public static class ExportsEndpoints
{
    public static void MapExportsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/organizations/{organizationId:guid}")
            .WithTags("Exports")
            .RequireAuthorization();

        // No body: an export takes no parameters beyond the tenant (Decision A -- FR-2.8's five
        // categories, always, no checkboxes and no date range).
        group.MapPost("/export-jobs", async (
            Guid organizationId, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new CreateExportJobCommand(organizationId), ct);
            return Results.Created($"/api/organizations/{organizationId}/export-jobs/{result.Id}", result);
        });

        group.MapGet("/export-jobs", async (
            Guid organizationId, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ListExportJobsQuery(organizationId, page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize), ct);
            return Results.Ok(result);
        });

        // The only way a generated export ever leaves the server (Decision F). IFileStorage has no
        // "resolve to a public URL" method precisely so this is the only door: the query runs the
        // full pipeline (Configuration.ExportJob.View plus org membership) and re-filters by
        // OrganizationId, and only then is a storage key resolved to a stream. Same shape as
        // AttachmentsEndpoints' download route.
        //
        // Results.File streams the FileStream asynchronously, so the Kestrel synchronous-write
        // constraint that bites ClosedXML (phase-16c bug #3) does not apply here -- the workbook was
        // already written to a buffer and then to storage by the background job, long before this
        // request existed.
        group.MapGet("/export-jobs/{id:guid}/download", async (
            Guid organizationId, Guid id, ISender sender, IFileStorage fileStorage, CancellationToken ct) =>
        {
            var artifact = await sender.Send(new GetExportJobArtifactQuery(organizationId, id), ct);
            var stream = await fileStorage.OpenReadAsync(artifact.StorageKey, ct);
            return Results.File(stream, artifact.ContentType, artifact.FileName);
        });

        group.MapPost("/export-jobs/{id:guid}/cancel", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new CancelExportJobCommand(organizationId, id), ct);
            return Results.NoContent();
        });
    }
}
