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
    /// <summary>Returns the fully-formatted number (prefix + fiscal-year segment if configured +
    /// the sequential number) -- callers just stamp the result directly onto the document.</summary>
    Task<string> GetNextNumberAsync(Guid organizationId, DocumentType documentType, CancellationToken cancellationToken);
}
