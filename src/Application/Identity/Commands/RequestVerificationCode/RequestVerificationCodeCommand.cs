using MediatR;

namespace ErpApp.Application.Identity.Commands.RequestVerificationCode;

public sealed record RequestVerificationCodeCommand(string Email) : IRequest;
