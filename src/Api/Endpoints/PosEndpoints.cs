using ErpApp.Application.Pos.Commands.ClosePosSession;
using ErpApp.Application.Pos.Commands.CreatePosRefund;
using ErpApp.Application.Pos.Commands.CreatePosSale;
using ErpApp.Application.Pos.Commands.OpenPosSession;
using ErpApp.Application.Pos.Commands.PrintPosReceipt;
using ErpApp.Application.Pos.Commands.PrintPosRefundReceipt;
using ErpApp.Application.Pos.Commands.RecordPosCashMovement;
using ErpApp.Application.Pos.Commands.SetLocationPosMode;
using ErpApp.Application.Pos.Commands.SetPosLocationPaymentModes;
using ErpApp.Application.Pos.Commands.UpdatePosLocationSettings;
using ErpApp.Application.Pos.Queries.FindPosSales;
using ErpApp.Application.Pos.Queries.GetMyOpenPosSession;
using ErpApp.Application.Pos.Queries.GetPosConfiguration;
using ErpApp.Application.Pos.Queries.GetPosLocationSettings;
using ErpApp.Application.Pos.Queries.GetPosSession;
using ErpApp.Application.Pos.Queries.GetPosRefundableSale;
using ErpApp.Application.Pos.Queries.GetPosTill;
using ErpApp.Application.Pos.Queries.ListPosProducts;
using ErpApp.Application.Pos.Queries.ListPosSessionRefunds;
using ErpApp.Application.Pos.Queries.ListPosSessionSales;
using ErpApp.Application.Pos.Queries.PreviewPosRefund;
using ErpApp.Application.Pos.Queries.ListPosTills;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Api.Endpoints;

/// <summary>
/// Phase 60 -- the ERP side of point-of-sale configuration (Configurations &gt; Point of Sale). The
/// till itself (phases 61-62) will live under the same <c>/pos</c> prefix; like the vendor's
/// <c>/pos/*</c> resources, these are the POS-only ones, and everything a till shares with the ERP
/// (payment modes, products, contacts) stays on its ERP route.
/// </summary>
public static class PosEndpoints
{
    public static void MapPosEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/organizations/{organizationId:guid}/pos")
            .WithTags("Point of sale")
            .RequireAuthorization();

        group.MapGet("/configuration", async (Guid organizationId, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new GetPosConfigurationQuery(organizationId), ct)));

        group.MapGet("/locations/{locationId:guid}/settings", async (
            Guid organizationId, Guid locationId, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new GetPosLocationSettingsQuery(organizationId, locationId), ct)));

        group.MapPut("/locations/{locationId:guid}/settings", async (
            Guid organizationId, Guid locationId, UpdatePosLocationSettingsRequest request, ISender sender,
            CancellationToken ct) =>
            Results.Ok(await sender.Send(
                new UpdatePosLocationSettingsCommand(
                    organizationId,
                    locationId,
                    request.ServiceChargeEnabled,
                    request.ServiceChargeRate,
                    request.ServiceChargeAccountId,
                    request.RoundOffEnabled,
                    request.RoundOffAccountId,
                    request.CashVerificationRequired,
                    request.Denominations ?? [],
                    request.DefaultTab,
                    request.PrintEstimateBill,
                    request.PrintInvoice,
                    request.PrintCreditNote,
                    request.PrintKot,
                    request.AbbreviatedTaxInvoiceEnabled),
                ct)));

        group.MapPut("/locations/{locationId:guid}/mode", async (
            Guid organizationId, Guid locationId, SetLocationPosModeRequest request, ISender sender,
            CancellationToken ct) =>
            Results.Ok(await sender.Send(new SetLocationPosModeCommand(organizationId, locationId, request.PosMode), ct)));

        // A PUT with the list in the body, on purpose: phase 38's array-binding gotcha is about a
        // POST whose array was meant to come from the query string. Here the body is the resource.
        group.MapPut("/locations/{locationId:guid}/payment-modes", async (
            Guid organizationId, Guid locationId, SetPosLocationPaymentModesRequest request, ISender sender,
            CancellationToken ct) =>
            Results.Ok(await sender.Send(
                new SetPosLocationPaymentModesCommand(organizationId, locationId, request.PaymentModeIds ?? []), ct)));

        // ---- Phase 61: sessions, the drawer, and the sale -------------------------------------

        // The till's first question: is there a session of mine open here? 204 when there is not.
        group.MapGet("/locations/{locationId:guid}/sessions/mine", async (
            Guid organizationId, Guid locationId, ISender sender, CancellationToken ct) =>
            await sender.Send(new GetMyOpenPosSessionQuery(organizationId, locationId), ct) is { } session
                ? Results.Ok(session)
                : Results.NoContent());

        group.MapPost("/sessions", async (
            Guid organizationId, OpenPosSessionRequest request, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(
                new OpenPosSessionCommand(organizationId, request.LocationId, request.OpeningAmount, request.Denominations),
                ct)));

        group.MapGet("/sessions/{sessionId:guid}", async (
            Guid organizationId, Guid sessionId, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new GetPosSessionQuery(organizationId, sessionId), ct)));

        group.MapPost("/sessions/{sessionId:guid}/cash-movements", async (
            Guid organizationId, Guid sessionId, RecordPosCashMovementRequest request, ISender sender,
            CancellationToken ct) =>
            Results.Ok(await sender.Send(
                new RecordPosCashMovementCommand(
                    organizationId, sessionId, request.Direction, request.Amount, request.AccountId, request.Note,
                    request.OverrideNegativeCashBalanceWarning),
                ct)));

        group.MapPost("/sessions/{sessionId:guid}/close", async (
            Guid organizationId, Guid sessionId, ClosePosSessionRequest request, ISender sender,
            CancellationToken ct) =>
            Results.Ok(await sender.Send(
                new ClosePosSessionCommand(
                    organizationId, sessionId, request.CountedAmount, request.Denominations, request.Note),
                ct)));

        group.MapPost("/sales", async (
            Guid organizationId, CreatePosSaleRequest request, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(
                new CreatePosSaleCommand(
                    organizationId,
                    request.SessionId,
                    request.LocationId,
                    request.Lines ?? [],
                    request.Tenders ?? [],
                    request.ChangeAmount,
                    request.ContactId,
                    request.WarehouseId,
                    request.OrderType,
                    request.DiscountPct,
                    request.OverrideStockWarning,
                    request.OverrideCreditLimitWarning),
                ct)));

        // Phase 66 -- phase 61's /day-summary became the Day Report over a period (PosReportEndpoints).

        // ---- Phase 62: what the till screen reads, and its receipt -------------------------------

        // The launcher: the tills the caller may open a drawer at, with their own open session.
        group.MapGet("/tills", async (Guid organizationId, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new ListPosTillsQuery(organizationId), ct)));

        // The till's own read of its location -- the settings it acts on, under the cashier's key
        // rather than the Admin's configuration key (phase-62-status.md Decision D).
        group.MapGet("/tills/{locationId:guid}", async (
            Guid organizationId, Guid locationId, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new GetPosTillQuery(organizationId, locationId), ct)));

        group.MapGet("/tills/{locationId:guid}/products", async (
            Guid organizationId, Guid locationId, string? search, string? code, Guid? categoryId, int? page,
            int? pageSize, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(
                new ListPosProductsQuery(
                    organizationId, locationId, search, code, categoryId, page ?? 1,
                    pageSize ?? ListPosProductsQuery.DefaultPageSize),
                ct)));

        group.MapGet("/sessions/{sessionId:guid}/sales", async (
            Guid organizationId, Guid sessionId, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new ListPosSessionSalesQuery(organizationId, sessionId), ct)));

        // A POST because every call is a printing the server counts (Decision B): the first answers
        // print number 1, the original; every later one is a marked copy.
        group.MapPost("/sales/{invoiceId:guid}/prints", async (
            Guid organizationId, Guid invoiceId, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new PrintPosReceiptCommand(organizationId, invoiceId), ct)));

        // ---- Phase 63: returns at the till ------------------------------------------------------

        // Find a sale to refund by its number, at one till's location.
        group.MapGet("/tills/{locationId:guid}/sales", async (
            Guid organizationId, Guid locationId, string? search, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new FindPosSalesQuery(organizationId, locationId, search), ct)));

        // A sale as the refund screen needs it: lines with what is left to refund, earlier refunds.
        group.MapGet("/sales/{invoiceId:guid}/refundable", async (
            Guid organizationId, Guid invoiceId, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new GetPosRefundableSaleQuery(organizationId, invoiceId), ct)));

        // The refund's figure before it is made: a query with a body, computed by the refund's own planner.
        group.MapPost("/refunds/preview", async (
            Guid organizationId, PreviewPosRefundRequest request, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(
                new PreviewPosRefundQuery(organizationId, request.SessionId, request.InvoiceId, request.Lines ?? []), ct)));

        group.MapPost("/refunds", async (
            Guid organizationId, CreatePosRefundRequest request, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(
                new CreatePosRefundCommand(
                    organizationId,
                    request.SessionId,
                    request.LocationId,
                    request.InvoiceId,
                    request.Lines ?? [],
                    request.Payouts ?? [],
                    request.Reason ?? ""),
                ct)));

        group.MapGet("/sessions/{sessionId:guid}/refunds", async (
            Guid organizationId, Guid sessionId, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new ListPosSessionRefundsQuery(organizationId, sessionId), ct)));

        // A POST for the sale receipt's reason: every call is a printing the server counts.
        group.MapPost("/refunds/{creditNoteId:guid}/prints", async (
            Guid organizationId, Guid creditNoteId, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new PrintPosRefundReceiptCommand(organizationId, creditNoteId), ct)));
    }

    private sealed record UpdatePosLocationSettingsRequest(
        bool ServiceChargeEnabled,
        decimal ServiceChargeRate,
        Guid? ServiceChargeAccountId,
        bool RoundOffEnabled,
        Guid? RoundOffAccountId,
        bool CashVerificationRequired,
        IReadOnlyList<int>? Denominations,
        PosTab? DefaultTab,
        bool PrintEstimateBill,
        bool PrintInvoice,
        bool PrintCreditNote,
        bool PrintKot,
        bool AbbreviatedTaxInvoiceEnabled);

    private sealed record SetLocationPosModeRequest(PosMode PosMode);

    private sealed record SetPosLocationPaymentModesRequest(IReadOnlyList<Guid>? PaymentModeIds);

    // Phase 61 -- every field each command takes, so none binds to its default in silence (phase 27b).
    // None carries a date: a till acts now, and the commands stamp the Nepal date themselves.
    private sealed record OpenPosSessionRequest(
        Guid LocationId, decimal? OpeningAmount, IReadOnlyList<DenominationCount>? Denominations);

    private sealed record RecordPosCashMovementRequest(
        PosCashMovementDirection Direction,
        decimal Amount,
        Guid AccountId,
        string? Note,
        bool OverrideNegativeCashBalanceWarning);

    private sealed record ClosePosSessionRequest(
        decimal? CountedAmount, IReadOnlyList<DenominationCount>? Denominations, string? Note);

    private sealed record CreatePosSaleRequest(
        Guid SessionId,
        Guid? LocationId,
        IReadOnlyList<PosSaleLineInput>? Lines,
        IReadOnlyList<PosTenderInput>? Tenders,
        decimal ChangeAmount,
        Guid? ContactId,
        Guid? WarehouseId,
        PosTab? OrderType,
        decimal DiscountPct,
        bool OverrideStockWarning,
        bool OverrideCreditLimitWarning);

    // Phase 63 -- every field each request takes (phase 27b). No date: the refund stamps the Nepal date.
    private sealed record PreviewPosRefundRequest(
        Guid SessionId, Guid InvoiceId, IReadOnlyList<PosRefundLineInput>? Lines);

    private sealed record CreatePosRefundRequest(
        Guid SessionId,
        Guid? LocationId,
        Guid InvoiceId,
        IReadOnlyList<PosRefundLineInput>? Lines,
        IReadOnlyList<PosTenderInput>? Payouts,
        string? Reason);
}
