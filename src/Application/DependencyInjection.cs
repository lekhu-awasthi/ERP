using System.Reflection;
using ErpApp.Application.Configuration.Commands.DeleteLookup;
using ErpApp.Application.Configuration.Queries.ListLookups;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Configuration;
using ErpApp.Domain.Contacts;
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

        // Pipeline behaviors run in registration order, wrapping every command/query: log, then
        // validate the request shape, then check the caller's permission (Authorization last so
        // a malformed request 400s before it triggers a permission-check DB lookup).
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(Common.Behaviors.LoggingBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(Common.Behaviors.ValidationBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(Common.Behaviors.AuthorizationBehavior<,>));

        // ListLookupsQuery<TLookup>/DeleteLookupCommand<TLookup> (Configuration foundation,
        // architecture-spec.md §4.10) are generic in only one of IRequestHandler<,>'s two type
        // parameters (TResponse is fixed/derived, not itself a free parameter) -- .NET DI's
        // open-generic-to-open-generic registration requires the implementation to close over
        // exactly the service's generic parameters in order, which this doesn't, so MediatR's
        // assembly scan can't discover these. Registered explicitly here instead; add one line
        // per verb whenever a new lookup type joins the pattern.
        RegisterLookupHandlers<CreditTerm>(services);
        RegisterLookupHandlers<PaymentMode>(services);
        RegisterLookupHandlers<CustomStatus>(services);
        RegisterLookupHandlers<ReportingTagCategory>(services);
        RegisterLookupHandlers<ReportingTagOption>(services);

        // Phase 3 (Contacts & Catalog) -- ContactGroup/ProductCategory/UnitOfMeasurement are the
        // same "pure {id, name, parent?}" lookup shape as Phase 2's, so they reuse the generic
        // ListLookupsQuery<TLookup>/DeleteLookupCommand<TLookup> pair instead of hand-rolling
        // duplicates.
        RegisterLookupHandlers<ContactGroup>(services);
        RegisterLookupHandlers<ProductCategory>(services);
        RegisterLookupHandlers<UnitOfMeasurement>(services);

        return services;
    }

    private static void RegisterLookupHandlers<TLookup>(IServiceCollection services)
        where TLookup : class, Domain.Common.ITenantLookupEntity
    {
        services.AddTransient<
            IRequestHandler<ListLookupsQuery<TLookup>, IReadOnlyList<TLookup>>, ListLookupsQueryHandler<TLookup>>();

        // Registered against the 2-arg IRequestHandler<,Unit> form (not IRequestHandler<TRequest>)
        // -- MediatR's Send(IRequest) pipeline resolves the former from the container; the
        // 1-arg form is only a convenience interface handlers implement, not what's looked up.
        services.AddTransient<
            IRequestHandler<DeleteLookupCommand<TLookup>, Unit>, DeleteLookupCommandHandler<TLookup>>();
    }
}
