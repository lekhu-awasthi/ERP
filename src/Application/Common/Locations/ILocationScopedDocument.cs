using ErpApp.Domain.Common;

namespace ErpApp.Application.Common.Locations;

/// <summary>
/// Phase 32b -- declares that a request targets an <b>existing</b> document by id, so
/// <c>AuthorizationBehavior</c> must read that document's own <c>LocationId</c> before it can decide
/// whether a location-scoped grant covers the caller. The Approve/Void/detail-View half of the
/// enforcement seam; the Create/Update half needs no interface of its own because
/// <see cref="ILocationBearingCommand"/> already carries the requested location on every one of
/// those 34 commands. An Update implements both: the row it edits and the location it writes are
/// two different places the caller must be allowed to touch.
///
/// <para><b>The document's <i>type</i> is deliberately not on this interface.</b> It is always
/// derivable from the request's own <c>PermissionKey</c> -- <c>Sales.Invoice.Approve</c> names
/// Invoice, and <see cref="Security.LocationScopedPermissions.DocumentTypeOf"/> is the one place
/// that reads it. Declaring it here as well would be two values that must agree forever, and it
/// would force a nullable type on the polymorphic requests (attachments, comments, custom fields,
/// print), whose parent can be a Contact rather than a document. Those requests get their location
/// scoping for free by declaring an id and nothing else: when their parent is a Contact the key is
/// <c>Contacts.Contact.View</c>, which is not location-scopable, so the whole check is skipped.</para>
///
/// <para><b>Deliberately its own marker rather than a reuse of
/// <see cref="Security.ILockDateSensitiveDocument"/>, whose shape overlaps.</b> Phase-31 lesson (c)
/// says to reuse an existing marker set rather than invent a third -- but only when the set is the
/// same. It is not: a lock date freezes <i>writes</i>, so that interface is on Approve and Void
/// only, while a location grant also governs <i>reads</i> (a Member scoped to HeadOffice must not
/// open a POS Retail invoice, print it, or read its comments). This set is strictly wider, and
/// widening the lock-date marker to match would silently start rejecting reads against a lock date.
/// <see cref="LocationScopeResolver"/> nonetheless <i>reads</i> the lock-date marker when this one is
/// absent, which is what spares all thirty Approve/Void commands a duplicate declaration.</para>
///
/// <para>A document that cannot be found resolves to no location and the request is let through,
/// exactly as <c>LockDateBehavior</c> does: 404 is the handler's own answer to give, and a pipeline
/// that turned a missing id into a 403 would leak which ids exist. That is also what makes the
/// phase-31 both-directions E2E proof possible -- 404 on a nonexistent document shows the caller
/// holds the pipeline key, and 403 on a real one shows the location check fired.</para>
/// </summary>
public interface ILocationScopedDocument
{
    /// <summary>The row whose <c>LocationId</c> decides the location-scoped key.</summary>
    Guid LocationDocumentId { get; }

    /// <summary>
    /// Override <b>only</b> when the targeted row is not of the type the permission key names, which
    /// is almost never. <c>ApplyPaymentAllocationCommand</c> is the single case in this codebase: it
    /// always rides <c>Payments.Payment.Edit</c>, but the credit it applies can be a Journal Voucher
    /// line, so the row to read a location from lives in a different table than the key names. Left
    /// as a default-implemented member returning null so the other 45 implementations say nothing,
    /// and <c>LocationScopeSweepGuardTests</c> pins that the exception stays deliberate.
    /// </summary>
    DocumentType? LocationDocumentTypeOverride => null;
}
