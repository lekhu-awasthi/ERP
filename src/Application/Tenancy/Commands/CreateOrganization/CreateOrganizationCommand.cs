using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Tenancy.Commands.CreateOrganization;

/// <summary>
/// Backs the entire 3-step New Organization wizard as one command (roadmap Phase 1b task 7,
/// architecture-spec.md §4.1) -- the wizard is client-side pagination over this single input,
/// not three separate commands per step.
///
/// <b>Phase 27b added the Turnstile check.</b> The wizard shows a Turnstile widget on step 1 and
/// again on step 3 (confirm-live), but there is exactly one server call behind all three steps --
/// this command -- so there is exactly one token to verify. Two widgets guarding one write would be
/// two chances to fail and no extra protection; the check sits on the step that actually submits.
///
/// Implements IRequirePermission but not IOrganizationScoped/ITargetsMembership -- creating an
/// Organization is, by definition, the one action that predates any membership (and thus any
/// role) in it, so PermissionKeys.OrganizationCreate is a global permission AuthorizationBehavior
/// grants to any authenticated user (see IOrganizationScoped's remarks).
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
    bool PosRestaurant,
    // Phase 27b -- the wizard's Cloudflare Turnstile check (FR-1.1, extending Phase 20g's
    // registration bot-check). Optional and trailing so the shape of the wizard's own payload is
    // unchanged for every existing caller; the handler decides whether a missing token is fatal,
    // and it is -- see CreateOrganizationCommandHandler.
    string? TurnstileToken = null) : IRequest<CreateOrganizationResult>, IRequirePermission
{
    public string PermissionKey => PermissionKeys.OrganizationCreate;
}

public sealed record CreateOrganizationResult(Guid OrganizationId, string Name, string WorkspaceName);
