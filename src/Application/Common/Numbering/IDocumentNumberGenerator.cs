using ErpApp.Domain.Common;

namespace ErpApp.Application.Common.Numbering;

/// <summary>
/// Assigns the real, sequential document number for an Organization+DocumentType pair
/// (architecture-spec.md §3.1). Called from each module's ApproveXCommandHandler -- never from
/// CreateXCommandHandler, since every document sits at the literal placeholder "DRAFT" until
/// approved (confirmed live). Nothing calls this yet (Phase 2 builds it ahead of the real
/// transactional aggregates that will, from Phase 4 onward).
///
/// Guarantees no two calls for the same (organizationId, documentType) ever return the same
/// number, even under concurrent callers (fiscal/tax-audit implications of a duplicate outweigh
/// the cost of an occasional gap, which is tolerated). See
/// Infrastructure.Persistence.DocumentNumberGenerator for the atomic-increment implementation.
/// </summary>
public interface IDocumentNumberGenerator
{
    /// <summary>
    /// Returns the fully-formatted number (prefix + fiscal-year segment if configured + the
    /// sequential number) -- callers just stamp the result directly onto the document.
    ///
    /// <para>Phase 32: <paramref name="locationId"/> is the approving document's billing location, and
    /// is consulted <b>only</b> when that document type's rule has
    /// <c>LocationWiseNumbering</c> on -- the live "Enable Location-wise Next Number" toggle. With it
    /// off, or with a null location, every caller shares the one counter exactly as before, so no
    /// existing tenant's numbering changes and the parameter is optional for the numbering-pool
    /// callers (Account/Contact/Product codes) that have no location at all.</para>
    /// </summary>
    Task<string> GetNextNumberAsync(
        Guid organizationId, DocumentType documentType, CancellationToken cancellationToken, Guid? locationId = null);
}
