using MediatR;

namespace ErpApp.Application.Tenancy.Commands.CreateOrganization;

/// <summary>
/// Backs the entire 3-step New Organization wizard as one command (roadmap Phase 1b task 7,
/// architecture-spec.md §4.1) -- the wizard is client-side pagination over this single input,
/// not three separate commands per step.
/// </summary>
public sealed record CreateOrganizationCommand(
    // Step 1 -- Set Up Your Organization
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
    // Step 2 -- Accounting Features
    bool TrackInventory,
    bool MultipleLocations,
    bool MultipleWarehouses,
    bool MultiCurrency,
    bool Manufacturing,
    bool PosRetail,
    bool PosRestaurant) : IRequest<CreateOrganizationResult>;

public sealed record CreateOrganizationResult(Guid OrganizationId, string Name, string WorkspaceName);
