using ErpApp.Application.Common.Behaviors;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Sales.Commands.ApproveInvoice;
using ErpApp.Application.Sales.Commands.UpdateInvoice;
using ErpApp.Application.Sales.Queries.GetInvoice;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using ErpApp.Domain.Tenancy;

namespace ErpApp.Application.UnitTests.Common.Behaviors;

/// <summary>
/// Phase 32b -- <c>AuthorizationBehavior</c>'s location-scoped second chance.
///
/// <para>The shape being pinned was confirmed live on 2026-09-09: the role editor's two sections are
/// <b>independent stores</b>. A role holding 25 of 94 organization-wide Transactions grants showed
/// 0 of 94 at every one of the tenant's three locations, and the organization-wide section is
/// subtitled "Apply across all billing locations". So the effective rule is org-wide <b>OR</b>
/// location-specific, never AND -- which is also what makes Decision D true: a tenant that never
/// opens the second section behaves exactly as it did before this phase.</para>
/// </summary>
public class LocationScopedAuthorizationTests
{
    // The membership's role is what the grant join matches on, and OrganizationMembership
    // .CreateAccepted resolves MembershipRole.Member to this well-known id -- so the grants under
    // test must hang off the same one.
    private static readonly Guid RoleId = Role.MemberId;

    [Fact]
    public async Task An_organization_wide_grant_still_works_at_every_location()
    {
        var f = await Fixture.CreateAsync(organizationWideInvoiceView: true);

        var result = await f.Handle(new GetInvoiceQuery(f.OrganizationId, f.PosInvoiceId));

        Assert.NotNull(result);
    }

    [Fact]
    public async Task A_location_grant_admits_a_document_at_that_location()
    {
        var f = await Fixture.CreateAsync(headOfficeInvoiceView: true);

        var result = await f.Handle(new GetInvoiceQuery(f.OrganizationId, f.HeadOfficeInvoiceId));

        Assert.NotNull(result);
    }

    /// <summary>
    /// The direction that matters. Same user, same key, a real document at a location they do not
    /// hold -- and the message names the location so they know which chip to ask for.
    /// </summary>
    [Fact]
    public async Task A_location_grant_refuses_a_document_at_another_location_and_names_it()
    {
        var f = await Fixture.CreateAsync(headOfficeInvoiceView: true);

        var exception = await Assert.ThrowsAsync<ForbiddenException>(
            () => f.Handle(new GetInvoiceQuery(f.OrganizationId, f.PosInvoiceId)));

        Assert.Contains("POS.Sales.Invoice.View", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The other direction of phase-31's both-directions rule. The same user, on a document id that
    /// does not exist, is let through to the handler -- which is what shows they hold the pipeline
    /// key at all. One leg alone is consistent with the route simply being broken.
    /// </summary>
    [Fact]
    public async Task A_nonexistent_document_reaches_the_handler_so_it_can_answer_404()
    {
        var f = await Fixture.CreateAsync(headOfficeInvoiceView: true);

        var result = await f.Handle(new GetInvoiceQuery(f.OrganizationId, Guid.NewGuid()));

        Assert.NotNull(result);
    }

    [Fact]
    public async Task Holding_the_key_at_no_location_at_all_is_still_a_plain_refusal()
    {
        var f = await Fixture.CreateAsync();

        var exception = await Assert.ThrowsAsync<ForbiddenException>(
            () => f.Handle(new GetInvoiceQuery(f.OrganizationId, f.HeadOfficeInvoiceId)));

        Assert.Contains("Sales.Invoice.View", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("POS.", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("HO.", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// An Approve command carries no <c>ILocationScopedDocument</c> of its own -- the resolver reads
    /// its <c>ILockDateSensitiveDocument</c> target instead. This is the test that pins that reuse
    /// actually enforces something, rather than the thirty Approve/Void commands quietly falling
    /// through unchecked.
    /// </summary>
    [Fact]
    public async Task An_approve_command_is_scoped_through_its_lock_date_target()
    {
        var f = await Fixture.CreateAsync(headOfficeInvoiceApprove: true);

        var exception = await Assert.ThrowsAsync<ForbiddenException>(
            () => f.Handle(new ApproveInvoiceCommand(f.OrganizationId, f.PosInvoiceId)));

        Assert.Contains("POS.Sales.Invoice.Approve", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The reason an Update needs <i>both</i> markers: the row it edits and the location it writes
    /// are two different places to be allowed. Editing a HeadOffice invoice is fine; moving it to POS
    /// is not, and only the written-location half can catch that.
    /// </summary>
    [Fact]
    public async Task An_update_cannot_move_a_document_into_a_location_the_caller_does_not_hold()
    {
        var f = await Fixture.CreateAsync(headOfficeInvoiceEdit: true);

        var moveToPos = new UpdateInvoiceCommand(
            f.OrganizationId, f.HeadOfficeInvoiceId, f.ContactId, f.WarehouseId,
            new DateOnly(2026, 7, 1), null, []) { LocationId = f.PosLocationId };

        var exception = await Assert.ThrowsAsync<ForbiddenException>(() => f.Handle(moveToPos));

        Assert.Contains("POS.Sales.Invoice.Edit", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_update_that_leaves_the_document_where_it_is_passes()
    {
        var f = await Fixture.CreateAsync(headOfficeInvoiceEdit: true);

        var stayPut = new UpdateInvoiceCommand(
            f.OrganizationId, f.HeadOfficeInvoiceId, f.ContactId, f.WarehouseId,
            new DateOnly(2026, 7, 1), null, []) { LocationId = f.HeadOfficeLocationId };

        var result = await f.Handle(stayPut);

        Assert.NotNull(result);
    }

    private sealed class Fixture
    {
        public required IAppDbContext Db { get; init; }
        public required Guid OrganizationId { get; init; }
        public required Guid UserId { get; init; }
        public required Guid HeadOfficeLocationId { get; init; }
        public required Guid PosLocationId { get; init; }
        public required Guid HeadOfficeInvoiceId { get; init; }
        public required Guid PosInvoiceId { get; init; }
        public required Guid ContactId { get; init; }
        public required Guid WarehouseId { get; init; }

        public static async Task<Fixture> CreateAsync(
            bool organizationWideInvoiceView = false,
            bool headOfficeInvoiceView = false,
            bool headOfficeInvoiceApprove = false,
            bool headOfficeInvoiceEdit = false)
        {
            var db = TestAppDbContext.Create();
            var organization = Organization.Create(
                "Acme Traders", "Retail", null, new DateOnly(2026, 1, 1), true,
                "acme-traders", null, null, null, null, Guid.NewGuid());
            db.Organizations.Add(organization);

            // AllTransactions, so the type is in scope regardless of which mode a later phase makes
            // the default -- this suite is about the permission check, not about DocumentLocationScope.
            var settings = TenantSettings.CreateDefault(organization.Id);
            settings.SetLocationSettings(LocationScopeMode.AllTransactions, false);
            db.TenantSettings.Add(settings);

            var userId = Guid.NewGuid();
            db.OrganizationMemberships.Add(
                OrganizationMembership.CreateAccepted(organization.Id, userId, MembershipRole.Member));
            db.Roles.Add(Role.Create(RoleId, "Member"));

            var headOffice = BillingLocation.CreateHeadOffice(organization.Id);
            var pos = BillingLocation.Create(organization.Id, "POS", "POS Retail", null, null);
            db.BillingLocations.AddRange(headOffice, pos);

            if (organizationWideInvoiceView)
            {
                db.RolePermissions.Add(
                    RolePermission.Create(Guid.NewGuid(), RoleId, PermissionKeys.InvoiceView, true));
            }

            foreach (var (granted, key) in new[]
                     {
                         (headOfficeInvoiceView, PermissionKeys.InvoiceView),
                         (headOfficeInvoiceApprove, PermissionKeys.InvoiceApprove),
                         (headOfficeInvoiceEdit, PermissionKeys.InvoiceEdit),
                     })
            {
                if (granted)
                {
                    db.RolePermissions.Add(
                        RolePermission.Create(Guid.NewGuid(), RoleId, key, true, headOffice.Id));
                }
            }

            var contactId = Guid.NewGuid();
            var warehouseId = Guid.NewGuid();

            var headOfficeInvoice = Invoice.Create(
                organization.Id, contactId, warehouseId, new DateOnly(2026, 7, 1), null, null, null);
            headOfficeInvoice.SetLocation(headOffice.Id);

            var posInvoice = Invoice.Create(
                organization.Id, contactId, warehouseId, new DateOnly(2026, 7, 1), null, null, null);
            posInvoice.SetLocation(pos.Id);

            db.Invoices.AddRange(headOfficeInvoice, posInvoice);
            await db.SaveChangesAsync();

            return new Fixture
            {
                Db = db,
                OrganizationId = organization.Id,
                UserId = userId,
                HeadOfficeLocationId = headOffice.Id,
                PosLocationId = pos.Id,
                HeadOfficeInvoiceId = headOfficeInvoice.Id,
                PosInvoiceId = posInvoice.Id,
                ContactId = contactId,
                WarehouseId = warehouseId,
            };
        }

        /// <summary>
        /// Runs the behavior alone, with a sentinel <c>next()</c>. The handler is deliberately not
        /// involved: this suite is about which requests reach it, and a sentinel makes "was it let
        /// through?" unambiguous -- including on the nonexistent-document leg, where the real handler
        /// would throw NotFound and hide the fact that the pipeline allowed it.
        /// </summary>
        public async Task<object> Handle<TRequest>(TRequest request)
            where TRequest : notnull
        {
            var behavior = new AuthorizationBehavior<TRequest, object>(Db, new FakeCurrentUserService(UserId));
            var sentinel = new object();

            return await behavior.Handle(request, () => Task.FromResult(sentinel), CancellationToken.None);
        }
    }

}
