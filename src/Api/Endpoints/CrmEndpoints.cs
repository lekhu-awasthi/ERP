using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Crm.Commands.CreateDeal;
using ErpApp.Application.Crm.Commands.MarkDealLost;
using ErpApp.Application.Crm.Commands.MarkDealWon;
using ErpApp.Application.Crm.Commands.MoveDealToStage;
using ErpApp.Application.Crm.Commands.UpdateDeal;
using ErpApp.Application.Crm.Queries.ListDeals;
using ErpApp.Domain.Crm;
using MediatR;

namespace ErpApp.Api.Endpoints;

public static class CrmEndpoints
{
    public static void MapCrmEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/organizations/{organizationId:guid}")
            .WithTags("Crm")
            .RequireAuthorization();

        group.MapGet("/deals", async (
            Guid organizationId, Guid? contactId, DealStatus? status, int? page, int? pageSize,
            ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ListDealsQuery(organizationId, contactId, status, page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize), ct);
            return Results.Ok(result);
        });

        group.MapPost("/deals", async (
            Guid organizationId, CreateDealRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new CreateDealCommand(
                    organizationId, request.ContactId, request.Title, request.AssigneeUserIds, request.LeadSourceId,
                    request.Description, request.ExpectedRevenue, request.ExpectedClosingDate, request.IsPrivate),
                ct);
            return Results.Created($"/api/organizations/{organizationId}/deals/{result.Id}", result);
        });

        group.MapPut("/deals/{id:guid}", async (
            Guid organizationId, Guid id, UpdateDealRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new UpdateDealCommand(
                    organizationId, id, request.Title, request.AssigneeUserIds, request.LeadSourceId,
                    request.Description, request.ExpectedRevenue, request.ExpectedClosingDate, request.IsPrivate),
                ct);
            return Results.Ok(result);
        });

        group.MapPut("/deals/{id:guid}/stage", async (
            Guid organizationId, Guid id, MoveDealToStageRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new MoveDealToStageCommand(organizationId, id, request.DealStageId), ct);
            return Results.Ok(result);
        });

        group.MapPost("/deals/{id:guid}/mark-won", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new MarkDealWonCommand(organizationId, id), ct);
            return Results.Ok(result);
        });

        group.MapPost("/deals/{id:guid}/mark-lost", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new MarkDealLostCommand(organizationId, id), ct);
            return Results.Ok(result);
        });
    }

    private sealed record CreateDealRequest(
        Guid ContactId,
        string Title,
        IReadOnlyList<Guid> AssigneeUserIds,
        Guid? LeadSourceId,
        string? Description,
        decimal ExpectedRevenue,
        DateOnly? ExpectedClosingDate,
        bool IsPrivate);

    private sealed record UpdateDealRequest(
        string Title,
        IReadOnlyList<Guid> AssigneeUserIds,
        Guid? LeadSourceId,
        string? Description,
        decimal ExpectedRevenue,
        DateOnly? ExpectedClosingDate,
        bool IsPrivate);

    private sealed record MoveDealToStageRequest(Guid DealStageId);
}
