using System.Collections;
using System.Reflection;
using ErpApp.Application.Common.Filtering;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Catalog;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Common;

/// <summary>
/// Phase 47 — the <c>Sort by</c> sweep's guard, in the shape <see cref="SearchSweepGuardTests"/>
/// established for the search term.
///
/// <para><b>Why this guard has a behavioural half, and why that is the whole point.</b> Phase 44
/// found that a sweep guard written over <i>query records</i> cannot see whether the <i>handler</i>
/// applies the filter it accepts — both statutory registers narrowed only their return half for
/// three phases while a guard over their request records reported them conformant. A <c>Sort</c>
/// property that reaches no <c>OrderBy</c> is exactly that defect: the control appears, the request
/// carries the value, the validator accepts it, and the rows never move. So the last test here seeds
/// two documents whose creation order and business date deliberately <b>disagree</b>, drives the real
/// handler through both orderings, and asserts the two answers differ. A pass where the two agreed
/// would prove nothing, which is why the seed is built to make them disagree.</para>
///
/// <para>The set this sweeps is derived, not listed: a <b>document list</b> is a paginated list query
/// that also filters by date range, which is phase 34b's own rule for what has a business date
/// (<c>TenantIndexConvention</c> reads the same sentence to decide which tables get the two indexes
/// an ordering may be offered on). That derivation yields fifteen queries over sixteen screens —
/// <c>ListPaymentsQuery</c> backs both the customer and the supplier payment lists — and not the
/// "18" the roadmap carried forward from phase 40's approximate shape census.</para>
/// </summary>
public class SortSweepGuardTests
{
    private static readonly Assembly ApplicationAssembly = typeof(IRequirePermission).Assembly;

    private static readonly DateOnly Earlier = new(2026, 5, 10);
    private static readonly DateOnly Later = new(2026, 5, 20);

    /// <summary>
    /// Document-list queries that deliberately offer no ordering, each with the reason.
    ///
    /// <para>There is one, and it is worth more than its own exemption: it is the case that shows the
    /// rule has teeth. Phase 47 gave it <b>two</b> reasons — the screen is not a document list, and
    /// the aggregate carried neither index the rule requires, because <c>TenantIndexConvention</c>
    /// recognised a business date by <b>name</b> and a cheque spells its <c>ChequeDate</c>.</para>
    ///
    /// <para><b>Phase 50 measured the second reason away.</b> <c>Cheque</c> now carries
    /// <c>(OrganizationId, ChequeDate)</c>, so the exemption rests on the screen alone:
    /// <c>cheque-register-page</c> is a dashboard with two status tabs and a state-transition action
    /// per row, carries no <c>app-list-chrome</c>, and is filed under Accounting — phase 40's
    /// Decision F evidence for <c>transaction-list-page</c>, arrived at the same way. That remains a
    /// good reason and it is why this is still an exemption. But it is now <i>one</i> reason, and the
    /// half that has gone is stated rather than deleted, because a reader who remembers the old
    /// sentence would otherwise carry on believing the schema is what it was. If this screen ever
    /// grows list chrome, the index is already there and the exemption should go — which is the
    /// opposite of what the phase-47 text implies, and is phase 45's lesson that an allow-list reason
    /// can become the argument for the other conclusion.</para>
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> Exempt =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ListChequesQuery"] =
                "The Cheque Register, not a document list: a dashboard plus two status tabs, no list "
                + "chrome, and a per-row status transition rather than a row you open. NOTE that this "
                + "is now the only reason -- phase 50 added (OrganizationId, ChequeDate) on a "
                + "measurement, so the aggregate no longer lacks the index the rule requires. See "
                + "ListSortIndexCorrespondenceTests.The_cheque_exemptions_index_half_has_been_measured_away.",

            // Phase 56. This one is NOT "the harness was awkward" -- phase 55's own lesson is that
            // an exemption on a brand-new screen is how a seam stays empty for six phases, and it
            // taught this harness rather than exempt itself. The difference here is that the
            // premise of the rule is false for this subject rather than inconvenient.
            //
            // The rule (ISortableQuery) is that an ordering may be offered exactly where an index
            // leads on (OrganizationId, <that column>), which for a document means two columns:
            // CreatedAt and its own business date. A GL posting has exactly ONE date --
            // GlJournalEntry.PostedAt -- because phase 26a decided the entry stores no copy of its
            // document's business date, and there is no CreatedAt beside it. So a Sort by menu here
            // would be a control with nothing to choose between, which is the outcome the rule
            // exists to prevent rather than an exception to it. (The reference product's control on
            // these panes is a direction toggle, Recent First / Oldest First -- a different control
            // this codebase does not have anywhere.)
            //
            // The premise is asserted independently, so this reason cannot quietly stop being true:
            // ListSortIndexCorrespondenceTests.A_gl_posting_has_exactly_one_date fails if anyone
            // adds a second date to GlJournalEntry, at which point this exemption should go.
            ["ListBookTransactionsQuery"] =
                "A GL posting has exactly one date (GlJournalEntry.PostedAt -- phase 26a), so there "
                + "is nothing for a Sort by menu to choose between. The premise is pinned by "
                + "ListSortIndexCorrespondenceTests.A_gl_posting_has_exactly_one_date.",
        };

    [Fact]
    public void Every_document_list_query_accepts_an_ordering_or_states_why_not()
    {
        var missing = DocumentListQueries()
            .Where(t => !typeof(ISortableQuery).IsAssignableFrom(t))
            .Where(t => !Exempt.ContainsKey(t.Name))
            .Select(t => t.Name)
            .ToList();

        Assert.True(
            missing.Count == 0,
            "These document lists offer no ordering and give no reason. Either implement "
            + "ISortableQuery (and apply it in the handler) or add the query to "
            + "SortSweepGuardTests.Exempt with the reason:\n  "
            + string.Join("\n  ", missing));
    }

    /// <summary>
    /// An exemption naming a query that no longer exists is a reason nobody can check, and it hides
    /// the fact that the guard has stopped covering something (phase 34a's rule for allow-lists).
    /// </summary>
    [Fact]
    public void Every_exemption_names_a_query_that_still_exists()
    {
        var names = DocumentListQueries().Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        var stale = Exempt.Keys.Where(k => !names.Contains(k)).ToList();

        Assert.True(stale.Count == 0, "Stale exemptions: " + string.Join(", ", stale));
    }

    /// <summary>
    /// The validator half. Asserted as behaviour rather than as "a rule exists", for the same reason
    /// the search guard bounds a term by sending one character too many: a rule declared against the
    /// wrong property would satisfy any check that only asks whether one was declared.
    /// </summary>
    [Fact]
    public void Every_sortable_query_refuses_an_unknown_ordering_and_accepts_every_allowed_one()
    {
        var wrong = new List<string>();

        foreach (var type in DocumentListQueries().Where(typeof(ISortableQuery).IsAssignableFrom))
        {
            var validator = ValidatorFor(type);

            if (validator is null)
            {
                wrong.Add($"{type.Name} (no validator at all)");
                continue;
            }

            var parentId = Guid.NewGuid();

            foreach (var allowed in ListSort.DocumentOrderings.Append(null))
            {
                var query = Activate(type, Guid.NewGuid(), allowed, parentId);

                if (query is not null && !validator.Validate(new ValidationContext<object>(query)).IsValid)
                {
                    wrong.Add($"{type.Name} (rejects '{allowed ?? "null"}', which it is supposed to accept)");
                }
            }

            var unknown = Activate(type, Guid.NewGuid(), "customer", parentId);

            if (unknown is not null && validator.Validate(new ValidationContext<object>(unknown)).IsValid)
            {
                wrong.Add($"{type.Name} (accepts an ordering no index leads on)");
            }
        }

        Assert.True(
            wrong.Count == 0,
            "An unrecognised ordering has to be a 400 naming the field, never a silent fall back to "
            + "the default -- a list that ignores the ordering it was asked for is a control that "
            + "looks like it works and rows that never change:\n  "
            + string.Join("\n  ", wrong));
    }

    /// <summary>
    /// The half phase 44 says a record-shaped guard cannot see: that the handler <i>applies</i> what
    /// the request accepts.
    /// </summary>
    [Fact]
    public async Task Every_sortable_handler_actually_applies_the_ordering_it_accepts()
    {
        var inert = new List<string>();

        foreach (var queryType in DocumentListQueries().Where(typeof(ISortableQuery).IsAssignableFrom))
        {
            var db = TestAppDbContext.Create();
            var organizationId = Guid.NewGuid();
            var entityType = EntityFor(queryType);

            // Created first but dated later; created second but dated earlier. The two orderings
            // therefore have to return opposite orders, and a handler that ignores `Sort` returns
            // the same order twice.
            // One parent for both rows, so a parent-scoped list returns both of them and the two
            // orderings are comparable (phase 55's ListBankStatementLinesQuery is the first).
            var parentName = ParentScopedIdFor(queryType);
            var parentId = Guid.NewGuid();

            var older = Seed(db, entityType, organizationId, Later, DateTimeOffset.UtcNow.AddMinutes(-5), parentName, parentId);
            var newer = Seed(db, entityType, organizationId, Earlier, DateTimeOffset.UtcNow.AddMinutes(-4), parentName, parentId);
            await db.SaveChangesAsync(CancellationToken.None);

            var byDefault = await RunAsync(queryType, db, organizationId, null, parentId);
            var byNewest = await RunAsync(queryType, db, organizationId, ListSort.Newest, parentId);
            var byDate = await RunAsync(queryType, db, organizationId, ListSort.DocumentDate, parentId);

            if (!byNewest.SequenceEqual([IdOf(newer), IdOf(older)]))
            {
                inert.Add($"{queryType.Name} ('{ListSort.Newest}' is not newest-first)");
            }

            if (!byDate.SequenceEqual([IdOf(older), IdOf(newer)]))
            {
                inert.Add($"{queryType.Name} ('{ListSort.DocumentDate}' does not order by the document date)");
            }

            if (!byDefault.SequenceEqual(byNewest))
            {
                inert.Add($"{queryType.Name} (null is not this list's default ordering)");
            }
        }

        Assert.True(
            inert.Count == 0,
            "These handlers accept an ordering and do not apply it. A Sort property that reaches no "
            + "OrderBy is phase 44's defect exactly -- the control appears, the value travels, and "
            + "the rows never move:\n  "
            + string.Join("\n  ", inert));
    }

    /// <summary>
    /// A document list: paginated, and filtered by a date range. That second half is phase 34b's own
    /// rule for "this aggregate has a business date", which is also the sentence
    /// <c>TenantIndexConvention</c> reads to decide which tables carry the two indexes an ordering
    /// may be offered on — so the set that <i>may</i> sort and the set that <i>is swept</i> are
    /// derived from one fact rather than listed twice.
    /// </summary>
    private static IEnumerable<Type> DocumentListQueries() =>
        ApplicationAssembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false })
            .Where(t => typeof(IDateRangeFilteredQuery).IsAssignableFrom(t))
            .Where(t => t.Name.StartsWith("List", StringComparison.Ordinal))
            .Where(t => t.Name.EndsWith("Query", StringComparison.Ordinal))
            .OrderBy(t => t.Name, StringComparer.Ordinal);

    /// <summary>
    /// <c>ListPurchaseBillsQuery</c> → the <c>PurchaseBills</c> set → <see cref="Domain.Purchasing.PurchaseBill"/>.
    /// Derived from the name rather than from the response type, because two of the fifteen project
    /// a DTO and so never name their aggregate in their signature.
    /// </summary>
    private static Type EntityFor(Type queryType)
    {
        var setName = queryType.Name["List".Length..^"Query".Length];

        var property = typeof(IAppDbContext).GetProperties()
            .FirstOrDefault(p => p.Name == setName)
            ?? throw new InvalidOperationException($"IAppDbContext has no '{setName}' set for {queryType.Name}.");

        return property.PropertyType.GetGenericArguments()[0];
    }

    /// <summary>
    /// Builds one document through its own <c>Create</c> factory — never by reaching past it — and
    /// stamps the <c>CreatedAt</c> the test needs, which is the one value a factory takes from the
    /// clock and no caller can pass.
    /// </summary>
    private static object Seed(
        IAppDbContext db,
        Type entityType,
        Guid organizationId,
        DateOnly date,
        DateTimeOffset createdAt,
        string? parentName = null,
        Guid parentId = default)
    {
        var factory = entityType.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == "Create" && m.ReturnType == entityType)
            .OrderBy(m => m.GetParameters().Length)
            .First();

        var entity = factory.Invoke(
            null,
            factory.GetParameters()
                .Select(p => Argument(p, organizationId, date, parentName, parentId))
                .ToArray())!;

        // A parent-scoped list's handler checks its parent exists and 404s otherwise, so the row
        // alone is not enough. Today the only parent is a cash-and-bank Account (phase 55); the
        // case is named rather than generalised, exactly like the Product one below, because a
        // guess at "any Guid ending in AccountId" would seed rows nothing asked for.
        if (parentName == "BankAccountId" && !db.Accounts.Local.Any(a => a.Id == parentId))
        {
            var account = Account.Create(
                organizationId, "BC0001", "Nabil Bank", AccountRootType.Asset, Guid.NewGuid(), AccountKind.Bank);
            typeof(Account).GetProperty("Id")!.GetSetMethod(nonPublic: true)!.Invoke(account, [parentId]);
            db.Accounts.Add(account);
        }

        entityType.GetProperty("CreatedAt")!.GetSetMethod(nonPublic: true)!.Invoke(entity, [createdAt]);

        // The two manufacturing lists project a DTO through an inner join on Products, so a row that
        // does not join is a row that does not come back -- and the assertion would then fail for a
        // reason that has nothing to do with ordering.
        if (entityType.GetProperty("ProductId")?.GetValue(entity) is Guid productId)
        {
            var product = Product.Create(
                organizationId, ProductType.Goods, "Seeded", "SEED-" + productId.ToString()[..8],
                Guid.NewGuid(), Guid.NewGuid(), null, true, 1m, 1m, VatRate.ThirteenPercentVat, 0, true);
            typeof(Product).GetProperty("Id")!.GetSetMethod(nonPublic: true)!.Invoke(product, [productId]);
            db.Products.Add(product);
        }

        var set = typeof(IAppDbContext).GetProperties()
            .First(p => p.PropertyType == typeof(DbSet<>).MakeGenericType(entityType))
            .GetValue(db)!;

        set.GetType().GetMethod("Add", [entityType])!.Invoke(set, [entity]);

        return entity;
    }

    private static Guid IdOf(object entity) => (Guid)entity.GetType().GetProperty("Id")!.GetValue(entity)!;

    private static async Task<IReadOnlyList<Guid>> RunAsync(
        Type queryType, IAppDbContext db, Guid organizationId, string? sort, Guid parentId)
    {
        var responseType = queryType.GetInterfaces()
            .First(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(MediatR.IRequest<>))
            .GetGenericArguments()[0];

        var handlerType = ApplicationAssembly.GetTypes()
            .First(t => t is { IsClass: true, IsAbstract: false }
                && typeof(MediatR.IRequestHandler<,>).MakeGenericType(queryType, responseType).IsAssignableFrom(t));

        // Most of these handlers take (db, currentUser) because they apply phase 32b's per-location
        // permission scope. One does not: a bank account carries no LocationId, so
        // ListBankStatementLinesQueryHandler has nothing to scope and takes (db) alone. The guard
        // asks the constructor rather than assuming, so a handler's dependencies are free to change.
        var handler = handlerType.GetConstructors().Single().GetParameters().Length == 1
            ? Activator.CreateInstance(handlerType, db)!
            : Activator.CreateInstance(handlerType, db, new FakeCurrentUserService(Guid.NewGuid()))!;

        var task = (Task)handlerType.GetMethod("Handle")!
            .Invoke(handler, [Activate(queryType, organizationId, sort, parentId), CancellationToken.None])!;

        await task;

        var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
        var items = (IEnumerable)result.GetType().GetProperty("Items")!.GetValue(result)!;

        return items.Cast<object>().Select(IdOf).ToList();
    }

    private static IValidator? ValidatorFor(Type queryType) =>
        ApplicationAssembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false })
            .Where(t => typeof(IValidator<>).MakeGenericType(queryType).IsAssignableFrom(t))
            .Select(t => (IValidator?)Activator.CreateInstance(t))
            .FirstOrDefault();

    /// <summary>
    /// Builds a query with this organization and this ordering, and defaults for the rest.
    ///
    /// <para><b><paramref name="parentId"/> is what makes a parent-scoped list testable here.</b>
    /// Most of these lists stand alone, so every constructor parameter beyond the organization has
    /// a sensible default. <c>ListBankStatementLinesQuery</c> (phase 55) does not: a statement
    /// belongs to one bank account and the account is required, so defaulting it to
    /// <c>Guid.Empty</c> made the validator reject the query for a reason that had nothing to do
    /// with the ordering -- and this guard read that as "rejects 'newest'". Any required
    /// non-nullable <c>Guid</c> other than the organization gets the parent id, which
    /// <see cref="ParentScopedIdFor"/> also hands to the seeder so the rows and the query agree
    /// about which parent they mean.</para>
    /// </summary>
    private static object? Activate(Type queryType, Guid organizationId, string? sort, Guid parentId)
    {
        var constructor = queryType.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();

        var arguments = constructor.GetParameters()
            .Select(p => p.Name switch
            {
                nameof(IOrganizationScoped.OrganizationId) => organizationId,
                nameof(ISortableQuery.Sort) => sort,
                "Page" => 1,
                "PageSize" => (object)PagingDefaults.DefaultPageSize,
                _ => p is { HasDefaultValue: false, ParameterType: { } pt } && pt == typeof(Guid)
                    ? parentId
                    : p.HasDefaultValue
                        ? p.DefaultValue
                        : p.ParameterType.IsValueType
                            ? Activator.CreateInstance(p.ParameterType)
                            : null,
            })
            .ToArray();

        return constructor.Invoke(arguments);
    }

    /// <summary>
    /// The name of the one required parent this query is scoped to, or null when it stands alone.
    /// Matched by <b>name</b> between the query's constructor and the aggregate's <c>Create</c>
    /// factory, which is how the seeded rows end up under the parent the query will ask for.
    /// </summary>
    private static string? ParentScopedIdFor(Type queryType) =>
        queryType.GetConstructors()
            .OrderByDescending(c => c.GetParameters().Length)
            .First()
            .GetParameters()
            .Where(p => p is { HasDefaultValue: false })
            .Where(p => p.ParameterType == typeof(Guid))
            .Select(p => p.Name)
            .FirstOrDefault(n => n != nameof(IOrganizationScoped.OrganizationId));

    /// <summary>
    /// Fills one factory parameter. The two decimals that are refused at zero are named rather than
    /// defaulted, so a factory that grows a third validated amount fails here loudly instead of
    /// being quietly fed a value it rejects.
    /// </summary>
    private static object? Argument(
        ParameterInfo parameter, Guid organizationId, DateOnly date, string? parentName, Guid parentId)
    {
        if (parameter.Name == "organizationId")
        {
            return organizationId;
        }

        // The factory parameter the query's required parent id names, matched case-insensitively
        // (a query says BankAccountId and a factory says bankAccountId). Without this the Guid
        // branch below would give every seeded row a different parent and the list would be empty.
        if (parentName is not null && string.Equals(parameter.Name, parentName, StringComparison.OrdinalIgnoreCase))
        {
            return parentId;
        }

        if (parameter.ParameterType == typeof(DateOnly))
        {
            return date;
        }

        if (parameter.ParameterType == typeof(decimal))
        {
            // A production order refuses a non-positive Output Quantity and a payment a non-positive
            // Amount; every other decimal on these factories is a discount or a TDS amount, where
            // zero is the ordinary value.
            return parameter.Name is "outputQuantity" or "amount" ? 1m : 0m;
        }

        // A bank statement line refuses a zero amount, and default(StatementAmount) is one --
        // the same shape as the two decimals named above, in a value type.
        if (parameter.ParameterType == typeof(StatementAmount))
        {
            return StatementAmount.Deposit(1m);
        }

        if (parameter.ParameterType == typeof(Guid))
        {
            // A fresh Guid per parameter, not one reused: a warehouse transfer refuses a From and a
            // To that are the same warehouse.
            return Guid.NewGuid();
        }

        if (parameter.ParameterType == typeof(string))
        {
            return "Seeded";
        }

        return parameter.HasDefaultValue
            ? parameter.DefaultValue
            : parameter.ParameterType.IsValueType
                ? Activator.CreateInstance(parameter.ParameterType)
                : null;
    }
}
