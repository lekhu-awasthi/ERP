using ErpApp.Application.Pos.Commands.SetLocationPosMode;
using ErpApp.Application.Pos.Commands.SetPosLocationPaymentModes;
using ErpApp.Application.Pos.Commands.UpdatePosLocationSettings;
using ErpApp.Application.Pos.Queries.GetPosConfiguration;
using ErpApp.Application.Pos.Queries.GetPosLocationSettings;
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
}
