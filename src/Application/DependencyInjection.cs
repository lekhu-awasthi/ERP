using System.Reflection;
using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Configuration.Commands.DeleteLookup;
using ErpApp.Application.Configuration.Queries.ListLookups;
using ErpApp.Application.Payments.Posting;
using ErpApp.Application.Purchasing.Posting;
using ErpApp.Application.Sales.Posting;
using ErpApp.Application.Sales.Stock;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Configuration;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Tenancy;
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

        // Phase 4 (Accounting core) -- AccountGroup is the same "pure {id, name, parent}" tree
        // shape (plus an immutable RootType that doesn't conflict with ITenantLookupEntity, same
        // precedent as ContactGroup/ProductCategory) so it reuses the generic pair too.
        RegisterLookupHandlers<AccountGroup>(services);

        // Phase 5 (Sales chain) -- Warehouse is a minimal single-column lookup (see its doc
        // comment), same generic pair.
        RegisterLookupHandlers<Warehouse>(services);

        // Phase 6 (Purchase chain) -- TdsType is the same "pure {code, name, rate}" lookup shape.
        RegisterLookupHandlers<TdsType>(services);

        // IGlPostingRule<T> (architecture-spec.md §3.4) -- pure TDocument->GL-lines mappers, one
        // per document type that posts to GL. Registered so ApproveXCommandHandler and
        // PreviewGlPostingQueryHandler share the exact same instance type (no duplicated math).
        services.AddTransient<IGlPostingRule<JournalVoucher>, JournalVoucherPostingRule>();
        services.AddTransient<IGlPostingRule<CashTransfer>, CashTransferPostingRule>();
        // Invoice/Payment's posting rules take a resolved *PostingInput record, not the aggregate
        // itself -- see InvoicePostingInput's doc comment for why.
        services.AddTransient<IGlPostingRule<InvoicePostingInput>, InvoicePostingRule>();
        services.AddTransient<IGlPostingRule<CreditNotePostingInput>, CreditNotePostingRule>();
        services.AddTransient<IGlPostingRule<PaymentPostingInput>, PaymentPostingRule>();
        // Purchase-side resolved-input-record posting rules, same InvoicePostingRule split.
        services.AddTransient<IGlPostingRule<PurchaseBillPostingInput>, PurchaseBillPostingRule>();
        services.AddTransient<IGlPostingRule<ExpensePostingInput>, ExpensePostingRule>();
        services.AddTransient<IGlPostingRule<DebitNotePostingInput>, DebitNotePostingRule>();

        // Phase 5's stock-decrement seam (architecture-spec.md §3.5) -- a literal always-Ok stub
        // until Phase 7's real FIFO ledger exists (see AlwaysOkStockAvailabilityPolicy).
        services.AddTransient<IStockAvailabilityPolicy, AlwaysOkStockAvailabilityPolicy>();

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
