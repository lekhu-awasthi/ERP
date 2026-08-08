using MediatR;

namespace ErpApp.Application.Identity.Commands.ForgotPassword;

public sealed record ForgotPasswordCommand(string Email) : IRequest;
