using ErpApp.Application.Common.Numbering;
using ErpApp.Domain.Common;

namespace ErpApp.Application.UnitTests.TestSupport;

public sealed class FakeDocumentNumberGenerator : IDocumentNumberGenerator
{
    private int _next = 1;

    /// <summary>Phase 32 -- records the location each call was made with, so an Approve handler test
    /// can assert the document's own location reached the generator without needing SQL Server. The
    /// real per-location counter behaviour is a raw-SQL concern and is proved against a real database
    /// in <c>DocumentNumberGeneratorTests</c>, not here.</summary>
    public List<Guid?> RequestedLocationIds { get; } = [];

    public Task<string> GetNextNumberAsync(
        Guid organizationId, DocumentType documentType, CancellationToken cancellationToken, Guid? locationId = null)
    {
        RequestedLocationIds.Add(locationId);
        return Task.FromResult($"{documentType}-{_next++:D4}");
    }
}
