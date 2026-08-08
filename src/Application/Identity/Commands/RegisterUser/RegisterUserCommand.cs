using MediatR;

namespace ErpApp.Application.Identity.Commands.RegisterUser;

public sealed record RegisterUserCommand(
    string FullName,
    string Email,
    string Phone,
    string Password) : IRequest<RegisterUserResult>;

public sealed record RegisterUserResult(Guid UserId, string Email);
