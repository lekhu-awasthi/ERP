using ErpApp.Application.Payments;
using ErpApp.Application.Payments.Commands.ApplyPaymentAllocation;
using ErpApp.Application.Payments.Commands.ApprovePayment;
using ErpApp.Application.Payments.Commands.CreatePayment;
using ErpApp.Application.Payments.Commands.TransitionChequeStatus;
using ErpApp.Application.Payments.Commands.UpdatePayment;
using ErpApp.Application.Payments.Commands.VoidPayment;
using ErpApp.Application.Payments.Queries.ChequeDashboard;
using ErpApp.Application.Payments.Queries.GetDefaultPaymentAllocations;
using ErpApp.Application.Payments.Queries.GetPayment;
using ErpApp.Application.Payments.Queries.ListAllocatablePayments;
using ErpApp.Application.Payments.Queries.ListCheques;
using ErpApp.Application.Payments.Queries.ListPayments;
using ErpApp.Application.Payments.Queries.PreviewPaymentGlPosting;
using ErpApp.Application.Common.Pagination;
using ErpApp.Domain.Common;
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

        MapChequeEndpoints(group);

        group.MapGet("/payments", async (
            Guid organizationId, PaymentStatus? status, PaymentDirection? direction, int? page, int? pageSize,
            string? search, DateOnly? fromDate, DateOnly? toDate, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ListPaymentsQuery(
                    organizationId, status, direction, page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize, search, fromDate, toDate),
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
                    request.Amount, request.Reference, request.Allocations, request.ChequeDetails) { CurrencyCode = request.CurrencyCode, ExchangeRate = request.ExchangeRate, LocationId = request.LocationId },
                ct);
            return Results.Created($"/api/organizations/{organizationId}/payments/{result.Id}", result);
        });

        group.MapPut("/payments/{id:guid}", async (
            Guid organizationId, Guid id, PaymentRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new UpdatePaymentCommand(
                    organizationId, id, request.ContactId, request.Date, request.PaymentModeId, request.AccountId,
                    request.Amount, request.Reference, request.Allocations, request.ChequeDetails) { CurrencyCode = request.CurrencyCode, ExchangeRate = request.ExchangeRate, LocationId = request.LocationId },
                ct);
            return Results.Ok(result);
        });

        // Phase 31 -- the Negative Cash Balance warning is confirmable, so the approve endpoint now
        // takes an optional body carrying the acknowledgement. Nullable request so every existing
        // caller that posts no body at all keeps working.
        group.MapPost("/payments/{id:guid}/approve", async (
            Guid organizationId, Guid id, ApproveWithCashOverrideRequest? request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ApprovePaymentCommand(
                    organizationId, id, request?.OverrideNegativeCashBalanceWarning ?? false),
                ct);
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

        group.MapGet("/payments/allocatable", async (
            Guid organizationId, PaymentDirection direction, bool? showAllocated, Guid? contactId, int? page, int? pageSize,
            ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ListAllocatablePaymentsQuery(
                    organizationId, direction, showAllocated ?? false, contactId,
                    page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize),
                ct);
            return Results.Ok(result);
        });

        // Decision #2 (docs/phase-17-status.md) -- not nested under /payments/{id} anymore since
        // the source being applied can now be a Payment or a JournalVoucher line; SourceType/
        // SourceId carry that in the request body instead of the route.
        group.MapPost("/payment-allocations/apply", async (
            Guid organizationId, ApplyPaymentAllocationRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ApplyPaymentAllocationCommand(
                    organizationId, request.SourceType, request.SourceId, request.ParentDocumentId,
                    request.TargetDocumentType, request.TargetDocumentId, request.Amount),
                ct);
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

    private static void MapChequeEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/cheques", async (
            Guid organizationId, PaymentDirection? direction, ChequeStatus? status, Guid? contactId,
            DateOnly? fromDate, DateOnly? toDate, int? page, int? pageSize, string? search,
            ISender sender, CancellationToken ct) =>
        {
            // Phase 34b: the Cheque Register already had its own fromDate/toDate, which is why its
            // query satisfies IDateRangeFilteredQuery with no new parameters -- the shell's global
            // range now drives the same two.
            var result = await sender.Send(
                new ListChequesQuery(
                    organizationId, direction, status, contactId, fromDate, toDate,
                    page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize, search),
                ct);
            return Results.Ok(result);
        });

        group.MapGet("/cheques/dashboard-summary", async (
            Guid organizationId, DateOnly? fromDate, DateOnly? toDate, Guid? contactId, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ChequeDashboardSummaryQuery(organizationId, fromDate, toDate, contactId), ct);
            return Results.Ok(result);
        });

        group.MapPost("/cheques/{id:guid}/transition", async (
            Guid organizationId, Guid id, TransitionChequeStatusRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new TransitionChequeStatusCommand(organizationId, id, request.NewStatus), ct);
            return Results.Ok(result);
        });
    }

    // Phase 31 -- the Warn-and-allow acknowledgement for TenantSettings.NegativeCashBalanceAction.
    // Carried on the request record, not only on the command (phase-27b's Terms gotcha).
    private sealed record ApproveWithCashOverrideRequest(bool OverrideNegativeCashBalanceWarning = false);

    private sealed record PaymentRequest(
        Guid ContactId, PaymentDirection Direction, DateOnly Date, Guid? PaymentModeId, Guid AccountId, decimal Amount,
        string? Reference, IReadOnlyList<PaymentAllocationInput> Allocations, ChequeDetailsInput? ChequeDetails = null,
        // Phase 28 (FR-2.5) -- the Currency + "Exchange Rate To NPR" pair. Optional and trailing so
        // every existing caller is unchanged; null/null means the base currency at rate 1. These must
        // be carried on the request record itself, not only on the command: a trailing optional
        // parameter added to a command alone binds to null forever and every test still passes
        // (phase-27b's Terms).
        string? CurrencyCode = null, decimal? ExchangeRate = null,
        // Phase 32 (FR-2.3/FR-3.3) -- the billing location the document is raised from. Same shape and
        // the same reason as the currency pair above: optional, trailing, and carried on the request
        // record itself rather than only on the command.
        Guid? LocationId = null);

    private sealed record PreviewPaymentGlPostingRequest(Guid AccountId, decimal Amount, PaymentDirection Direction);

    private sealed record TransitionChequeStatusRequest(ChequeStatus NewStatus);

    private sealed record ApplyPaymentAllocationRequest(
        DocumentType SourceType, Guid SourceId, Guid? ParentDocumentId, DocumentType TargetDocumentType, Guid TargetDocumentId, decimal Amount);
}
