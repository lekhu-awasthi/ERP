using ErpApp.Application.Payments;
using ErpApp.Application.Payments.Commands.ApprovePayment;
using ErpApp.Application.Payments.Commands.CreatePayment;
using ErpApp.Application.Payments.Commands.UpdatePayment;
using ErpApp.Application.Payments.Commands.VoidPayment;
using ErpApp.Application.Payments.Queries.GetDefaultPaymentAllocations;
using ErpApp.Application.Payments.Queries.GetPayment;
using ErpApp.Application.Payments.Queries.ListPayments;
using ErpApp.Application.Payments.Queries.PreviewPaymentGlPosting;
using ErpApp.Application.Common.Pagination;
using ErpApp.Domain.Payments;
using MediatR;

namespace ErpApp.Api.Endpoints;

public static class PaymentsEndpoints
{
    public static void MapPaymentsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/organizations/{organizationId:guid}")
            .WithTags("Payments")
            .RequireAuthorization();

        group.MapGet("/payments", async (
            Guid organizationId, PaymentStatus? status, PaymentDirection? direction, int? page, int? pageSize,
            ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ListPaymentsQuery(
                    organizationId, status, direction, page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize),
                ct);
            return Results.Ok(result);
        });

        group.MapGet("/payments/{id:guid}", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetPaymentQuery(organizationId, id), ct);
            return Results.Ok(result);
        });

        group.MapPost("/payments", async (
            Guid organizationId, PaymentRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new CreatePaymentCommand(
                    organizationId, request.ContactId, request.Direction, request.Date, request.PaymentModeId, request.AccountId,
                    request.Amount, request.Reference, request.Allocations),
                ct);
            return Results.Created($"/api/organizations/{organizationId}/payments/{result.Id}", result);
        });

        group.MapPut("/payments/{id:guid}", async (
            Guid organizationId, Guid id, PaymentRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new UpdatePaymentCommand(
                    organizationId, id, request.ContactId, request.Date, request.PaymentModeId, request.AccountId,
                    request.Amount, request.Reference, request.Allocations),
                ct);
            return Results.Ok(result);
        });

        group.MapPost("/payments/{id:guid}/approve", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ApprovePaymentCommand(organizationId, id), ct);
            return Results.Ok(result);
        });

        group.MapPost("/payments/{id:guid}/void", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new VoidPaymentCommand(organizationId, id), ct);
            return Results.Ok(result);
        });

        group.MapGet("/payments/default-allocations", async (
            Guid organizationId, Guid contactId, decimal amount, PaymentDirection direction, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetDefaultPaymentAllocationsQuery(organizationId, contactId, amount, direction), ct);
            return Results.Ok(result);
        });

        group.MapPost("/payments/preview-gl-posting", async (
            Guid organizationId, PreviewPaymentGlPostingRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new PreviewPaymentGlPostingQuery(organizationId, request.AccountId, request.Amount, request.Direction), ct);
            return Results.Ok(result);
        });
    }

    private sealed record PaymentRequest(
        Guid ContactId, PaymentDirection Direction, DateOnly Date, Guid? PaymentModeId, Guid AccountId, decimal Amount,
        string? Reference, IReadOnlyList<PaymentAllocationInput> Allocations);

    private sealed record PreviewPaymentGlPostingRequest(Guid AccountId, decimal Amount, PaymentDirection Direction);
}
