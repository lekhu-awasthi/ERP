using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using MediatR;

namespace ErpApp.Application.Platform.Queries.GlobalSearch;

/// <summary>
/// Phase 33 -- the top bar's global search (Ctrl + /).
///
/// <para><b>This is the record half only.</b> The reference product's endpoint returns a single flat
/// list mixing stored rows with navigation targets ("Invoice", "Add Invoice", the five inventory
/// reports) -- confirmed live, and on a typical query the navigation half is the majority of it. This
/// codebase splits them: the navigation half is a client-side catalogue
/// (<c>web/src/app/shared/navigation/navigation-catalog.ts</c>) and only stored rows come from the
/// server. The route table, which routes are feature-gated and which are behind a permission are all
/// already client knowledge; a server-side second copy would be a source of truth that drifts, which
/// is the failure this codebase writes guard tests to prevent. It also means the navigation half
/// renders on the first keystroke instead of after a round trip.</para>
///
/// <para><b>What is matched, and on what.</b> Master data (<see cref="GlobalSearchCollection.Contact"/>,
/// <see cref="GlobalSearchCollection.Product"/>, <see cref="GlobalSearchCollection.Account"/>) matches
/// on <i>name or code</i>. Documents match on <b>code only</b> -- a document has no name, which is
/// why the live payload carries none for them and why searching a customer's name does not surface
/// that customer's invoices. That is the reference product's behaviour, observed, and it is also the
/// only behaviour a document number index can support cheaply.</para>
///
/// <para><b>Permissions are not this key's job.</b> <see cref="PermissionKeys.GlobalSearchView"/> is a
/// blanket key -- the phase-12/phase-23 pattern, fourth use -- whose purpose is that
/// <c>AuthorizationBehavior</c> runs at all. The handler re-derives, per collection, whether the
/// caller holds that collection's own View key, and narrows document hits to the caller's billing
/// locations. A Member without <c>Sales.Invoice.View</c> cannot find an invoice by its number here,
/// and a Member granted <c>Sales.Invoice.View</c> at one location only cannot find another
/// location's invoice.</para>
/// </summary>
public sealed record GlobalSearchQuery(Guid OrganizationId, string Term, int? Limit)
    : IRequest<IReadOnlyList<GlobalSearchHitDto>>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.GlobalSearchView;
}

/// <summary>
/// What kind of stored row a hit is. Mirrors the reference product's <c>collection</c> field, except
/// that its fifteen separate document collections ("Journal Voucher", "Purchase Bill", …) collapse
/// onto <see cref="Document"/> plus <see cref="GlobalSearchHitDto.DocumentType"/> -- this codebase
/// already has an enum naming exactly those fifteen, and a second string vocabulary alongside it
/// would be the ordinal-bridging hazard phase-26a and phase-27a both warn about.
/// </summary>
public enum GlobalSearchCollection
{
    Contact = 0,
    Product = 1,
    Account = 2,
    Document = 3,
}

/// <summary>
/// One hit. Carries <b>what the thing is</b>, never where it lives: the client owns the route table,
/// so it turns a (collection, documentType, id) triple into a link. The reference product does the
/// same for its own record rows -- they carry no url either, only the navigation rows do.
/// </summary>
/// <param name="Name">
/// The row's name -- <b>null for a document</b>, which has only a number. Confirmed live: document
/// rows in the reference payload carry no name field at all, and render code-as-title.
/// </param>
/// <param name="SubKind">
/// The row's own sub-classification, for the result's second line: <c>Customer</c>/<c>Supplier</c>/
/// <c>Lead</c> for a contact, <c>Goods</c>/<c>Service</c> for a product, the root type for an
/// account. Null for a document, whose second line is its <paramref name="DocumentType"/>.
/// </param>
public sealed record GlobalSearchHitDto(
    GlobalSearchCollection Collection,
    DocumentType? DocumentType,
    Guid Id,
    string Code,
    string? Name,
    string? SubKind);
