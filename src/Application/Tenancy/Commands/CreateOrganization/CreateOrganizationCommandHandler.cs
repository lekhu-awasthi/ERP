using ErpApp.Application.Common.BotProtection;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Tenancy;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Tenancy.Commands.CreateOrganization;

public sealed class CreateOrganizationCommandHandler(
    IAppDbContext db, ICurrentUserService currentUser, ITurnstileVerifier turnstileVerifier)
    : IRequestHandler<CreateOrganizationCommand, CreateOrganizationResult>
{
    public async Task<CreateOrganizationResult> Handle(CreateOrganizationCommand request, CancellationToken cancellationToken)
    {
        // Phase 27b -- same shape as RegisterUserCommandHandler's check, and first, before any read:
        // a bot must not be able to probe workspace-name availability through the failure mode of
        // this command. A null token fails here exactly as a bad one does; the parameter is optional
        // only so existing callers compile, never so the check can be skipped.
        if (string.IsNullOrWhiteSpace(request.TurnstileToken)
            || !await turnstileVerifier.VerifyAsync(request.TurnstileToken, cancellationToken))
        {
            throw new TurnstileVerificationFailedException("Bot verification failed. Please try again.");
        }

        var normalizedWorkspaceName = request.WorkspaceName.Trim().ToLowerInvariant();

        var workspaceTaken = await db.Organizations
            .AnyAsync(o => o.WorkspaceName == normalizedWorkspaceName, cancellationToken);

        if (workspaceTaken)
        {
            throw new ConflictException("This workspace name is already taken.");
        }

        var organization = Organization.Create(
            request.Name,
            request.Industry,
            request.Address,
            request.AccountingStartDate,
            request.IsVatRegistered,
            normalizedWorkspaceName,
            request.Email,
            request.Phone,
            request.PanNumber,
            request.Website,
            currentUser.UserId);

        db.Organizations.Add(organization);
        db.TenantSettings.Add(TenantSettings.CreateDefault(organization.Id));

        // Phase 28 -- every tenant starts with exactly one currency, the base one. Unlike
        // Warehouse (which this handler deliberately does not seed, see phase-20f Decision #4),
        // seeding here is required rather than optional: every document defaults to
        // CurrencyCatalog.BaseCode, so a tenant with no Currency row would have a document header
        // referring to a currency its own list does not contain. Seeding it is also what makes the
        // MultiCurrency entitlement expressible as a cap -- see
        // CreateCurrencyCommandHandler.EnforceMultiCurrencyEntitlementAsync.
        db.Currencies.Add(Currency.CreateBase(organization.Id));

        var features = new AccountingFeatureSelections(
            request.TrackInventory,
            request.MultipleLocations,
            request.MultipleWarehouses,
            request.MultiCurrency,
            request.Manufacturing,
            request.PosRetail,
            request.PosRestaurant);
        db.TenantSubscriptions.Add(TenantSubscription.CreateTrial(organization.Id, features));

        db.OrganizationMemberships.Add(
            OrganizationMembership.CreateAccepted(organization.Id, currentUser.UserId, MembershipRole.Admin));

        await db.SaveChangesAsync(cancellationToken);

        return new CreateOrganizationResult(organization.Id, organization.Name, organization.WorkspaceName);
    }
}
