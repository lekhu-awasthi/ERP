using ErpApp.Application.Common.Numbering;
using ErpApp.Domain.Common;

namespace ErpApp.Application.UnitTests.TestSupport;

public sealed class FakeDocumentNumberGenerator : IDocumentNumberGenerator
{
    private int _next = 1;

    public Task<string> GetNextNumberAsync(Guid organizationId, DocumentType documentType, CancellationToken cancellationToken)
    {
        return Task.FromResult($"{documentType}-{_next++:D4}");
    }
}
