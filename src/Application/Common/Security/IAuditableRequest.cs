using ErpApp.Domain.Common;

namespace ErpApp.Application.Common.Security;

/// <summary>
/// Declares the DocumentType a Create/UpdateXCommand affects, so AuditBehavior
/// (Common/Behaviors/AuditBehavior.cs) can write an audit row without a per-command-type switch.
/// Approve/Void commands don't need this -- they already implement
/// <see cref="ILockDateSensitiveDocument"/>, which exposes the same DocumentType (plus the
/// DocumentId this interface alone can't, since a Create command's document doesn't exist until
/// after its handler runs -- see <see cref="IAuditableRequestWithId"/>), so AuditBehavior reuses
/// that interface directly for those two actions rather than asking every Approve/Void command to
/// implement a second, redundant marker.
///
/// A command that implements neither this interface nor <see cref="ILockDateSensitiveDocument"/>
/// is simply not audited -- the same "no marker interface, no gate" pattern
/// AuthorizationBehavior/LockDateBehavior already use for their own opt-in interfaces.
/// </summary>
public interface IAuditableRequest
{
    DocumentType AuditDocumentType { get; }
}

/// <summary>
/// Declares both the DocumentType and the DocumentId a Create/UpdateXCommand affects. Update
/// commands implement this (their Id is already on the request); Create commands implement only
/// the base <see cref="IAuditableRequest"/>, since the new document's Id isn't known until its
/// handler's response comes back -- AuditBehavior resolves that case by reading an `Id` property
/// off the response via reflection instead.
/// </summary>
public interface IAuditableRequestWithId : IAuditableRequest
{
    Guid AuditDocumentId { get; }
}
