using ErpApp.Api.Reports;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Imports.Commands.CancelImportJob;
using ErpApp.Application.Imports.Commands.CreateImportJob;
using ErpApp.Application.Imports.Queries.GetImportJob;
using ErpApp.Application.Imports.Queries.GetImportTemplate;
using ErpApp.Application.Imports.Queries.ListImportJobs;
using ErpApp.Domain.Imports;
using MediatR;

namespace ErpApp.Api.Endpoints;

/// <summary>Bulk import (roadmap Phase 21a, FR-2.9 / NFR-4.3). Every route is org-scoped and its
/// command/query carries its own permission key, so nothing here re-checks anything by hand.</summary>
public static class ImportsEndpoints
{
    public static void MapImportsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/organizations/{organizationId:guid}")
            .WithTags("Imports")
            .RequireAuthorization();

        // The downloadable template. Generated from the importer's own ImportTemplateDefinition, so
        // the file a user fills in and the parser that reads it back cannot drift -- see
        // GetImportTemplateQuery.
        group.MapGet("/import-templates/{entityType}", async (
            Guid organizationId, ImportEntityType entityType, ISender sender, CancellationToken ct) =>
        {
            var template = await sender.Send(new GetImportTemplateQuery(organizationId, entityType), ct);
            return ImportTemplateWriter.Export(template);
        });

        // A single IFormFile parameter makes this a multipart/form-data endpoint automatically, and
        // .NET then attaches antiforgery metadata that this app has no middleware for -- every
        // upload 500s without .DisableAntiforgery(). Same trap, same fix, as
        // AttachmentsEndpoints' upload route (phase-18-status.md bug #1); CSRF mitigation here is
        // the CORS allow-list plus the httpOnly JWT cookie, not antiforgery tokens.
        group.MapPost("/import-jobs", async (
            Guid organizationId,
            ImportEntityType entityType,
            ImportMode mode,
            IFormFile file,
            ISender sender,
            CancellationToken ct) =>
        {
            await using var stream = file.OpenReadStream();
            var result = await sender.Send(
                new CreateImportJobCommand(organizationId, entityType, mode, file.FileName, file.Length, stream), ct);

            return Results.Created($"/api/organizations/{organizationId}/import-jobs/{result.Id}", result);
        }).DisableAntiforgery();

        group.MapGet("/import-jobs", async (
            Guid organizationId, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ListImportJobsQuery(organizationId, page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize), ct);
            return Results.Ok(result);
        });

        // The screen's progress poll: job status/counts plus a page of row outcomes in one trip.
        group.MapGet("/import-jobs/{id:guid}", async (
            Guid organizationId,
            Guid id,
            bool? failedRowsOnly,
            int? page,
            int? pageSize,
            ISender sender,
            CancellationToken ct) =>
        {
            var result = await sender.Send(
                new GetImportJobQuery(
                    organizationId, id, failedRowsOnly ?? true, page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize),
                ct);
            return Results.Ok(result);
        });

        group.MapPost("/import-jobs/{id:guid}/cancel", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new CancelImportJobCommand(organizationId, id), ct);
            return Results.NoContent();
        });
    }
}
