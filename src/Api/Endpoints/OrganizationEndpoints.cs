using ErpApp.Application.Tenancy.Commands.AcceptInvitation;
using ErpApp.Application.Tenancy.Commands.AcceptRequest;
using ErpApp.Application.Tenancy.Commands.CreateOrganization;
using ErpApp.Application.Tenancy.Commands.InviteUser;
using ErpApp.Application.Tenancy.Queries.CheckWorkspaceNameAvailability;
using ErpApp.Application.Tenancy.Queries.MyOrganizations;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Api.Endpoints;

public static class OrganizationEndpoints
{
    public static void MapOrganizationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/organizations").WithTags("Organizations").RequireAuthorization();

        group.MapGet("/workspace-name-availability", async (string workspaceName, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new CheckWorkspaceNameAvailabilityQuery(workspaceName), ct);
            return Results.Ok(result);
        });

        group.MapGet("/mine", async (ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new MyOrganizationsQuery(), ct);
            return Results.Ok(result);
        });

        group.MapPost("/", async (CreateOrganizationRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new CreateOrganizationCommand(
                    request.Name,
                    request.Industry,
                    request.Address,
                    request.AccountingStartDate,
                    request.IsVatRegistered,
                    request.WorkspaceName,
                    request.Email,
                    request.Phone,
                    request.PanNumber,
                    request.Website,
                    request.TrackInventory,
                    request.MultipleLocations,
                    request.MultipleWarehouses,
                    request.MultiCurrency,
                    request.Manufacturing,
                    request.PosRetail,
                    request.PosRestaurant),
                ct);
            return Results.Created($"/api/organizations/{result.OrganizationId}", result);
        });

        group.MapPost("/{organizationId:guid}/invitations", async (
            Guid organizationId, InviteUserRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new InviteUserCommand(organizationId, request.Email, request.Role), ct);
            return Results.Ok(result);
        });

        group.MapPost("/memberships/{membershipId:guid}/accept-invitation", async (
            Guid membershipId, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new AcceptInvitationCommand(membershipId), ct);
            return Results.Ok();
        });

        group.MapPost("/memberships/{membershipId:guid}/accept-request", async (
            Guid membershipId, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new AcceptRequestCommand(membershipId), ct);
            return Results.Ok();
        });
    }

    private sealed record CreateOrganizationRequest(
        string Name,
        string Industry,
        string? Address,
        DateOnly AccountingStartDate,
        bool IsVatRegistered,
        string WorkspaceName,
        string? Email,
        string? Phone,
        string? PanNumber,
        string? Website,
        bool TrackInventory,
        bool MultipleLocations,
        bool MultipleWarehouses,
        bool MultiCurrency,
        bool Manufacturing,
        bool PosRetail,
        bool PosRestaurant);

    private sealed record InviteUserRequest(string Email, MembershipRole Role);
}
