using ErpApp.Api.Reports;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Workflow.Commands.CreateTask;
using ErpApp.Application.Workflow.Commands.UpdateTask;
using ErpApp.Application.Workflow.Commands.UpdateTaskStatus;
using ErpApp.Application.Workflow.Queries.ListTasks;
using ErpApp.Application.Workflow.Queries.SystemAuditReport;
using ErpApp.Application.Workflow.Queries.TransactionApproval;
using ErpApp.Domain.Common;
using ErpApp.Domain.Workflow;
using MediatR;

namespace ErpApp.Api.Endpoints;

public static class WorkflowEndpoints
{
    public static void MapWorkflowEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/organizations/{organizationId:guid}")
            .WithTags("Workflow")
            .RequireAuthorization();

        group.MapGet("/workflow/transaction-approval-queue", async (
            Guid organizationId, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new TransactionApprovalQuery(organizationId), ct);
            return Results.Ok(result);
        });

        group.MapGet("/reports/system-audit", async (
            Guid organizationId, Guid? userId, string? action, DocumentType? documentType,
            DateOnly? fromDate, DateOnly? toDate, int? page, int? pageSize,
            ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new SystemAuditReportQuery(
                    organizationId, userId, action, documentType, fromDate, toDate,
                    page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize),
                ct);
            return Results.Ok(result);
        });

        group.MapGet("/reports/system-audit/export", async (
            Guid organizationId, Guid? userId, string? action, DocumentType? documentType,
            DateOnly? fromDate, DateOnly? toDate, bool full, int? page, int? pageSize,
            ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new SystemAuditReportQuery(
                    organizationId, userId, action, documentType, fromDate, toDate,
                    page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize, ExportAll: full),
                ct);
            return ReportSpreadsheetExporter.ExportSystemAuditReport(result);
        });

        MapTaskEndpoints(group);
    }

    private static void MapTaskEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/tasks", async (
            Guid organizationId, TaskParentType parentType, Guid parentId, WorkTaskStatus? status, int? page, int? pageSize,
            ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ListTasksQuery(
                    organizationId, parentType, parentId, status, page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize),
                ct);
            return Results.Ok(result);
        });

        group.MapPost("/tasks", async (
            Guid organizationId, CreateTaskRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new CreateTaskCommand(
                    organizationId, request.ParentType, request.ParentId, request.Title, request.Description,
                    request.AssignedToUserId, request.DueDate, request.TaskTypeId, request.Priority, request.IsPrivate),
                ct);
            return Results.Created($"/api/organizations/{organizationId}/tasks/{result.Id}", result);
        });

        group.MapPut("/tasks/{id:guid}", async (
            Guid organizationId, Guid id, UpdateTaskRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new UpdateTaskCommand(
                    organizationId, id, request.Title, request.Description, request.AssignedToUserId, request.DueDate,
                    request.TaskTypeId, request.Priority, request.IsPrivate),
                ct);
            return Results.Ok(result);
        });

        group.MapPut("/tasks/{id:guid}/status", async (
            Guid organizationId, Guid id, UpdateTaskStatusRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new UpdateTaskStatusCommand(organizationId, id, request.NewStatus), ct);
            return Results.Ok(result);
        });
    }

    private sealed record CreateTaskRequest(
        TaskParentType ParentType,
        Guid ParentId,
        string Title,
        string? Description,
        Guid? AssignedToUserId,
        DateOnly? DueDate,
        Guid TaskTypeId,
        TaskPriority Priority,
        bool IsPrivate);

    private sealed record UpdateTaskRequest(
        string Title,
        string? Description,
        Guid? AssignedToUserId,
        DateOnly? DueDate,
        Guid TaskTypeId,
        TaskPriority Priority,
        bool IsPrivate);

    private sealed record UpdateTaskStatusRequest(WorkTaskStatus NewStatus);
}
