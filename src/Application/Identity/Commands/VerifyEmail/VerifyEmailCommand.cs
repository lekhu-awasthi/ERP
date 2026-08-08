using MediatR;

namespace ErpApp.Application.Identity.Commands.VerifyEmail;

public sealed record VerifyEmailCommand(string Email, string Code) : IRequest;
