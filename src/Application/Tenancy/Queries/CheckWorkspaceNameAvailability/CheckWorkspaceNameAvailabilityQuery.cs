using MediatR;

namespace ErpApp.Application.Tenancy.Queries.CheckWorkspaceNameAvailability;

public sealed record CheckWorkspaceNameAvailabilityQuery(string WorkspaceName)
    : IRequest<CheckWorkspaceNameAvailabilityResult>;

public sealed record CheckWorkspaceNameAvailabilityResult(bool IsAvailable);
