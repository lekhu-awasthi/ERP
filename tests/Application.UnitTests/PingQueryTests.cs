using ErpApp.Application;
using ErpApp.Application.Common.Ping;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace ErpApp.Application.UnitTests;

/// <summary>
/// Phase 0 harness proof: sends PingQuery through the real DI container (MediatR +
/// FluentValidation + LoggingBehavior + ValidationBehavior all wired via
/// AddApplication()) and asserts the handler actually ran. Proves the pipeline fires
/// end to end, per roadmap Phase 0 task 2. Delete PingQuery/this test once Phase 1
/// adds a real command with a real validator to exercise instead.
/// </summary>
public class PingQueryTests
{
    [Fact]
    public async Task Pipeline_delivers_ping_through_mediatr()
    {
        var services = new ServiceCollection();
        services.AddLogging(); // LoggingBehavior needs an ILogger<T> registered to resolve.
        services.AddApplication();
        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var result = await mediator.Send(new PingQuery());

        Assert.Equal("pong", result);
    }
}
