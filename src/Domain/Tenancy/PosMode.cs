namespace ErpApp.Domain.Tenancy;

/// <summary>
/// Phase 60 -- which point-of-sale shell a <see cref="BillingLocation"/> runs, if any. A <b>mode of
/// every location</b>, not a kind of location (phase 59 Decision B): the vendor's own Locations form
/// offers exactly two POS types, <c>{label:"Bar / Restaurant", value:"Bar"}</c> and
/// <c>{label:"Retail", value:"Retail"}</c>, and returns its HeadOffice typed <c>Retail</c>. So the
/// type is an attribute every location carries, and it replaces the two reserved
/// <c>BillingLocationType</c> members phase 32 had put on the wrong axis.
///
/// <para><see cref="None"/> is member 0 and means something ("no till here"), which is the shape
/// phase 58's gotcha warns about: project it nullable anywhere a missing row could be read as a
/// value. Every location that existed before this phase is <see cref="None"/> -- no till existed,
/// so that is the only true value (phase-60-status.md Decision A).</para>
///
/// <para>Stored as a string, unlike <c>LocationType</c>'s int: a mode is read in <c>sqlcmd</c>
/// proofs and filtered on by the till's location picker, and a string survives a member being
/// inserted.</para>
/// </summary>
public enum PosMode
{
    /// <summary>No till runs at this location. The value for every location a tenant has not opted
    /// into POS, and always settable, even after the entitlement is gone (phase-20f: a flag-off
    /// tenant must still be able to switch a thing off).</summary>
    None = 0,

    /// <summary>The vendor's "Retail": Retail and Delivery tabs. Needs <see cref="TenantFeature.PosRetail"/>.</summary>
    Retail = 1,

    /// <summary>The vendor's "Bar / Restaurant": Dine In, Take Away, Delivery, KOT and the floor plan.
    /// Needs <see cref="TenantFeature.PosRestaurant"/>.</summary>
    Restaurant = 2,
}
