using ErpApp.Domain.Common;

namespace ErpApp.Domain.Tenancy;

/// <summary>
/// What kind of place a <see cref="BillingLocation"/> is. Confirmed live 2026-09-07 on a
/// location-enabled tenant: the seeded set is <c>HO / HeadOffice</c>, <c>1002 / POS Restaurant</c>
/// and <c>1003 / POS Retail</c>, and the <b>Add New Location dialog carries no type field at all</b>
/// (Location Code, Location Name, Address, Warehouse -- nothing else). So a type is assigned by the
/// system, never chosen by the tenant, which corrects both erp-module-scan.md's Features section and
/// architecture-spec.md §3.7 -- each modelled <c>locationType</c> as if it were part of the create
/// form.
///
/// <para>Only <see cref="HeadOffice"/> and <see cref="Standard"/> are reachable in this product:
/// HeadOffice is the row seeded at Organization creation, Standard is what every user-created branch
/// is. The two POS members exist because architecture-spec.md §6 reserves the seam and because the
/// permission matrix scopes itself by location (phase 32b) -- a POS location must be nameable there
/// before POS itself is built. Nothing in the ERP back-office branches on the two POS members;
/// they are reserved vocabulary, not dead code paths.</para>
/// </summary>
public enum BillingLocationType
{
    /// <summary>The one row every Organization is seeded with. See <see cref="BillingLocation.CreateHeadOffice"/>.</summary>
    HeadOffice = 1,

    /// <summary>An ordinary branch the tenant added itself -- what "+ ADD NEW LOCATION" creates.</summary>
    Standard = 2,

    PosRestaurant = 3,
    PosRetail = 4,
}

/// <summary>
/// A billing location (branch) the tenant transacts from -- one row of Organization &gt; Features &gt;
/// Billing Location's Code/Name/Address/Warehouse table, confirmed live 2026-09-07 on a
/// location-enabled tenant (`cadehi.tigg.app`; the tenant the rest of the scan was taken from has the
/// entitlement switched off, which is why this screen could not be read until now).
///
/// <para>Every tenant has exactly one of these from the moment its Organization is created -- the
/// HeadOffice row -- and a tenant with the <see cref="TenantFeature.MultipleLocations"/> entitlement
/// can add more. That is deliberately the <b>same shape as <see cref="Currency"/></b> and, before it,
/// <see cref="Warehouse"/>: <b>the entitlement is a cap on the list, not a block on documents</b>
/// (phase-20f Decision #4, phase-28's second instance, this the third). Seeding is unconditional for
/// the same reason Currency's is -- every document defaults to a location, so a tenant with no
/// BillingLocation row would have a document header pointing at a location its own list does not
/// contain.</para>
///
/// <para><b>Id, not Code, is what a document stores.</b> This diverges from Currency, on purpose.
/// Currency stores its three-letter code on documents because the code is globally meaningful and is
/// printed on the document itself. A location code is tenant-local ("HO", "1002"), is renamed freely,
/// and is shown by joining to this row (the live Invoice header renders <c>Name (Code)</c> and the
/// list grid renders the name) -- so an FK is both smaller and the only thing that survives a rename.
/// </para>
///
/// <para>Lives in <c>Domain.Tenancy</c> beside <see cref="Warehouse"/> and <see cref="Currency"/>,
/// the other two rows of the same Features tab. It does <b>not</b> implement
/// <see cref="ITenantLookupEntity"/>: the generic <c>ListLookupsQuery&lt;T&gt;</c> projects Name and
/// IsActive only, and every consumer of this list needs Code, Address and the warehouse too, so it
/// carries its own list query rather than a lookup projection that every caller would have to
/// re-query behind.</para>
/// </summary>
public sealed class BillingLocation
{
    /// <summary>The code the seeded HeadOffice row carries, matching the reference product's own.</summary>
    public const string HeadOfficeCode = "HO";

    /// <summary>The name the seeded HeadOffice row carries, matching the reference product's own.</summary>
    public const string HeadOfficeName = "HeadOffice";

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }

    /// <summary>Tenant-local short code ("HO", "1002"). Required by the live dialog and shown in the
    /// list's CODE column and in the document header's <c>Name (Code)</c> label. Unique per tenant --
    /// see BillingLocationConfiguration.</summary>
    public string Code { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    /// <summary>Required on the live Add New Location dialog, but nullable here and empty on the
    /// seeded HeadOffice row -- the reference product's own seeded HO and POS rows show a blank
    /// Address in the list, so the field is required of a <i>user</i> creating a branch, not an
    /// invariant of the row. The validator enforces it on the create path; this stays nullable so
    /// the seeded row does not have to invent an address the tenant never gave.</summary>
    public string? Address { get; private set; }

    /// <summary>The location's default warehouse -- "Select Default Warehouse" on the live dialog.
    /// Nullable because the reference product's own POS rows carry none, and because a tenant without
    /// the MultipleWarehouses entitlement may have no Warehouse row at all (nothing seeds one; see
    /// phase-20f Decision #4), which would otherwise make this aggregate unconstructible.</summary>
    public Guid? WarehouseId { get; private set; }

    public BillingLocationType LocationType { get; private set; }

    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>True for the one row every tenant is seeded with and can never deactivate. Derived
    /// from <see cref="LocationType"/> rather than stored, exactly as
    /// <see cref="Currency.IsBaseCurrency"/> is derived from its code -- so it cannot drift or be
    /// flipped by a stray update.</summary>
    public bool IsHeadOffice => LocationType == BillingLocationType.HeadOffice;

    private BillingLocation()
    {
    }

    public static BillingLocation Create(
        Guid organizationId,
        string code,
        string name,
        string? address,
        Guid? warehouseId,
        BillingLocationType locationType = BillingLocationType.Standard)
    {
        if (locationType == BillingLocationType.HeadOffice)
        {
            throw new InvalidOperationException(
                "The HeadOffice location is seeded at Organization creation and cannot be created again. "
                + "Use CreateHeadOffice.");
        }

        return new BillingLocation
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Code = Require(code, "Code"),
            Name = Require(name, "Name"),
            Address = string.IsNullOrWhiteSpace(address) ? null : address.Trim(),
            WarehouseId = warehouseId,
            LocationType = locationType,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>
    /// The HeadOffice row seeded for every Organization at creation -- see
    /// CreateOrganizationCommandHandler, which seeds it beside <see cref="Currency.CreateBase"/> and
    /// for the identical reason.
    ///
    /// <para>Takes no warehouse: nothing seeds a Warehouse either (phase-20f Decision #4), so there
    /// is none to point at yet. An Admin sets it later from the Features screen.</para>
    /// </summary>
    public static BillingLocation CreateHeadOffice(Guid organizationId)
    {
        return new BillingLocation
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Code = HeadOfficeCode,
            Name = HeadOfficeName,
            Address = null,
            WarehouseId = null,
            LocationType = BillingLocationType.HeadOffice,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>
    /// Renames a location, re-points its default warehouse, and activates or deactivates it. The
    /// HeadOffice row may be renamed and given a warehouse but <b>never deactivated</b>: every
    /// document defaults to it and every historical document may point at it, so a tenant that
    /// switched it off would be unable to raise any document at all. Same rule, same reasoning and
    /// same wording as <see cref="Currency.Update"/>'s base-currency guard -- the phase-20f lesson
    /// (a flag-off tenant must still function) applied to a row rather than a flag.
    ///
    /// <para><see cref="LocationType"/> is not a parameter: it is system-assigned and the live
    /// dialog has no control for it.</para>
    /// </summary>
    public void Update(string code, string name, string? address, Guid? warehouseId, bool isActive)
    {
        if (IsHeadOffice && !isActive)
        {
            throw new InvalidOperationException(
                $"'{Name}' is this organization's HeadOffice location and cannot be deactivated.");
        }

        Code = Require(code, "Code");
        Name = Require(name, "Name");
        Address = string.IsNullOrWhiteSpace(address) ? null : address.Trim();
        WarehouseId = warehouseId;
        IsActive = isActive;
    }

    private static string Require(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"A billing location's {field} is required.");
        }

        return value.Trim();
    }
}
