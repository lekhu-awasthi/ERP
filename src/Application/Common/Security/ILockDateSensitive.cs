using ErpApp.Domain.Common;

namespace ErpApp.Application.Common.Security;

/// <summary>
/// Declares that a Create/Update command's own request payload already carries the business
/// <see cref="Date"/> LockDateBehavior (Common/Behaviors/LockDateBehavior.cs) must check against
/// Organization.LockDate (roadmap Phase 16a, NFR-3.4) -- no DB lookup needed, since the document
/// doesn't exist yet (Create) or its Date is exactly the value being written (Update). Every
/// Create/UpdateXCommand across the 13 ApprovableTransaction types already has a positional
/// <c>DateOnly Date</c> property, so implementing this interface adds no new code beyond the
/// interface name itself.
/// </summary>
public interface ILockDateSensitive
{
    DateOnly Date { get; }
}

/// <summary>
/// Declares that an Approve/Void command's date must be looked up from the document it targets
/// (the command itself only ever carries OrganizationId+Id, never the document's own Date) --
/// LockDateBehavior resolves <see cref="LockDateDocumentType"/>/<see cref="LockDateDocumentId"/>
/// against the right table via one 13-branch switch, the same "13 concrete blocks, not one
/// generic helper" precedent TransactionApprovalQueryHandler (Phase 12) established for
/// cross-document-type dispatch in this codebase.
/// </summary>
public interface ILockDateSensitiveDocument
{
    DocumentType LockDateDocumentType { get; }

    Guid LockDateDocumentId { get; }
}
