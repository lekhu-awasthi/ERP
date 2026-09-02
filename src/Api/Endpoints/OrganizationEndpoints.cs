using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Configuration.Commands.DeleteLookup;
using ErpApp.Application.Configuration.Queries.ListLookups;
using ErpApp.Application.Tenancy.Commands.AcceptInvitation;
using ErpApp.Application.Tenancy.Commands.AcceptRequest;
using ErpApp.Application.Tenancy.Commands.CreateOrganization;
using ErpApp.Application.Tenancy.Commands.CreateRole;
using ErpApp.Application.Tenancy.Commands.CreateWarehouse;
using ErpApp.Application.Tenancy.Commands.DeleteRole;
using ErpApp.Application.Tenancy.Commands.InviteUser;
using ErpApp.Application.Tenancy.Commands.SetOrganizationLockDate;
using ErpApp.Application.Tenancy.Commands.UpdateAccountingDefaults;
using ErpApp.Application.Tenancy.Commands.UpdateMembershipRole;
using ErpApp.Application.Tenancy.Commands.UpdateRole;
using ErpApp.Application.Tenancy.Commands.UpdateRolePermissions;
using ErpApp.Application.Tenancy.Commands.UpdateWarehouse;
using ErpApp.Application.Tenancy.Queries.CheckWorkspaceNameAvailability;
using ErpApp.Application.Tenancy.Queries.GetAccountingDefaults;
using ErpApp.Application.Tenancy.Queries.GetOrganizationLockDate;
using ErpApp.Application.Tenancy.Queries.GetTenantSubscription;
using ErpApp.Application.Tenancy.Queries.GetRolePermissionMatrix;
using ErpApp.Application.Tenancy.Queries.ListOrganizationMembers;
using ErpApp.Application.Tenancy.Queries.ListRoles;
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
            var result = await sender.Send(new InviteUserCommand(organizationId, request.Email, request.RoleId), ct);
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

        // Phase 14 (Role Reference) -- reassigns an existing Accepted member's Role from the Users
        // tab; a member's Role was previously fixed at invite time with no way to change it after.
        group.MapPut("/{organizationId:guid}/memberships/{membershipId:guid}/role", async (
            Guid organizationId, Guid membershipId, UpdateMembershipRoleRequest request, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new UpdateMembershipRoleCommand(organizationId, membershipId, request.RoleId), ct);
            return Results.Ok();
        });

        group.MapGet("/{organizationId:guid}/roles", async (
            Guid organizationId, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ListRolesQuery(organizationId, page ?? 1, pageSize ?? PagingDefaults.MaxPageSize), ct);
            return Results.Ok(result);
        });

        group.MapPost("/{organizationId:guid}/roles", async (
            Guid organizationId, CreateRoleRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new CreateRoleCommand(organizationId, request.Name, request.Description), ct);
            return Results.Created($"/api/organizations/{organizationId}/roles/{result.Id}", result);
        });

        group.MapPut("/{organizationId:guid}/roles/{id:guid}", async (
            Guid organizationId, Guid id, UpdateRoleRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new UpdateRoleCommand(organizationId, id, request.Name, request.Description), ct);
            return Results.Ok(result);
        });

        group.MapDelete("/{organizationId:guid}/roles/{id:guid}", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DeleteRoleCommand(organizationId, id), ct);
            return Results.NoContent();
        });

        group.MapGet("/{organizationId:guid}/roles/{id:guid}/permissions", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetRolePermissionMatrixQuery(organizationId, id), ct);
            return Results.Ok(result);
        });

        group.MapPut("/{organizationId:guid}/roles/{id:guid}/permissions", async (
            Guid organizationId, Guid id, UpdateRolePermissionsRequest request, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new UpdateRolePermissionsCommand(organizationId, id, request.Grants), ct);
            return Results.Ok();
        });

        group.MapGet("/{organizationId:guid}/warehouses", async (
            Guid organizationId, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ListLookupsQuery<Warehouse>(organizationId, page ?? 1, pageSize ?? PagingDefaults.MaxPageSize), ct);
            return Results.Ok(result);
        });

        group.MapPost("/{organizationId:guid}/warehouses", async (
            Guid organizationId, CreateWarehouseRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new CreateWarehouseCommand(organizationId, request.Name), ct);
            return Results.Created($"/api/organizations/{organizationId}/warehouses/{result.Id}", result);
        });

        group.MapPut("/{organizationId:guid}/warehouses/{id:guid}", async (
            Guid organizationId, Guid id, UpdateWarehouseRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new UpdateWarehouseCommand(organizationId, id, request.Name, request.IsActive), ct);
            return Results.Ok(result);
        });

        group.MapDelete("/{organizationId:guid}/warehouses/{id:guid}", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DeleteLookupCommand<Warehouse>(organizationId, id), ct);
            return Results.NoContent();
        });

        // Phase 13 -- powers the Task feature's Assigned-To picker (see
        // ListOrganizationMembersQuery's own doc comment for why it's gated on TaskView rather
        // than a standalone "view members" key nothing else needs yet).
        group.MapGet("/{organizationId:guid}/members", async (
            Guid organizationId, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ListOrganizationMembersQuery(organizationId, page ?? 1, pageSize ?? PagingDefaults.MaxPageSize), ct);
            return Results.Ok(result);
        });

        group.MapGet("/{organizationId:guid}/accounting-defaults", async (
            Guid organizationId, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetAccountingDefaultsQuery(organizationId), ct);
            return Results.Ok(result);
        });

        group.MapPut("/{organizationId:guid}/accounting-defaults", async (
            Guid organizationId, UpdateAccountingDefaultsRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new UpdateAccountingDefaultsCommand(
                    organizationId,
                    request.DefaultSalesAccountId,
                    request.DefaultAccountsReceivableId,
                    request.DefaultVatPayableAccountId,
                    request.DefaultPurchaseAccountId,
                    request.DefaultAccountsPayableId,
                    request.DefaultVatReceivableAccountId,
                    request.DefaultTdsPayableAccountId,
                    request.DefaultInventoryAccountId,
                    request.DefaultCogsAccountId,
                    request.DefaultInventoryAdjustmentAccountId,
                    request.DefaultProductionCostAccountId),
                ct);
            return Results.Ok(result);
        });

        // Phase 16a (lock-date enforcement) -- Admin-only view/set/clear of the LockDate seam
        // schema'd since Phase 1b.
        group.MapGet("/{organizationId:guid}/lock-date", async (
            Guid organizationId, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetOrganizationLockDateQuery(organizationId), ct);
            return Results.Ok(result);
        });

        group.MapPut("/{organizationId:guid}/lock-date", async (
            Guid organizationId, SetOrganizationLockDateRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new SetOrganizationLockDateCommand(organizationId, request.LockDate), ct);
            return Results.Ok(result);
        });

        // Phase 20f (tenant feature-flag enforcement, FR-2.6) -- read-only plan + entitlement
        // state, mirroring the reference product's Tigg Subscriptions / Organization > Features
        // screens. No PUT counterpart on purpose: the flags are immutable after creation.
        group.MapGet("/{organizationId:guid}/subscription", async (
            Guid organizationId, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetTenantSubscriptionQuery(organizationId), ct);
            return Results.Ok(result);
        });
    }

    private sealed record CreateWarehouseRequest(string Name);

    private sealed record UpdateWarehouseRequest(string Name, bool IsActive);

    private sealed record UpdateAccountingDefaultsRequest(
        Guid? DefaultSalesAccountId,
        Guid? DefaultAccountsReceivableId,
        Guid? DefaultVatPayableAccountId,
        Guid? DefaultPurchaseAccountId,
        Guid? DefaultAccountsPayableId,
        Guid? DefaultVatReceivableAccountId,
        Guid? DefaultTdsPayableAccountId,
        Guid? DefaultInventoryAccountId,
        Guid? DefaultCogsAccountId,
        Guid? DefaultInventoryAdjustmentAccountId,
        Guid? DefaultProductionCostAccountId);

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

    private sealed record InviteUserRequest(string Email, Guid RoleId);

    private sealed record UpdateMembershipRoleRequest(Guid RoleId);

    private sealed record CreateRoleRequest(string Name, string? Description);

    private sealed record UpdateRoleRequest(string Name, string? Description);

    private sealed record UpdateRolePermissionsRequest(IReadOnlyDictionary<string, bool> Grants);

    private sealed record SetOrganizationLockDateRequest(DateOnly? LockDate);
}
