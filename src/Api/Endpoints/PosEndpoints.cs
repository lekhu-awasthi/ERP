using ErpApp.Application.Pos.Commands.ClosePosSession;
using ErpApp.Application.Pos.Commands.CreatePosSale;
using ErpApp.Application.Pos.Commands.OpenPosSession;
using ErpApp.Application.Pos.Commands.RecordPosCashMovement;
using ErpApp.Application.Pos.Commands.SetLocationPosMode;
using ErpApp.Application.Pos.Commands.SetPosLocationPaymentModes;
using ErpApp.Application.Pos.Commands.UpdatePosLocationSettings;
using ErpApp.Application.Pos.Queries.GetMyOpenPosSession;
using ErpApp.Application.Pos.Queries.GetPosConfiguration;
using ErpApp.Application.Pos.Queries.GetPosDaySummary;
using ErpApp.Application.Pos.Queries.GetPosLocationSettings;
using ErpApp.Application.Pos.Queries.GetPosSession;
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
                    request.PrintKot),
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

        group.MapGet("/day-summary", async (
            Guid organizationId, DateOnly date, Guid? locationId, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new GetPosDaySummaryQuery(organizationId, date, locationId), ct)));
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
        bool PrintKot);

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
}
