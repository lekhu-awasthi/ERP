namespace ErpApp.Application.Common.Locations;

/// <summary>
/// Phase 32b -- a <b>list</b> query carrying a location-scopable key, which narrows its own rows to
/// the caller's locations instead of being refused outright.
///
/// <para>A list is the one shape where "403" is the wrong answer and the right one is "fewer rows".
/// The live product's own Invoice list has a LOCATION column and a location filter, and a Member
/// scoped to HeadOffice is expected to open that list and see HeadOffice invoices -- not an error
/// page. So <c>AuthorizationBehavior</c> lets these through on any location grant, and the handler
/// takes it from there via <see cref="LocationAccessScope"/>.</para>
///
/// <para><b>The interface alone is not the enforcement</b> -- the handler's own filter is, and no
/// reflection test can prove a handler filters. What the marker buys is that adding a new list over
/// a location-bearing type is a deliberate act: <c>LocationScopeSweepGuardTests</c> fails the build
/// unless every request with a location-scopable key declares one of the four markers, so the
/// question "what does this do for a location-scoped caller?" cannot go unasked.</para>
/// </summary>
public interface ILocationFilteredQuery;

/// <summary>
/// Phase 32b -- a request that carries a location-scopable key but genuinely has <b>no location
/// dimension</b>, so holding the key at any one location is enough.
///
/// <para>Two kinds qualify, and both are helpers rather than document operations. The
/// <c>Preview*GlPosting</c> queries compute GL lines from a payload the caller is holding in an
/// unsaved form -- there is no row, no stored location, and nothing per-location to leak.
/// <c>GetBomTemplateQuery</c> scales a bill of materials, which is master data every Member can read
/// anyway; it rides <c>ProductionJournalCreate</c> only so a prefill cannot be an easier door than
/// the create it prefills.</para>
///
/// <para>Refusing these would be the phase-20f failure mode in miniature: a Member scoped to one
/// branch could create an invoice but not preview its posting, or load a BOM for the journal they
/// are allowed to write. Implementing this interface is therefore a claim, and the type's own doc
/// comment is expected to say why the request has no location.</para>
/// </summary>
public interface ILocationAgnosticRequest;
