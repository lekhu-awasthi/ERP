namespace ErpApp.Application.Common.Locations;

/// <summary>
/// Marks a command that carries the billing location its document is raised from (Phase 32,
/// FR-2.3/FR-3.3). Implemented by the Create and Update command of every one of
/// <c>DocumentMechanisms.LocationBearing</c>'s 17 types -- the 15 transactional documents plus the
/// two opening-balance row commands, whose live inline form leads with a Location field.
///
/// <para>Deliberately the same shape as <see cref="Currencies.ICurrencyBearingCommand"/> directly
/// beside it, and for the same reason: purely a marker for a sweep test, read by nothing in the
/// MediatR pipeline. It exists because "every document type that shows this field has it wired end
/// to end" is the sort of claim that rots silently -- a document type added by a later phase would
/// simply never offer a location, with nothing failing.
/// <c>LocationBearingCommandSweepGuardTests</c> enumerates the types from
/// <c>DocumentMechanisms.LocationBearing</c> and asserts a matching pair of commands implements
/// this, the same guard shape phases 27a and 28 used.</para>
///
/// <para><b>Nullable, and null does not mean "no location".</b> It means "the caller did not choose
/// one", which <see cref="LocationResolver"/> turns into the tenant's HeadOffice when the document
/// type is in scope, and into a real null when it is not. So every existing caller, every existing
/// test and every single-location client keeps working untouched -- the same compatibility property
/// phase 28's null-means-base-currency gave.</para>
/// </summary>
public interface ILocationBearingCommand
{
    Guid? LocationId { get; }
}
