using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Configuration.Commands.DeleteLookup;
using ErpApp.Application.Configuration.Queries.ListLookups;
using ErpApp.Application.Tenancy.Commands.AcceptInvitation;
using ErpApp.Application.Tenancy.Commands.AcceptRequest;
using ErpApp.Application.Tenancy.Commands.CreateOrganization;
using ErpApp.Application.Tenancy.Commands.CreateRole;
using ErpApp.Application.Tenancy.Commands.CreateBillingLocation;
using ErpApp.Application.Tenancy.Commands.CreateCurrency;
using ErpApp.Application.Tenancy.Commands.CreateWarehouse;
using ErpApp.Application.Tenancy.Commands.DeleteRole;
using ErpApp.Application.Tenancy.Commands.InviteUser;
using ErpApp.Application.Tenancy.Commands.SetOrganizationLockDate;
using ErpApp.Application.Tenancy.Commands.SetTenantSubscription;
using ErpApp.Application.Tenancy.Commands.UpdateAccountingDefaults;
using ErpApp.Application.Tenancy.Commands.UpdateGeneralSettings;
using ErpApp.Application.Tenancy.Commands.UpdateMembershipRole;
using ErpApp.Application.Tenancy.Commands.UpdateRole;
using ErpApp.Application.Tenancy.Commands.UpdateRolePermissions;
using ErpApp.Application.Tenancy.Commands.UpdateBillingLocation;
using ErpApp.Application.Tenancy.Commands.UpdateBillingLocationSettings;
using ErpApp.Application.Tenancy.Commands.UpdateCurrency;
using ErpApp.Application.Tenancy.Commands.UpdateWarehouse;
using ErpApp.Application.Tenancy.Queries.CheckWorkspaceNameAvailability;
using ErpApp.Application.Tenancy.Queries.GetAccountingDefaults;
using ErpApp.Application.Tenancy.Queries.GetBillingLocationSettings;
using ErpApp.Application.Tenancy.Queries.ListBillingLocations;
using ErpApp.Application.Tenancy.Queries.GetGeneralSettings;
using ErpApp.Application.Tenancy.Queries.ListCurrencyCatalog;
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
                    request.PosRestaurant,
                    request.TurnstileToken),
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

        // Phase 28 (FR-2.5) -- the tenant's active-currency list, rendered on the Organization's own
        // Features tab in the reference product rather than under Configurations (confirmed live
        // 2026-09-04), which is why it sits on this endpoint group beside warehouses rather than in
        // ConfigurationEndpoints. List/Delete ride the generic lookup pair; Create and Update are
        // concrete because each carries a rule the generic pair cannot express.
        group.MapGet("/{organizationId:guid}/currencies", async (
            Guid organizationId, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ListLookupsQuery<Currency>(organizationId, page ?? 1, pageSize ?? PagingDefaults.MaxPageSize), ct);
            return Results.Ok(result);
        });

        // The "Select Currency" picker behind Add New Currency: the standard catalog, flagged with
        // what this tenant has already activated.
        group.MapGet("/{organizationId:guid}/currencies/catalog", async (
            Guid organizationId, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ListCurrencyCatalogQuery(organizationId), ct);
            return Results.Ok(result);
        });

        group.MapPost("/{organizationId:guid}/currencies", async (
            Guid organizationId, CreateCurrencyRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new CreateCurrencyCommand(organizationId, request.Code, request.Name, request.Symbol), ct);
            return Results.Created($"/api/organizations/{organizationId}/currencies/{result.Id}", result);
        });

        group.MapPut("/{organizationId:guid}/currencies/{id:guid}", async (
            Guid organizationId, Guid id, UpdateCurrencyRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new UpdateCurrencyCommand(organizationId, id, request.Name, request.Symbol, request.IsActive), ct);
            return Results.Ok(result);
        });

        group.MapDelete("/{organizationId:guid}/currencies/{id:guid}", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DeleteLookupCommand<Currency>(organizationId, id), ct);
            return Results.NoContent();
        });

        // Phase 32 (FR-2.3/FR-3.3) -- the tenant's billing locations, on this endpoint group for the
        // same reason currencies and warehouses are: the reference product renders all three on the
        // Organization's own Features tab, not under Configurations (confirmed live 2026-09-07 on a
        // location-enabled tenant). No Delete: the live list deactivates instead, and a location a
        // document points at must never disappear -- see BillingLocation.Update's HeadOffice guard.
        group.MapGet("/{organizationId:guid}/billing-locations", async (
            Guid organizationId, bool? includeInactive, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ListBillingLocationsQuery(organizationId, includeInactive ?? false), ct);
            return Results.Ok(result);
        });

        group.MapPost("/{organizationId:guid}/billing-locations", async (
            Guid organizationId, CreateBillingLocationRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new CreateBillingLocationCommand(
                    organizationId, request.Code, request.Name, request.Address, request.WarehouseId),
                ct);
            return Results.Created($"/api/organizations/{organizationId}/billing-locations/{result.Id}", result);
        });

        group.MapPut("/{organizationId:guid}/billing-locations/{id:guid}", async (
            Guid organizationId, Guid id, UpdateBillingLocationRequest request, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(
                new UpdateBillingLocationCommand(
                    organizationId, id, request.Code, request.Name, request.Address, request.WarehouseId,
                    request.IsActive),
                ct);
            return Results.NoContent();
        });

        // The Advanced panel inside the Billing Location card. Read on BillingLocationView rather
        // than the Manage key, because every document form asks it whether to render a location
        // picker -- see GetBillingLocationSettingsQuery.
        group.MapGet("/{organizationId:guid}/billing-location-settings", async (
            Guid organizationId, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetBillingLocationSettingsQuery(organizationId), ct);
            return Results.Ok(result);
        });

        group.MapPut("/{organizationId:guid}/billing-location-settings", async (
            Guid organizationId, UpdateBillingLocationSettingsRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new UpdateBillingLocationSettingsCommand(
                    organizationId, request.LocationScopeMode, request.LocationWiseReportPermission),
                ct);
            return Results.Ok(result);
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
                    request.DefaultProductionCostAccountId,
                    request.DefaultForexGainAccountId,
                    request.DefaultForexLossAccountId,
                    request.DefaultLandedCostClearingAccountId),
                ct);
            return Results.Ok(result);
        });

        // Phase 31 -- Configurations > General. The five behaviour switches on TenantSettings, four
        // of which had been schema'd since phase 2 with no way to read or write them at all.
        group.MapGet("/{organizationId:guid}/general-settings", async (
            Guid organizationId, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetGeneralSettingsQuery(organizationId), ct);
            return Results.Ok(result);
        });

        group.MapPut("/{organizationId:guid}/general-settings", async (
            Guid organizationId, UpdateGeneralSettingsRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new UpdateGeneralSettingsCommand(
                    organizationId,
                    request.SuggestSellingPriceMode,
                    request.ProductPriceBasis,
                    request.InventoryTrackingMode,
                    request.NegativeCashBalanceAction,
                    request.NegativeStockBalanceAction,
                    request.CreditLimitExceedsAction),
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

        // Phase 31 -- the renewal counterpart 20f left out, so an expired organization is not
        // permanently read-only from inside the product. The entitlement flags stay immutable and
        // are not on this request at all; see TenantSubscription.Renew.
        group.MapPut("/{organizationId:guid}/subscription", async (
            Guid organizationId, SetTenantSubscriptionRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new SetTenantSubscriptionCommand(organizationId, request.PlanName, request.EndsAt), ct);
            return Results.Ok(result);
        });
    }

    private sealed record SetTenantSubscriptionRequest(string PlanName, DateTimeOffset EndsAt);

    private sealed record UpdateGeneralSettingsRequest(
        SuggestSellingPriceMode SuggestSellingPriceMode,
        ProductPriceBasis ProductPriceBasis,
        InventoryTrackingMode InventoryTrackingMode,
        BalanceAction NegativeCashBalanceAction,
        BalanceAction NegativeStockBalanceAction,
        BalanceAction CreditLimitExceedsAction);

    private sealed record CreateWarehouseRequest(string Name);

    private sealed record UpdateWarehouseRequest(string Name, bool IsActive);

    private sealed record CreateCurrencyRequest(string Code, string? Name = null, string? Symbol = null);

    private sealed record UpdateCurrencyRequest(string Name, string Symbol, bool IsActive);

    // Phase 32 -- the live "Add New Location" dialog is Location Code*, Location Name*, Address* and
    // Warehouse*. WarehouseId is optional here although the dialog marks it required: nothing seeds a
    // Warehouse for a tenant, so demanding one would make the second location unreachable for a
    // tenant that has never created one (the phase-20f failure mode). See the command's remarks.
    private sealed record CreateBillingLocationRequest(
        string Code, string Name, string? Address = null, Guid? WarehouseId = null);

    private sealed record UpdateBillingLocationRequest(
        string Code, string Name, string? Address, Guid? WarehouseId, bool IsActive);

    private sealed record UpdateBillingLocationSettingsRequest(
        LocationScopeMode LocationScopeMode, bool LocationWiseReportPermission);

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
        Guid? DefaultProductionCostAccountId,
        // Phase 28 (FR-2.5) -- the two realised-forex accounts. Optional and trailing, so a client
        // that predates this phase keeps working; but they must exist HERE and not only on the
        // command, or they bind to null forever (phase-27b's Terms).
        Guid? DefaultForexGainAccountId = null,
        Guid? DefaultForexLossAccountId = null,
        // Phase 29 (FR-6.15) -- the landed-cost clearing account, same trailing-optional treatment
        // and the same phase-27b warning applies.
        Guid? DefaultLandedCostClearingAccountId = null);

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
        bool PosRestaurant,
        string? TurnstileToken);

    private sealed record InviteUserRequest(string Email, Guid RoleId);

    private sealed record UpdateMembershipRoleRequest(Guid RoleId);

    private sealed record CreateRoleRequest(string Name, string? Description);

    private sealed record UpdateRoleRequest(string Name, string? Description);

    private sealed record UpdateRolePermissionsRequest(IReadOnlyDictionary<string, bool> Grants);

    private sealed record SetOrganizationLockDateRequest(DateOnly? LockDate);
}
