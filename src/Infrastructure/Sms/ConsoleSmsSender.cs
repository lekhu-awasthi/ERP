using ErpApp.Application.Common.Sms;
using Microsoft.Extensions.Logging;

namespace ErpApp.Infrastructure.Sms;

public sealed class ConsoleSmsSender(ILogger<ConsoleSmsSender> logger) : ISmsSender
{
    public Task SendAsync(string phoneNumber, string message, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("[SMS -> {PhoneNumber}] {Message}", phoneNumber, message);
        return Task.CompletedTask;
    }
}
