using MediatR;

namespace ErpApp.Application.Identity.Commands.Login;

public sealed record LoginCommand(string Email, string Password) : IRequest<LoginResult>;

public sealed record LoginResult(Guid UserId, string Email, string FullName, string Token, DateTimeOffset ExpiresAt);
