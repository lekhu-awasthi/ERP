using MediatR;

namespace ErpApp.Application.Common.Ping;

/// <summary>
/// Trivial no-op query with no domain meaning. Exists solely so Phase 0's test harness
/// (Application.UnitTests) and the Api's /health check have something real to send through
/// the full MediatR + FluentValidation pipeline, proving it fires end to end before any
/// real command/query exists. Safe to delete once Phase 1 adds real handlers.
/// </summary>
public sealed record PingQuery : IRequest<string>;

public sealed class PingQueryHandler : IRequestHandler<PingQuery, string>
{
    public Task<string> Handle(PingQuery request, CancellationToken cancellationToken)
        => Task.FromResult("pong");
}
