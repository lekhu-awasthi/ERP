using System.Reflection;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace ErpApp.Application;

/// <summary>
/// Composition-root extension for the Application layer. Wires MediatR handlers,
/// FluentValidation validators, and the shared pipeline behaviors.
/// Called once from Api/Program.cs (builder.Services.AddApplication()).
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(assembly));
        services.AddValidatorsFromAssembly(assembly);

        // Pipeline behaviors run in registration order, wrapping every command/query.
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(Common.Behaviors.LoggingBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(Common.Behaviors.ValidationBehavior<,>));

        return services;
    }
}
