using ErpApp.Application.Workflow.Queries.TransactionApproval;
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
    }
}
