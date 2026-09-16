# Known gotchas — full narrative

The one-sentence rules live in `CLAUDE.md`'s Known gotchas section; this file holds each rule's full
story (symptom, cause, how it was caught, the fix), moved verbatim out of `CLAUDE.md` on 2026-09-02 and
regrouped by area. Every entry cites the phase status doc that has the complete history. When a new
gotcha is found, add the one-liner to `CLAUDE.md` and the narrative here, under the matching heading.

## Configuration, hosting, pipeline and auth

- **Never read configuration (`builder.Configuration.Get<T>()`, `.GetConnectionString(...)`, etc.) as a top-level statement in `Program.cs` before `.Build()` if the value gets captured into a closure that runs later** (an options-configure delegate, a DbContext options builder, `AddJwtBearer(options => ...)`, etc.) — that snapshot is taken too early to see config sources added afterward (notably `WebApplicationFactory`'s test-only overrides in `Api.IntegrationTests`), and the bug is easy to miss locally because developer-machine `user-secrets` are already loaded by the time the eager read happens, masking it. Bit us twice in Phase 1a (Jwt:SigningKey and ConnectionStrings:Default) — see `docs/phase-1a-status.md` item 8. Prefer `services.AddOptions<T>().Bind(configuration.GetSection(...))` (lazy) or resolve `IOptions<T>`/`IConfiguration` from `IServiceProvider` inside a lazily-invoked delegate.
- **Every new `AddOptions<T>().Validate(...).ValidateOnStart()` registration silently breaks all four host-booting `Api.IntegrationTests` suites — but only in CI.** `WebApplicationFactory<Program>` boots the real host, so a failed start-up validation throws `OptionsValidationException` in `InitializeAsync` before a single assertion runs. It passes on a developer machine because `WebApplicationFactory` runs in the **Development** environment, which loads the Api's `user-secrets`; a CI runner has none. That is the mask, and it is the same shape as the eager-config-read gotcha above. Each of `AccountingFlowTests`/`DocumentNumberGeneratorTests`/`ExceptionHandlingTests`/`HealthEndpointTests` therefore carries its own `AddInMemoryCollection` of test-only values for **every** `ValidateOnStart` option (currently `Jwt`, `Email`, `Turnstile`) — add the new key to all four in the same commit that adds the option. Phase 20g added `TurnstileOptions` without this and turned CI red; the .NET job's `env:` block now also carries all three sets as a second line of defence.
- **Do not try to reproduce that CI failure with `ASPNETCORE_ENVIRONMENT=Production`.** It does drop user-secrets, but it also flips Minimal APIs' `RouteHandlerOptions.ThrowOnBadRequest`, which defaults to true **only** in Development: under Production a malformed request body returns a bare 400 with an **empty response body** instead of raising the `BadHttpRequestException` that `UseAppExceptionHandler` turns into ProblemDetails. `ExceptionHandlingTests.Empty_string_for_a_nullable_Guid_body_property_returns_400_not_500` fails spuriously that way (`JsonException: The input does not contain any JSON tokens`) while being perfectly fine in CI. Verified both ways during the Phase 21b CI fix: 9/10 under a Production override, 10/10 under the default environment. To prove a config-only gap, temporarily delete the key from one suite's `AddInMemoryCollection` and watch it throw — that isolates the variable without changing framework behaviour.
- JWT bearer's default inbound claim mapping remaps `"sub"`/`"email"` to legacy XML-namespace claim types — set `options.MapInboundClaims = false;` or `ClaimsPrincipal.FindFirstValue(JwtRegisteredClaimNames.Sub)` silently returns null.
- Cookie `SameSite` must be `None` (not `Lax`), not just `Secure`, for the Angular dev server (`http://localhost:4200`) to receive it from the Api (`https://localhost:7104`) — differing scheme alone makes Chrome treat same-host requests as cross-site.
- `Response.Cookies.Delete(name, options)` only actually clears a cookie if `options` (`Path`/`Secure`/`SameSite`) matches what was used when the cookie was set — mismatched options are silently ignored by the browser.
- `IOptions<T>` (unlike `IOptionsSnapshot<T>`/`IOptionsMonitor<T>`) caches its bound value at first resolution and does not observe a later `dotnet user-secrets set` — even though the user-secrets JSON file is a reloading config source, a singleton service holding `IOptions<T>` keeps serving the value it saw at startup. Changing a user-secret mid-session (e.g. to flip a verification service between pass/fail for manual E2E) requires restarting the Api process, not just re-running `dotnet user-secrets set`. See `docs/phase-20g-status.md`.

- **`dotnet run --no-launch-profile` starts in Production, so `user-secrets` do not load** — and any `ValidateOnStart` option whose value lives there kills start-up. Phase 27b's manual E2E died on `Missing 'Turnstile:SecretKey'` before a single request. Pass `ASPNETCORE_ENVIRONMENT=Development` alongside `--no-launch-profile`; note this also avoids the `ThrowOnBadRequest` flip described above.
- **An `AsyncLocal` request-scoped value is legitimate for ambient *formatting* context, and `RequestCalendar` is this codebase's one instance of it.** The consumers are ~40 static export methods whose workbook is built inside a `Results.Stream` callback that runs after the endpoint returned and has no `HttpContext`. One middleware writes it; everything else reads. Treat it as `CultureInfo.CurrentCulture`, not as a licence to make anything else ambient — a value that decides *what a handler does* still goes through the constructor.
- MediatR 12.4.1's `RequestHandlerDelegate<TResponse>` is parameterless — call `next()`, not `next(cancellationToken)`, in pipeline behaviors.
- **A FluentValidation rule built from a captured `Func` cannot name its own property**, and the failure is a **500 on every endpoint that validator guards**, not a compile error and not a test failure. `RuleForEach(x => selector(x))` where `selector` is a `Func` parameter throws `InvalidOperationException: Could not infer property name for expression` the first time the rule *runs* -- FluentValidation walks an expression tree to derive the name, and a compiled delegate has none. **No handler unit test can catch this**: tests call handlers directly, so `ValidationBehavior` never executes; it surfaces only against the real API. Take `Expression<Func<T, IEnumerable<TElement>>>` instead (note `IEnumerable`, not `IReadOnlyList` -- `RuleForEach`'s type inference needs it). Same shape as phase-9 bug #1's captured-`Func`-in-`Where`, and the same remedy. A validator suite that actually calls `validator.Validate(command)` is the only thing that covers it -- see `ProductionValidatorTests` (Phase 25).

## EF Core, migrations and the InMemory provider

- `dotnet ef` needs `Microsoft.EntityFrameworkCore.Design` referenced by whichever project is passed as `--startup-project` (`Api`), not just `Infrastructure`.
- Validating a new migration by running `dotnet ef database update` against a scratch/Docker database (to sanity-check the generated SQL) does **not** apply it to the actual local dev database the Api's `ConnectionStrings:Default` user-secret points at — always follow up with a plain `dotnet ef database update` (no `--connection` override) before manually click-testing, or every endpoint that touches the new tables 500s with `Invalid object name`.
- `dotnet ef migrations add` orders operations by model diff, not by data safety — a migration that both drops a column and adds its replacement can scaffold the `DropColumn` *before* the `AddColumn`, silently losing the data needed to backfill the new one. Always read a scaffolded migration before applying it when a column's being replaced/retyped; reorder by hand (create/seed anything new first, backfill via raw SQL while the old column still exists, drop it last) — see `docs/phase-1c-status.md`'s bug #1 for a worked example (`OrganizationMembership.Role` string → `RoleId` FK).
- `dotnet ef migrations add` bundles the *entire* pending model diff into whichever migration you're adding — there's no way to scaffold two migrations from one model change without temporarily hiding part of the model from the DbContext. Plan for one migration per `migrations add` invocation, not per logical change; hand-review the single result instead of trying to force a split (see `docs/phase-2-status.md`'s bug #6).
- `dotnet ef migrations add`/`database update` diffs against whatever `Api` project output the `--startup-project` build actually produced — rebuilding only `Infrastructure` standalone in between (`dotnet build src/Infrastructure/...`) leaves `Api`'s own output stale, so a subsequent `dotnet ef` call can report `PendingModelChangesWarning` and re-scaffold an already-applied diff. Let `dotnet ef` rebuild itself (omit `--no-build`) after any partial rebuild, or rebuild the full solution first. See `docs/phase-16a-status.md`.
- An EF Core enum property configured with `.HasDefaultValue(someValue)` where `someValue` differs from the enum's CLR `default` (its first-declared, "= 0" member) needs `.ValueGeneratedNever()` chained after it, or EF silently substitutes the SQL default on every insert whenever the in-memory value happens to equal `default(TEnum)` — even if a factory method explicitly chose that value. See `docs/phase-2-status.md`'s bug #2.
- `Database.SqlQuery<TResult>` (EF Core 8+) only accepts *composable* SQL (effectively a plain `SELECT`) — an `UPDATE ... OUTPUT` statement throws `InvalidOperationException` at query-translation time (not caught by `dotnet build`, only by actually running the query against a real provider). For an atomic read-and-increment, use an explicit transaction with `SELECT ... WITH (UPDLOCK, ROWLOCK)` followed by a separate `ExecuteSqlInterpolatedAsync` `UPDATE`, not a single `OUTPUT`-returning statement. Also: for a scalar `TResult`, the result set's column must be aliased `AS Value` — `SqlQuery<T>` binds by that column name, not positionally. See `docs/phase-2-status.md`'s bugs #3–4 (`DocumentNumberGenerator`).
- A MediatR handler generic over a type parameter constrained by an interface (e.g. `Handler<TLookup> where TLookup : ISomeInterface`) that accesses a property via that constraint (`x.SomeProperty`) risks EF Core's LINQ translator failing to map the *interface's* `PropertyInfo` back to the concrete entity's mapped column. Use `EF.Property<T>(x, nameof(ISomeInterface.SomeProperty))` instead. Also: such a handler's DI registration must target the closed generic `IRequestHandler<TRequest, TResponse>` (2 type args) explicitly — MediatR's assembly scan can't discover it, and a request implementing MediatR's bare `IRequest`/`IRequestHandler<T>` (1-arg convenience interfaces) may not satisfy the 2-arg registration's generic constraint depending on version; implement `IRequest<Unit>`/`IRequestHandler<T, Unit>` explicitly instead. See `docs/phase-2-status.md`'s bugs #1 and #5. Related: EF Core also can't translate a captured `Func<T, TKey>` delegate inside `.Where()` — a generic query helper parameterized by a selector `Func` compiles but throws at translation time against a real provider; write concrete per-type `Where` blocks instead (see `docs/phase-9-status.md`'s bug #1 and Phase 12's 13-concrete-blocks precedent).
- An EF Core handler that replaces an *entire* encapsulated (private-backing-field) child collection in one save (`aggregate.ClearLines(); foreach (line) aggregate.AddLine(...);`) can get mis-tracked by the InMemory provider — a same-count Clear+re-Add was observed marking the wrong entries `Modified`/`Deleted` instead of `Added`/`Deleted`, throwing `DbUpdateConcurrencyException: ... does not exist in the store` on `SaveChangesAsync`, even with `.Include()`, `.IsRequired()`, and explicit `.OnDelete(Cascade)` all in place. Don't rely on collection-navigation-triggered fixup for a full-collection replace — snapshot the old children first and explicitly `db.Set<TChild>().RemoveRange(oldChildren)` / `.AddRange(newChildren)` around the mutation (or diff against the desired set explicitly, the Phase 14/15 pattern). See `docs/phase-4-status.md`'s bug #1.
- **A child appended to an already-*tracked* parent's encapsulated collection is picked up by `DetectChanges` as `Modified`, not `Added`**, because its key is already set by its factory and the parent is itself `Modified` - so `SaveChangesAsync` throws `DbUpdateConcurrencyException: Attempted to update or delete an entity that does not exist in the store`. This is the add-only sibling of phase-4 bug #1's clear-and-re-add, and the remedy is the same: have the Domain method report what it changed and let the handler `AddRange`/`RemoveRange` through the child's own `DbSet` (see `Product.VariantUsageChanges` / `SetProductVariantAttributesCommandHandler`). Note a *new* parent is fine - `db.Products.Add(parent)` propagates `Added` through the whole graph - so this only bites when mutating something already in the store. See `docs/phase-24-status.md`'s bug #1.
- `TestAppDbContext` deliberately has **no `ApplyConfigurationsFromAssembly`**, so every encapsulated (private-backing-field) collection must be restated in its `OnModelCreating` with `HasMany(...).WithOne().HasForeignKey(...)` plus `SetPropertyAccessMode(Field)`. Forgetting one makes EF fall back to convention and mis-map the navigation, and the symptom is the **identical** `DbUpdateConcurrencyException` as the gotcha above - which is what makes the pair genuinely hard to tell apart. Check the test context first, then the tracking state; `db.ChangeTracker.Entries()` naming the offending entity is what separates them.
- A **unique index over a nullable column is not optional to think about on SQL Server**: unlike the SQL standard, it treats NULLs as *equal* to one another, so a unique index on `(OrganizationId, ParentProductId, CombinationKey)` would reject every second ordinary product (all NULL) rather than only real duplicates. Add `.HasFilter("[Col] IS NOT NULL")`. The InMemory provider enforces unique indexes not at all, so nothing in the unit suites can catch either half of this.
- `EF.Functions.Like` cannot be translated by the **InMemory provider at all**, so a `LIKE`-style search in a handler whose tests run on InMemory must be written as `String.Contains` — which SQL Server translates to the same `LIKE '%term%'` and InMemory evaluates directly. Case-insensitivity then comes from the database's collation, as it does for every other comparison in this tree.
- Before assuming a MediatR query handler's `Where` clause matches its own request's fields, actually read it — `ListPaymentsQueryHandler` shipped with a hardcoded `Direction == Received` filter that no request field controlled, invisible until Phase 6's first `Direction=Paid` row. A handler whose observed behavior always happens to match its hardcoded assumption is exactly what manual E2E misses. See `docs/phase-6-status.md`'s bug #2.
- A handler that aggregates counts **in the database** (`GroupBy(...).Select(g => g.Count())`) must run *after* the `SaveChangesAsync` that persists the statuses it is counting — an outcome still sitting in the change tracker is invisible to a store-side aggregate, so the row is counted as neither. Bit Phase 21a's job finalisation, where an interrupted row was reported as neither succeeded nor failed. Caught by a unit test only because the test asserted the failed *count*, not just the row's own status.
- A Domain entity's factory/mutator can stay `internal` only while its sole caller is another same-assembly Domain type. The moment an Application-layer service calls it directly, `internal` fails to compile across the assembly boundary — make it `public` (caught by `dotnet build`). See `docs/phase-7-status.md`'s bug #1.
- Never name a Domain type after a common BCL word — `Task`/`TaskStatus` collide with `System.Threading.Tasks` (implicitly in scope in every async handler via `ImplicitUsings`); the aggregate became `WorkTask`/`WorkTaskStatus` while every other layer keeps plain "Task" naming. See `docs/phase-13-status.md`.

- **A filter over a tree of tenant-defined groups must match on group id, never group name.** `GeneralLedgerSummaryQueryHandler`'s first cut resolved the filtered `AccountGroup`'s subtree with `ITreeQuery`, projected it to a set of *names*, and compared each account's own group name against it. Group names are not unique across a chart of accounts and nothing in the schema makes them so, so two unrelated groups both called "Other" would have silently merged into one filter result — a wrong report that looks entirely plausible. The fix was to carry `GroupId` on the projection (`GlAccountClassification.AccountFacts`) and match on the id. The general form: any time a tree helper hands you ids and you convert them to labels to do the comparison, you have thrown away the only unique key you had (phase-26a bug #1).

**`decimal` has a signed zero, and no test will catch it (phase-26c bug #1).** `-0m` keeps its sign
bit. `decimal.Equals` treats it as equal to `0m`, so every assertion passes -- but the cast to
`double` that a spreadsheet cell needs preserves the sign, and ClosedXML writes `-0`, which renders
`-0.00` on screen. The shape that produced it was an unguarded `else { credit += -balance; }` over
contacts whose balance was exactly zero. Accumulate a magnitude only when the value is *strictly*
non-zero. Found by reading a generated workbook's raw cell XML during a manual E2E, which is the only
place it was visible.

## GL posting, documents and domain invariants

- A posting rule for a "reverse of X" document type can satisfy `GlJournalEntry.Post`'s own `sum(Debit)==sum(Credit)` invariant while leaving a *paired* account permanently unbalanced across the original + reversal combined (e.g. DebitNote debiting AP gross while PurchaseBill credited AP net of TDS — AP off by the TDS amount, TDS Payable stuck forever, every test green). Trace the *net* effect on every account the original touched, across both postings, especially control accounts the reversal doesn't obviously need to know about. See `docs/phase-6-status.md`'s bug #3.
- A "reverse of X" GL posting is foolproof only if it mirrors the *original entry's own posted lines* (swap every Debit/Credit) rather than re-deriving a reversal from the document's posting rule — a hand-derived reverse can satisfy its own balanced-entry invariant while still leaving a paired account (TDS Payable, etc.) permanently unbalanced (see the gotcha above). `GlJournalEntry.PostReversalOf` posts a second entry against the same `SourceDocumentType`/`SourceDocumentId` rather than mutating the original — `GlJournalEntry` has no UPDATE/DELETE path anywhere, and every GL-reading report already sums by account with no per-document uniqueness assumption, so this needs zero report changes. See `docs/phase-16a-status.md`.
- Setting `ReferrerType`/`ReferrerId` on a conversion document enforces **nothing** by itself — without hand-written checks in the Create handler: the source converts unlimited times (needs a `Converted` status + `MarkConverted()`), a CreditNote/DebitNote can exceed source quantities or claim Rate/VatRate combos that never existed (cap by the exact `(ProductId, Rate, VatRate)` triple, net of prior non-Void reversals), and Contact/TDS-Type can drift from the source. See `docs/phase-6-status.md`'s bug #4 for the four-part fix.
- `PurchaseBillPostingRule` debited the whole purchase to a Purchase (Expense) account, never an Inventory (Asset) account, until the post-Phase-19 fix (`docs/phase-7-status.md`'s addendum) made Goods lines debit `TenantSettings.DefaultInventoryAccountId` instead — Service lines still debit Purchase Expense. A report/ratio computation that wants a live Inventory *value* must still sum `StockLedgerEntry.QuantityRemaining × UnitCost` (the same FIFO-layer valuation Stock Ageing/Product Profitability use), not `TenantSettings.DefaultInventoryAccountId`'s GL balance — that balance is now a real perpetual-inventory asset balance post-fix, but nothing re-derived Ratio Analysis's Inventory figure to read it since the FIFO-sum approach already works and needs no further change. Caught only by a live Trial Balance cross-check against a freshly seeded org, not by a unit test that happened to avoid PurchaseBill activity. See `docs/phase-19-status.md`'s bug #1.
- A GL-report handler's unit test written with fixed calendar dates (e.g. `new DateOnly(2026, 1, 1)`) against real Create/Approve-seeded documents comes back all-zero, not a thrown exception — `GlJournalEntry.PostedAt` is stamped from the real clock at Approve() time (`GlDateBoundary`'s own doc comment), never the document's own business `Date`, so a query window built from fixed past/future dates never overlaps any real `PostedAt`. Every existing GL-report test already works around this by bracketing `DateOnly.FromDateTime(DateTime.UtcNow)` — grep for that pattern before writing a new one. See `docs/phase-19-status.md`'s bug #2.
- Anything scheduled or dated for a tenant must be computed on the **Nepal wall clock (UTC+05:45)**, never on UTC — this is a Nepal-only product and `Organization` has no timezone field, so `Domain/Common/NepalTime` is the single conversion point (a fixed offset, deliberately not `TimeZoneInfo`: Nepal has had no DST since 1986, and the tz id differs between Windows and Linux so a `FindSystemTimeZoneById` miss throws on whichever platform the code was not written on). The :45 offset makes the failure mode subtle: between 18:15 and 24:00 UTC the Nepal calendar date is already *tomorrow*, so a UTC-derived "today" silently keys the wrong day. A test that only asserts an evening-UTC case passes under a naive UTC implementation by luck — assert an after-local-midnight case too (see `AlertDispatcherTests.Uses_the_Nepal_local_day_and_time_not_UTC`). See `docs/phase-20e-status.md`.
- **A FIFO layer stores a unit cost, not a value**, so the value it represents is `Quantity x Round(cost, 4)`. Any document that computes a cost by division (a production run's cost per unit, an allocation across by-products) will leave a residue whenever the division is not exact at four decimals. Build the GL entry from **the values actually created** so it balances by construction, round every unit cost to `StockLedgerEntry.UnitCost`'s own scale (`ProductionJournal.UnitCostScale`) so the document and the ledger can never disagree, and **name the residue** rather than absorbing it silently. It is bounded by `OutputQuantity x 0.00005` and is zero for any ordinary whole-quantity run.

- **`GlJournalEntry` stores no copy of its source document's number, reference or business date.** It carries `SourceDocumentType`, `SourceDocumentId` and `PostedAt` and nothing else, deliberately — those fields belong to the document. So every report that shows a Txn No or Reference No alongside a GL line has to join back, and there are **eleven** document types that post GL (grep-confirmed against every `GlJournalEntry.Post` call site: Invoice, CreditNote, PurchaseBill, Expense, DebitNote, JournalVoucher, CashTransfer, InventoryAdjustment, Payment, ProductionJournal, OpeningBalance — Quotation, SalesOrder, PurchaseOrder and WarehouseTransfer post nothing). `GlSourceDocumentResolver` does that join once, batched per type, for all three of phase-26a's line-level reports.

  The consequence that matters for correctness: a GL report **must show the same date field it filters on**. Every GL report since Phase 8a filters on `PostedAt` (the Approve-time stamp), so they display `PostedAt` too. Resolving each document's own `Date` for display while still filtering on `PostedAt` would produce rows that visibly fall outside the range printed at the top of the report. Moving the whole family onto document dates is a coherent change; doing it for one report is not (phase-26a Decision B).

- **Two reports that must agree should read the same code, not derive the same figure twice.** Invoice Age's total balance and Customer Receivable Summary's closing balance are the same number, and they are the same number *by construction* because both go through `ContactLedgerReader` rather than each netting documents its own way. That is also why phase-26b extended that reader instead of teaching one report about Journal Vouchers: `JournalVoucherLine.ContactId` had existed since phase 17 with nothing reading it back, so a JV posted to a customer moved the general ledger without moving that customer's Statement. Adding it fixed Contact Statement and Contact Overview as a side effect — a deliberate, documented change to two shipped screens, not a quiet one (phase-26b Decision B).

  The mirror-image rule: an ageing report's **age runs from the Due Date**, but only `Expense` stores one in this codebase and no Contact carries a credit term, so Due Date equals the document date on Invoice and PurchaseBill rows. That is phase-9's dropped "Credit Term" column reached from the other side, and it is stated on the screen rather than papered over (phase-26b Decision C).

- **A report column that is not additive must not have a footer total.** phase-16c's rule is that a footer total covers the whole filtered set rather than the current page — but the Transaction list's Amount column crosses document types, where an Invoice's gross, a Journal Voucher's debit side and an Inventory Adjustment's value are not the same unit of account. Summing them produces a number with no meaning that a reader will nonetheless believe. Both the Transaction list and `RecentTransactionsQuery` therefore have no footer at all, which is stated in their doc comments so a later phase does not "fix" the omission (phase-26a Decision D).

**A dated stock report must derive from `StockMovement`, not from `StockLedgerEntry` (phase-26c).**
`StockLedgerEntry.QuantityRemaining` is decremented **in place** as later documents consume a layer,
so the FIFO table only ever describes stock as it stands right now. A report whose header says "for
the period ... to 30 Bhadra" and whose Balance column silently answered "as of today" is wrong in the
one case a reader most needs it -- reopening a closed period. `StockMovement` is append-only and
carries the consuming document's own weighted-average unit cost, so Opening + In - Out reconstructs
both quantity and value at any date, and equals the FIFO remaining value at today's date. That last
equality is what CLAUDE.md's older "a live inventory value comes from `QuantityRemaining x UnitCost`"
gotcha is really asserting; phase-26c's E2E proved all three figures identical (123 units / 1,330.00)
against the real database. `Inventory.Reports.StockFactReader` is the one place this is implemented.

**Stock cannot go negative in this codebase, so don't test the branch that handles it (phase-26c).**
`StockLedgerService.ConsumeAsync` *throws* a 409 when a document would consume more than the layers
hold. The reference product instead offers a "Negative Item Balance" setting (Reject / Warn / Do
Nothing) and its own tenant runs warn-and-allow, which is why its Inventory Position has hundreds of
negative rows and ours can have none. `StockFactReader` still guards the case -- a negative balance
reports zero value, because there is no cost to carry for goods that are not there -- and a test pins
the *throw*, naming in the test's own title that this is why the guard is unreachable. Otherwise the
guard reads as dead code and gets deleted before the setting that needs it is built.

**A Debit Note line carries no `ExpenditureClassification` and no `IsImport` of its own
(phase-19, phase-26c).** Both are resolved from the source Purchase Bill's matching line, keyed by
`(PurchaseBillId, ProductId, Rate, VatRate)` -- the same join `AnnexThirteenReportQueryHandler` uses.
A standalone debit note with no referrer falls back to Others/local. `PurchaseReturnReader` owns this
now, and both the Purchase Register and the Purchase Return Register read it, so the two cannot
report a return in different statutory columns.

**A currency conversion belongs on a posting rule's *inputs*, never on its finished lines
(phase-28).** Every posting rule in this codebase derives its balancing leg as a *sum of the other
legs* -- `InvoicePostingRule`'s AR line is revenue + VAT, `PurchaseBillPostingRule`'s AP line
likewise. Convert the already-built `GlLineInput` list and that balancing leg gets rounded
independently of the legs it balances, so `Round(T x r)` can differ from `Sum(Round(a_i x r))` and
`GlJournalEntry.Post`'s sum(Debit)==sum(Credit) invariant fails -- **intermittently**, for some rates
on some documents, which is the worst possible failure mode. Two 0.05 debits against one 0.10 credit
at rate 1.5 give 0.08 + 0.08 = 0.16 against 0.15. Converting the *inputs* (the line amounts the
account resolver is handed) keeps every entry balanced by construction, because the rule then sums
the very numbers it posts. `Domain/Common/ExchangeRates` is the one conversion point.

Two document types cannot take that route -- `JournalVoucher` and `CashTransfer`, whose rules take
the domain aggregate itself and so have no line-amount argument. They go through
`GlCurrencyConversion.ToBaseAsync`, which converts the finished list and **books the residue to the
tenant's forex account** rather than absorbing it (phase-25's name-the-residue rule). It is bounded
by half a paisa per line and is exactly zero for most entries, in which case no forex leg is added
and no forex account is required.

**Nothing already denominated in the base currency may be converted a second time (phase-28).**
`StockLedgerEntry.UnitCost` is written in base currency at receipt, so an Invoice's COGS leg is
already base and converting it again would double-apply the rate. The one place a document's own
rate reaches the stock ledger is `ApprovePurchaseBillCommandHandler`, and it uses
`ExchangeRates.ToBaseUnitCost` (four decimal places, matching every `UnitCost` column) rather than
`ToBase` (two) -- rounding a foreign unit price to paisa loses real precision on cheap goods and
then multiplies it by every quantity ever received. CreditNote and the Void paths re-increment from
a stored `CogsUnitCost`/`ConsumedUnitCost`, which is already base.


## Background jobs

- A singleton `BackgroundService` **cannot inject a scoped service** (`IAppDbContext` and `IEmailSender` are both `AddScoped` here) — injecting one either fails at startup or pins it for the process lifetime. Take `IServiceScopeFactory` and create a scope per tick. Two companions to the same rule: read options through `IOptionsMonitor`, not `IOptions` (see the caching gotcha below — a long-lived singleton is exactly what it bites), and never let a tick's exception escape `ExecuteAsync`, or the loop stops for the rest of the process's life while the app keeps serving HTTP perfectly happily. See `AlertSchedulerHostedService`.
- **An optimistic-concurrency token (`IsRowVersion()`) on a row that a background job writes repeatedly is a trap whenever a *user* can also write that row.** SQL Server bumps a rowversion on any UPDATE, so a user action against a job row (Phase 21a: cancelling a running import) invalidates the runner's in-memory token and its very next progress write dies with `DbUpdateConcurrencyException` — leaving the job wedged mid-run until its lease expires, with the app still serving HTTP perfectly happily. **The InMemory provider does not enforce concurrency tokens at all**, so no unit test can reach this; it surfaced only in manual E2E. Before adding a token, ask who *else* writes the row. In `ImportJob`'s case the answer was to delete it: job-level claiming was never the correctness mechanism — `ImportJobRow`'s `(ImportJobId, RowNumber)` unique index is, exactly as `AlertSendLog`'s index (not any lock) is what makes a send happen once. Two runners on one job then merely duplicate *effort*, never a record. See `docs/phase-21a-status.md`'s Decision C, Bug 1.
- A background job that **writes** cannot use Phase 20e's "send no MediatR request" escape hatch, because every rule about creating a record correctly (numbering, FK checks, validation, audit) lives in the Create/Update handlers. Phase 21a's answer: reuse the commands and re-establish the *initiating user's* identity through a scoped `IJobActingUser` that `CurrentUserService` consults **only when there is no `HttpContext` at all** — an HTTP request's JWT wins unconditionally, so a job identity can never serve a request even if something in that scope called `Assume`. The payoff is that `AuthorizationBehavior` then **re-checks the permission on every row at execution time** for free (a user whose grant was revoked between enqueue and run has the job stopped), and `AuditBehavior` attributes every imported record to them. Note the corollary: a feature-level `*.Manage` key does **not** replace the per-entity key the rows still exercise.
- A background job that needs to do something exactly once must **write its claim row and commit it before performing the external side effect**, under a unique index on the occurrence key — not after, and not "check then act". That one ordering is simultaneously the idempotency-across-process-restart mechanism, the multi-instance mechanism (the losing instance's insert violates the index; catch `DbUpdateException`, detach the entry, skip) and the at-most-once guarantee. Note the InMemory provider **does not enforce unique indexes**, so the race path is unreachable in unit tests and has to be verified against real SQL Server — the already-claimed pre-check is what the tests can cover. See `AlertSendLog`/`AlertDispatcher` and `docs/phase-20e-status.md`'s Decision C.
- **Nothing in this codebase deleted a file from `IFileStorage` until Phase 21b** — a grep for `DeleteAsync` found exactly one caller (`DeleteAttachmentCommandHandler`), so Phase 21a leaked every uploaded import workbook, permanently and silently. Any feature that writes a blob needs a deletion story decided *with* it, not after: for a full-tenant export the artifact's lifetime is a security posture, not housekeeping. The mechanism that exists now is `IQueuedJobProcessor.SweepAsync` (a default-no-op interface method the shared runner calls once per tick, before draining, in its own scope and its own try/catch) plus `JobArtifactRetention.Period` — reuse it rather than adding a background service for one DELETE. **Order matters: delete the blob first, stamp the row second.** A crash between the two re-sweeps the row and deletes an already-deleted file, which `IFileStorage` treats as a no-op; the reverse ordering strands the file forever. See `docs/phase-21b-status.md`'s Decision E.
- A background job that **produces** a file must commit the storage key and the terminal status in **one** `SaveChangesAsync`, and build the file into a buffer before either. That single ordering is what makes "a run that died mid-write" indistinguishable from "still running" to every reader — the job simply has no key, so no UI can offer a half-written download. The residue is at worst one orphaned blob (saved, never committed), which is a cost rather than a correctness failure and is not worth a two-phase protocol. Note the UI must gate its Download control on *"an artifact exists"* (`StorageKey != null && ArtifactPurgedAt == null`), never on `Status == Completed` — the two diverge the moment retention runs, and a whole-page text search for "Download" in a component test passes vacuously if the same screen has any other download button. See `ExportJob.HasArtifact` and `import-page.spec.ts`.

## Files, ClosedXML, uploads and downloads

- ClosedXML's (and any other sync-only writer's) `SaveAs(Stream)` cannot target a live ASP.NET Core response stream directly — Kestrel disallows synchronous writes there by default and throws `InvalidOperationException: Synchronous operations are disallowed`, surfacing only as a generic 500 unless you check the server's own console log (an InMemory-provider unit test never touches a real Kestrel response, so nothing catches this except manual E2E against the real server). `SaveAs` into a `MemoryStream` first, then `CopyToAsync` that buffer to the real response stream. See `docs/phase-16c-status.md`'s bug #3 and `ReportSpreadsheetExporter.WriteWorkbookAsync`.
- A Minimal API endpoint that binds an `IFormFile` parameter gets antiforgery metadata attached automatically by ASP.NET Core, even though nothing about the endpoint asked for it — every request 500s with `InvalidOperationException: ... contains anti-forgery metadata, but a middleware was not found` unless `app.UseAntiforgery()` is registered (it isn't, anywhere, in this app — CSRF mitigation here is the explicit CORS origin allow-list plus the httpOnly JWT cookie, not antiforgery tokens) or the endpoint opts out explicitly with `.DisableAntiforgery()`. Surfaces only via manual E2E against the real server (an InMemory-provider unit test never touches real Minimal API endpoint metadata). See `docs/phase-18-status.md`'s bug #1 and `AttachmentsEndpoints.cs`'s upload route.
- Executing a file-download `IResult` (`Results.Stream`, and anything else built on `PushStreamHttpResult`) against a bare `DefaultHttpContext` throws `ArgumentNullException (Parameter 'provider')` before a single byte is written: it resolves an `ILoggerFactory` from `HttpContext.RequestServices`. Give the context a real `ServiceProvider` with `AddLogging()` — see `MigratedRegisterTemplateRoundTripTests` (Phase 21c), which is how the generated .xlsx template and the parser that reads it back are proven not to have drifted.

- A `MultipartFormDataContent` built under `using` in a helper that **returns** the `Task` instead of awaiting it is disposed before the send completes, and `WebApplicationFactory`'s `TestHost` then throws `ObjectDisposedException: Cannot access a closed Stream` from `MultipartContent.ContentReadStream.set_Position` — a stack trace naming nothing about the real cause. Await inside the helper. Bit three integration tests at once in Phase 22.
- ClosedXML silently returns **empty text for a hand-rolled `t="inlineStr"` cell**, and ignores `<si>` entries past a stale `uniqueCount` on `<sst>`. Both matter only when *generating* a test .xlsx by hand: the symptom is a file whose headers "aren't there" (or whose new values come back blank) with no error anywhere. Build import fixtures by filling the app's own generated template — parts written by ClosedXML round-trip through ClosedXML — rather than synthesising a package from scratch. See `docs/phase-21a-status.md`'s testing section.
- ClosedXML's `AdjustToContents()` measures **every cell it is given**, so calling it on a 25,000-row sheet costs more than writing the sheet did; size columns over the header band plus a sample of the first rows instead (`worksheet.Columns(first, last).AdjustToContents(startRow, endRow)` — note there is no `AdjustToContents` overload on `IXLRangeColumns`, only on `IXLColumns`). The same buffering that makes this expensive is why the write side needs a stated row cap at all: `XLWorkbook` materialises every cell of every sheet before a byte is written, exactly as it materialises a whole sheet on read (phase-21a's 5,000-row read cap). State the cap and disclose truncation in the artifact itself; do not pretend to stream.
- A date column in an import template needs an **explicit format list with day-first ahead of month-first**, never a bare `DateTime.TryParse`. `07/08/2024` is a real date under both readings, so the wrong default silently imports the wrong month — in statutory data, with no error anywhere and nothing to reconcile it against. See `ImportRowReader.GetOptionalDate` (Phase 21c) and assert the ambiguous case explicitly; a test that only uses ISO dates passes under either implementation.

- **A trailing optional parameter added to a command does not reach the wire until the Api's own request record carries it too, and nothing fails to compile.** Phase 27b added `Terms` to five aggregates, their commands, handlers, EF configs, a migration and the Angular models — `dotnet build`, `dotnet test`, `tsc --noEmit` and `ng build` were all clean — but `SalesEndpoints`/`PurchasingEndpoints` map a separate `InvoiceRequest`-style record onto the command by hand, and those records had no `Terms`, so every request silently bound the default `null`. **Handler unit tests cannot see it** (they construct the command directly), and the optional parameter is exactly what removes the compiler's help. It surfaced only on a `GET` after a real `POST` in the manual E2E. When adding a field to a command, grep the Api endpoint's request record in the same commit.

## Angular

- An Angular component that handles both a `.../new` create route and a `.../:id` edit route (same route path, e.g. `contacts/:contactId`) must read the id from `route.paramMap` (an `Observable`) and re-derive its "is this new" state on every emission — Angular's default route-reuse strategy keeps the same component instance alive across that navigation, so a value captured once from `route.snapshot.paramMap` in the constructor goes stale the moment Create redirects to the new record's own URL (the page silently keeps showing the create form). See `docs/phase-3-status.md`'s bug #1.
- A `HttpClient.get<T>(url, { params })` call where `params`'s inferred type is a union including `{}` (e.g. `const params = cond ? { foo } : {};`) can fail TypeScript's overload resolution silently into the `responseType: 'arraybuffer'` overload, surfacing as a confusing `Observable<ArrayBuffer>` type error far from the real cause. Annotate `params: Record<string, string>` explicitly. See `docs/phase-3-status.md`'s bug #4.
- Assigning a ternary-chosen `request$: Observable<A> | Observable<B>` (Create vs Update, e.g. `const request$ = editingId ? updateX(...) : createX(...);`) fails the same way when `A`/`B` have different shapes — `.subscribe()` becomes `TS2349: This expression is not callable`, pointing at the call site rather than the real cause. Use an explicit `if (editingId) {...} else {...}` branch instead of a shared `request$` variable when the two result types differ. See `docs/phase-4-status.md`'s bug #3.
- A native `<select>`'s `[value]` property binding can silently lose to its own `@for`-generated `<option>` children when both are created in the same change-detection pass — the browser can't match the value, silently falls back to `selectedIndex = 0`, and the on-screen selection diverges from the bound data. Not limited to freshly-created `@for` rows: a static top-level select whose value-signal and options-signal resolve via two independent async subscribes hits it too, and Angular's signal reactivity never retriggers the `[value]` write. Confirmed in Phase 7 to cause **actually-wrong persisted data** (an Invoice's real `WarehouseId` and FIFO consumption differed from the warehouse shown on screen). Fix: bind `[selected]="option === boundValue"` per `<option>`, never `[value]` on the `<select>`. See `docs/phase-5-status.md` bug #1, `phase-6-status.md` bug #1, `phase-7-status.md` bug #2.
- A footer/summary total on any paginated screen must be computed server-side from the *full* filtered result, never a client-side `.reduce()`/sum over the currently-loaded page — the moment a list becomes paginated, the client only holds one page, so a pre-existing client-side total silently starts showing a page subtotal with no error and no failing test. Four report pages had exactly this latent bug, caught only by re-reading the Angular templates before assuming pagination was purely a backend change. See `docs/phase-16c-status.md`'s bug #1.
- This Angular app is **zoneless** (Angular 21 default — confirmed by no `zone.js` in `web/package.json`). A `computed()` signal only re-evaluates when a tracked *signal* it read during its last evaluation changes; wrapping a read of a plain (non-signal) mutable value inside `computed()` — most commonly `FormControl.value` from a Reactive Forms group — silently caches the first result forever, since the computed has no signal dependency to invalidate it on. A direct (uncached) template read of the same `form.controls.x.value`, by contrast, works fine — zoneless change detection still reruns the template function after an Angular-bound DOM event (`(change)`, `(click)`, etc.), so a plain property read comes back fresh every time. `tsc`/`ng build` cannot catch this class of bug; it only surfaces as a UI element silently never updating, caught in Phase 17 only by live browser testing (`quick-payment-page`'s Cheque Details section never appearing after selecting a Cheque-mode Payment Mode). Fix: track the value driving conditional UI in its own plain `signal()`, written directly by the control's `(change)`/`(input)` handler, rather than deriving it from the FormGroup inside a `computed()`.
- **Bootstrap's JavaScript is not loaded anywhere in this app** — `web/angular.json` registers `src/styles.scss` and has no `scripts` entry at all, so `data-bs-toggle="dropdown"` (and modal/tooltip/collapse likewise) renders a control that silently does nothing, with no error and a passing `ng build`. Drive such menus from a signal instead (see `document-inbox-page`'s "+ Add as").
- A Bootstrap `.dropdown-menu` rendered inside a `.table-responsive` is **clipped**: `overflow-x: auto` makes the browser compute `overflow-y: auto` too, so an absolutely-positioned menu is cut at the wrapper's edge. Every item is still in the DOM, so `ng build`, `tsc` and a component test asserting the items exist are all green while the user can only reach the first one. Render such a menu `position: fixed` at coordinates captured from its trigger on open (see `document-inbox-page`'s "+ Add as"). Caught only in Phase 22's browser pass.
- An Angular pipe that renders from a **global signal** while taking an argument that does not change must be **impure** (`pure: false`). A pure pipe caches on its argument, so a date interpolation would keep serving the AD rendering forever after the user flips the app-wide calendar toggle -- the same silently-stale shape as the zoneless-`computed()`-over-`FormControl` gotcha above, and equally invisible to `tsc` and `ng build`. Memoize inside the pipe to pay for the extra calls (see `NepaliDatePipe`).
- **`Domain/Common/BsCalendar` is a verbatim port of `web/src/app/shared/formatting/bs-date.ts`, and must stay one.** Phase-23 put the whole Bikram Sambat conversion on the client because dates are stored in AD and BS was presentation only; phase-26b broke that, because five reports are keyed by a BS *fiscal year* and return one row per BS *month*, so the grouping itself is a calendar operation and can only happen where the rows are grouped. The month-length table was generated out of the TypeScript file rather than retyped — a transcription slip would be silent, permanent, and land in a filed return — and `BsCalendarTests` re-asserts the same three families of anchor `bs-date.spec.ts` uses plus a round trip over all 33,969 days in range. **A fiscal year runs Shrawan 1 (BS month 4) to the last day of Asar and is named by its first BS year**, so "2083 - 2084" spans two BS years and the last expressible one is `LastYear - 1`. Extending the range means editing both tables and bumping both boundary tests (phase-26b Decision E).
- Angular's expression parser accepts a pipe **inside parentheses** anywhere, including a ternary branch, because `parsePrimary` calls `parsePipe()` for a parenthesised primary -- but the same pipe bare in a ternary branch does not parse. That is what makes a scripted `EXPR.toFixed(2)` to `(EXPR | amount)` sweep safe across 324 call sites without hand-inspecting each ternary. Pipes remain illegal in event bindings (`(click)`/`(change)`) regardless.
- A component test asserting an **uppercase label** fails when the uppercasing comes from CSS (`text-uppercase`) -- `textContent` carries the source casing, not the rendered casing. Assert what the template literally contains.
- **A report page can gain real data in its DTO and still show the user nothing.** `SalesRegisterQuery` carried four Export columns from Phase 19 that the handler hardcoded to zero/null; when Phase 23 filled them, the Angular table had no header or cell for them, because nobody adds columns that are always empty. Every automated check was green and the feature was invisible. When a phase starts populating a previously-dead field, grep the templates that consume it before calling it shipped. See `docs/phase-23-status.md`'s bug #1.
- Angular sanitizes an `<iframe [src]>` as a *resource* URL and blocks an interpolated string outright, while `<img [src]>` with the identical string is fine. An inline PDF preview therefore needs `DomSanitizer.bypassSecurityTrustResourceUrl`; nothing catches this until the element actually renders. See `SourceDocumentPanel`/`InboxConversionPanel` (Phase 22), where the URL is built only from the API base plus a route-parameter GUID, which is what makes the bypass safe.

- `AmountPipe` renders **exactly two decimals**, so any figure legitimately smaller than a cent renders as `0.00` -- a labelled row showing `0.00` reads as a defect rather than a disclosure. It takes an optional precision argument (`| amount: 4`), added in Phase 25 for the production cost roll-up's rounding residue; the default is unchanged, so phase-23's 324 call sites are unaffected. Caught only by the browser pass.

- **A background job needs an acting identity when it sends a MediatR request, not when it writes.**
  Phase 20e established "a job needs no ambient identity"; phase 21a gave `IJobActingUser` to the
  importer *because it writes*; phase 21b's exporter reads and needs none. Phase 30's email job reads
  too -- and still needs one, because it renders the attached PDF through `PrintDocumentQuery`, which
  is permission-gated, and `AuthorizationBehavior` has no `HttpContext` inside a job. Symptom if you
  get it wrong: every "Attach PDF" send fails *inside its own job*, which surfaces as a `Failed` row
  with an authorization message and gets diagnosed as an SMTP problem. The remedy is
  `IJobActingUser.Assume(row.SentByUserId)` before the render -- which also re-checks at render time,
  so a sender who lost access between queueing and sending gets a recorded failure rather than a
  mailed document. See `EmailSendJobProcessor` and phase-30's Decision H.
- **Phase-21a's "no concurrency token on a row a job writes" is not a blanket ban -- it is a rule about
  rows with two writers.** `ImportJob`/`ExportJob` have a second legitimate writer (the user's cancel
  command), so a rowversion bumped by either wedges the other. `EmailSendLog` has exactly one writer
  after creation (nothing edits a send; a resend is a new row), so it carries a rowversion and gets
  real compare-and-set for its claim, which those two had to do without. Ask how many writers the row
  has, not whether a job touches it (phase-30 Decision I).
- **Do-exactly-once and "a resend is a new row" need an *intent* key, not an occurrence or a content
  hash.** Phase 20e's occurrence key (definition, date, recipient) needs a schedule; a user-initiated
  send has none. A content hash would silently swallow the legitimate second send a customer asks for
  after saying "I never got it". The key that separates them is a **request id minted by the client
  when the composer opens**, under a unique index: submitting it twice (double-click, retry of a
  response never seen) is one row, and reopening the composer mints a fresh one. Note the winner's
  row is returned even when the duplicate carried different content -- the first intent wins
  (`EmailSendLog`, phase-30 Decision D).

## Testing and manual E2E

- A third-party verification API's *test/dummy* credentials that "always pass" (e.g. Cloudflare Turnstile's documented `1x0000000000000000000000000000000AA` secret key) accept literally any input value, not just well-formed ones — hitting the real endpoint with a bogus token under that secret still returns `success: true`, so it cannot be used to prove a server-side check actually *rejects* a bad token. Proving the negative path needs the vendor's matching *always-fails* dummy credential (Turnstile: `2x0000000000000000000000000000000AA`) swapped in temporarily. See `docs/phase-20g-status.md`.
- **A Goods line consumes stock whether or not the product tracks inventory.**
  `ApproveInvoiceCommandHandler` selects its `goodsLines` by `ProductType == Goods`, never by
  `TrackInventory`, so on a tenant *without* the Track Inventory feature a Goods product cannot be
  invoiced at all: seeding opening stock is refused `403` (feature off) and approving is refused
  `409` (`ConsumeAsync` throws on the oversell). A manual E2E that just needs an approved sales
  document should use a **Service** line. Found in phase 30's E2E; recorded there as a follow-up.
- **curl cannot read a file for `-F` upload in this sandbox** -- every path form (drive-letter,
  POSIX, relative, inside the repo) returns exit 26 with HTTP status `000`, which reads like a server
  problem and is not one. Multipart *without* a file works fine. Drive the file leg from a short
  Python `urllib` script instead (phase 30's `send_email.py`), transplanting the cookie jar's
  `erp_auth` value into a `Cookie` header.
- `dotnet run --project src/Api` with no `--launch-profile` binds **http://localhost:5155 only**, not
  the https 7104 the Angular dev environment points at. Pass `--launch-profile https` for anything
  the browser has to reach; and if a previous run is still holding 5155 the https profile fails to
  start with `address already in use` (both profiles bind it), so kill the old listener first.
- `UpdateRolePermissionsCommand.Grants` is an `IReadOnlyDictionary<string, bool>`, not a list of objects - posting an array yields a bare `400 Failed to read parameter ... as JSON` naming nothing useful. And **the built-in Admin/Member roles are system roles whose grants cannot be edited** (409), so a negative-permission E2E proof has to create a custom role and move the membership onto it, not revoke from Admin.
- **A browser pass is possible in a non-interactive session, and here is how** (this is what kept four screens unlooked-at across phases 21b/21c/22): the auth cookie is `HttpOnly; Secure; SameSite=None`, so the SPA must be served over HTTPS with a certificate the pane already trusts -- a self-signed `ng serve --ssl` cert is refused, the ASP.NET dev cert is not. Export it (`dotnet dev-certs https --export-path .certs/dev.pem --format PEM --no-password`), start the `erp-web-ssl` launch profile, then transplant the session curl already established: `document.cookie = "erp_auth=<token>; path=/; secure; samesite=none"`. Cookies ignore port, both origins are HTTPS, so it is same-site and reaches the Api. No credentials are typed into any form.

- **Map one enum onto another by name, never by ordinal, and add a test that says so.** The Transaction list unifies thirteen per-document status enums into one `TransactionListStatus`. They are *not* identical — only Quotation and PurchaseOrder have `Converted` — but all thirteen happen to share ordinals today, so `(TransactionListStatus)(int)x.Status` compiles and works. It would start reporting the wrong status the first time anyone *inserts* a member into one of those enums, with nothing to catch it. The handler uses `Enum.Parse`/`Enum.TryParse` on the name instead, and `TransactionListQueryHandlerTests` asserts every member of all thirteen enums has a counterpart in the shared one, so the failure mode is a red test rather than a wrong number (phase-26a Decision E).

**A seed script that pipes approvals to `/dev/null` hides its own failures (phase-26c bug #2).** Two
real traps cost a full seed run each, both invisible: `POST /api/organizations` returns
`organizationId`, not `id`; and the accounting *and* inventory GL defaults are **one**
`PUT /api/organizations/{id}/accounting-defaults` taking all eleven accounts, not two endpoints. The
approvals came back 409 ("Default Inventory account is not configured") and the first report simply
returned an empty list, which reads exactly like a handler bug. Print every approval's status code:
`-w " pb1:%{http_code}
" -o /dev/null`.

**Driving the reference product's UI needs coordinates, not refs (phase-26c).** Its accessibility
tree is nearly empty -- custom `div`s with no roles -- so `find` matches nothing and `read_page`
returns three anonymous generics. Read `getBoundingClientRect()` through `javascript_tool` and click
by coordinate, converting with the screenshot's own scale factor. And its **GENERATE control's DOM
text is "Generate"**: the capitals come from CSS `text-transform`, so a case-sensitive query for
"GENERATE" finds zero elements. That is phase-23's component-test casing gotcha met from the other
side.

- **A registered user has no verification code until `POST /api/auth/request-verification-code` is called.** Registration creates the user only, so an E2E that needs a second identity must call that endpoint, read the code from `[identity].VerificationCodes` with `sqlcmd`, then `verify-email` before `login` will return anything but 403. Worth the four extra calls: a **Member-role user is the only way to prove a document-scoped 403**, since the Admin role is seeded with every key.
- **A test suite that passes with *fewer* tests than the last run is a failure.** A Python patch script that sliced from an inserted block to the end of the enclosing `describe` deleted four specs in phase 27b; `ng test` went green at 163 where 167 was expected, and only comparing counts across runs caught it. Check what a rewriting script produced by counting it, not by whether the build is green. The same script silently converted CRLF to LF on every file it touched — invisible in `git diff` because `core.autocrlf` normalizes it, but a real change on disk.

- `identity` is a reserved word in T-SQL, so `SELECT ... FROM identity.VerificationCodes` fails with "Incorrect syntax near the keyword 'identity'" -- it needs `[identity].VerificationCodes`. Every E2E that proves a document-scoped 403 needs a Member-role user, and every Member-role user needs its verification code read out of that table, so this is on the path of every future negative-path proof (phase-28).
- `tsc --noEmit -p tsconfig.json` does **not** typecheck the Angular app in `web/` -- it returned clean while `ng build` reported 22 `TS2339` errors for properties that did not exist on the models. `ng build` is the check that actually covers `src/app`; treat the `tsc` line in CLAUDE.md's exit bar as the weaker of the two (phase-28).
- A wrong field name in a curl-seeded request body yields a bare `400` with no hint, and the *next* request then fails with "Failed to read parameter ... as JSON" -- because an empty `$VAR` was interpolated where a Guid was expected, and it is the Guid, not the JSON, that fails to bind. Read the Api's own request record rather than guessing its field names (phase-28: `CreateAccountRequest` takes `GroupId`, `CreateProductRequest` takes `CategoryId`/`PrimaryUnitId`/`ReOrderLevel`).

## Tooling and shell

- **`nvm use` from a shell that cannot create the symlink silently breaks Node entirely.** nvm-windows reports "Now using node v24.11.0" and exits 0 while `C:\nvm4w\nodejs` is left deleted, so `node` vanishes from `PATH` for every subsequent command. Recreate it without elevation with a junction: `cmd /c 'mklink /J "C:\nvm4w\nodejs" "%LOCALAPPDATA%\nvm\v24.11.0"'`.
- A `cat > file <<'EOF'` heredoc in the Bash tool is **silently truncated or mis-parsed**, and the symptom is `unexpected EOF while looking for matching ''` with the file never written. Originally recorded as a ~8 KB limit; phase-26a hit it twice on quoted heredocs of roughly 6–7 KB, so treat the threshold as unreliable rather than as a number to stay under. Use the Write tool, or write a small Python patch script to the scratchpad and run it.
- **A code generator that emits Angular templates through `str.format` must escape braces in the format string but *not* in a substituted value.** Phase-26b generated its four crosstab templates from a shared table fragment; the label-cell fragments were pre-escaped `{{{{ row.x }}}}` as though they would be formatted, but they were passed *as values*, so `format` never touched them and the literal quadruple braces shipped into the HTML. The build fails as `NG5002: Unexpected closing block` pointing at a line far below the real one. Generating repetitive Angular pages is still the right call for a mirrored pair — but compile the output before moving on, not after writing all thirteen.
- **`.claude/launch.json`'s `erp-web-ssl` runs `npm --prefix web`, so its working directory is `web/`.** Its cert paths are therefore relative to `web/`, while phase-25's documented export command (`dotnet dev-certs https --export-path .certs/dev.pem …`) writes to the repo root — the profile started, built the whole app, and only then died with `ENOENT: … web\.certs\dev.pem`. Fixed in phase-26a by pointing the entry at `../.certs/dev.pem`, so the documented recipe works as written.

## A capitalised cost and the reversal that has to give it back (phase 29)

`IStockLedgerService.ConsumeAsync` returns the weighted-average cost of the layers it actually
consumed, which since phase 29 includes any additional cost capitalised into them at purchase.
`DebitNotePostingRule`, however, credits Inventory each line's **Amount** - the price the goods are
being returned at. Before landed cost the two agreed for any undiscounted bill, so nothing showed;
afterwards they differ by exactly the freight and duty sitting in the returned units, and Inventory
would drift permanently above the FIFO ledger, one return at a time.

The fix is a second pair of legs on the Debit Note - credit Inventory the released share, debit the
Landed Cost Clearing account - matched back to the source bill's own line on the
`(ProductId, Rate, VatRate, DiscountPct)` quadruple every other purchase-return path keys on, and
proportional to the quantity returned. It is exactly zero for a bill with no Additional Cost section,
so no pre-existing behaviour moves.

The general rule is phase-6 bug #3's, restated: **when you change what a posting rule debits, trace
the net effect on every account across the original entry and every reversal of it** - a Void, a
Debit Note, a Credit Note. A rule that mirrored yours before your change does not necessarily mirror
it after. Void was fine here only because `PostReversalOf` mirrors the entry's own posted lines
rather than re-deriving them from the rule (phase-16a), which is precisely why that design was chosen.

## An account the server requires and no screen can set (phase 29)

Phase 25 added `DefaultProductionCostAccountId` and phase 28 added the two forex accounts to
`TenantSettings`, the command, the query and the API request record - and to nothing in `web/`. The
Accounting Defaults screen still offered ten accounts, so three server-side requirements had no way
to be configured through the application at all; the only route was a raw `PUT`. Nothing failed,
because each is resolved lazily and the E2E scripts set them over curl.

This is phase-23 bug #1 in reverse - there, a DTO carried fields no template rendered; here, an API
accepts fields no screen sends. Same remedy: **when a phase adds a tenant default, grep `web/` for
the field name before calling the phase done.** Phase 29 needed a fourth account and closed all four.

## A setting nothing can write (phase 31)

`TenantSettings` has carried five behaviour switches since phase 2 — Suggest Selling Price, Product
Price Basis, Inventory Tracking Mode, Negative Cash Balance, Negative Item Balance. Phase 31 opened
the roadmap expecting to *enforce* three "dead" ones and found something worse: **four of the five
had no command, no endpoint and no Angular screen at all**, so no tenant could ever have changed
them, and the fifth was in a subtler trap — `NegativeStockBalanceAction` was genuinely *read* by
`FifoStockAvailabilityPolicy`, which made it look shipped, while still being permanently stuck on
the value `TenantSettings.CreateDefault` seeded.

Phase 29 already recorded the field-with-no-screen version of this (three GL default accounts the
API required and nothing could set). The wider rule is: **a tenant-level field is reachable only if
you can name the command that writes it and the screen that calls that command.** A field being read
by a handler proves the read path, not the write path, and the read path is the half that makes the
gap invisible — the feature demonstrably "works", at exactly one value, forever.

The remedy in this phase was to build `Get`/`UpdateGeneralSettingsCommand` and the Configurations >
General screen *first*, and treat every enforcement afterwards as downstream of it.

## Two confirmable warnings, one document (phase 31)

Phase 7 gave Invoice Approve a Warn-and-allow flow: a 422, and an `OverrideWarning` flag the client
resubmits with. Phase 31 added a second warning (Crossed Credit Limit) to the same command, and an
invoice can trip both.

Widening `OverrideWarning` to cover both would have been one line and silently wrong: the client
shows the stock dialog, the user presses Continue, the resubmit carries `OverrideWarning=true` — and
the credit-limit breach is waived without ever having been displayed. The reference product shows
two dialogs, each with its own Dismiss/Continue, precisely because they are two decisions.

So each warning gets its own exception type and its own override flag, and the 422's ProblemDetails
carries a **`warningKind`** extension (`StockAvailability` | `CreditLimit` | `NegativeCashBalance`).
The client sets only the flag matching the kind it was just shown, and the second dialog appears on
the next attempt. `extractWarningKind` is deliberately null for any status other than 422 and for a
422 with no kind, so an unknown warning is never mistaken for a known one.

## Adding a NOT NULL column to a populated table (phase 31)

`dotnet ef migrations add` scaffolded `Invoice.DueDate` as:

```csharp
migrationBuilder.AddColumn<DateOnly>(
    name: "DueDate", schema: "sales", table: "Invoices",
    type: "date", nullable: false, defaultValue: new DateOnly(1, 1, 1));
```

Two faults in one call. Every historical invoice acquires a due date in the **year 1**, which the
ageing reports then age from; and SQL Server keeps that literal as a **named default constraint** the
model knows nothing about, so a later insert can silently take it.

Hand-rewritten as three steps — add nullable, `UPDATE [sales].[Invoices] SET [DueDate] = [Date]`,
then `AlterColumn` to NOT NULL. The backfill value is not arbitrary: "the document's own date" is
exactly what `DocumentAgeQueryHandler` and `ContactAgeingSummaryQueryHandler` were already
improvising for these two types, so no report's numbers move for historical data.

CLAUDE.md's existing rule says to hand-review a migration that *replaces or retypes* a column. This
extends it: a migration that merely **adds** a non-nullable column to a table with rows in it needs
the same read.

## A permission that depends on what you asked for (phase 31)

Phase 27a's `AttachmentAccess` established the shape: when the real permission key depends on a
column of the row the handler is about to load, `IRequirePermission.PermissionKey` cannot express it
(that property is evaluated before the handler runs), so the request declares a blanket key to get
through `AuthorizationBehavior` and the handler re-checks the real one via
`GrantedPermissionReader.EnsureGrantedAsync`, throwing the identical `ForbiddenException`.

Phase 31's cheque bounce widens it: the second key depends on the **requested value** as well as the
row. `TransitionChequeStatusCommand` needs `Configuration.Cheque.Manage` for every transition, and
additionally `Payments.Payment.Void` **only** when the new status is Bounced *and* the linked
payment is Approved — because that is the only combination that voids an approved financial document.

The E2E for this has to run in both directions or it proves nothing. The same Member user must get a
**404** on a nonexistent cheque (proving they hold the pipeline key and the route works for them)
and a **403 naming the second key** on a real one, with the payment still `Approved` on re-read.
One of those alone is consistent with the route simply being broken.

## An invariant only time can violate (phase 31)

`TenantSubscription.Renew` refuses an end date at or before `TrialStartsAt`, and `CreateTrial` sets
`TrialStartsAt = now`. Both are right. Together they mean an **expired** subscription cannot be
constructed through the aggregate's own API at all — only the passage of time produces one.

The tempting fix is to relax the guard so a test can pass a past date. Don't: the guard is the
invariant, and weakening a Domain rule to make a test easier to write trades a real protection for a
convenience. `GeneralSettingsAndSubscriptionTests.ExpireAsync` reaches through EF's change tracker
instead (`((DbContext)db).Entry(subscription).Property(nameof(...)).CurrentValue = ...`), with a
comment saying why. The E2E does the same thing one layer down, with `sqlcmd`.

## The unique index whose filter you must suppress (phase 32)

CLAUDE.md's standing rule, and a correct one: *SQL Server treats NULLs as equal in a unique index, so
a unique index over a nullable column needs `.HasFilter("[Col] IS NOT NULL")`.* EF Core agrees — it
scaffolds that filter **automatically** for a unique index over a nullable column, without being
asked.

Phase 32 made the document-numbering counter location-aware by adding a nullable `LocationId` to
`configuration.DocumentNumberingRules` and rebuilding its unique index as
`(OrganizationId, DocumentType, LocationId)`. In that table:

- `LocationId IS NULL` is **the settings row** — one per (organization, document type), carrying
  Prefix, Mode and the three flags, and doubling as the shared counter while location-wise numbering
  is off;
- a non-null value is one branch's own counter, created lazily on first approval from that branch.

So "there must be exactly one row with a NULL here" is precisely the invariant, and SQL Server
treating NULLs as equal is what buys it. **EF's automatic filter inverts that.** With
`filter: "[LocationId] IS NOT NULL"` the settings rows sit outside the index entirely: a tenant can
end up with two settings rows and two competing counters, and — worse, because it is silent —
`DocumentNumberGenerator`'s lazy-create race protection disappears, since that race is *only* guarded
by a concurrent loser's INSERT violating this index. Duplicate document numbers are the one failure
mode `architecture-spec.md` §3.1 says is not tolerable.

The fix is `.HasFilter(null)` in `DocumentNumberingRuleConfiguration`, and it is load-bearing rather
than cosmetic: a later scaffold that reintroduces the filter is a bug, not a tidy-up. Verified against
the live database with `SELECT name, is_unique, has_filter FROM sys.indexes`.

**The general rule:** before applying the filter gotcha, ask what a NULL in that column *means*. If
NULL is a sentinel value with an at-most-one invariant, the unfiltered index is the enforcement
mechanism and the filter destroys it. If NULL merely means "absent", the standing rule applies as
written.

## When a tenant setting decides which types store a field (phase 32)

Phase 31's lesson was that a `TenantSettings` field with no command behind it is not a dead setting
but an **absent feature**. Phase 32 is the same problem from the other side.

Organization > Features > Billing Location > **Advanced** holds a scope switch: *Enable Location in
Sales Transactions Only* (the default, covering Invoice / Sales Order / Credit Note) versus *Enable
Location in All Transactions* (all seventeen). An Admin can move it at any moment, with one click.

The tempting build is to add `LocationId` to the document types the default names, and add the rest
"when someone needs them". That produces a switch that **appears** to work: flipping it to All
Transactions changes the label, changes nothing in the database, and silently records no location on
eleven document types until some later phase ships the columns. Nobody gets an error.

So the storage is sized for the **union** of everything the setting can select, and only *behaviour*
is the selection:

- all 17 of `DocumentMechanisms.LocationBearing` carry a nullable `LocationId`;
- `DocumentLocationScope.AppliesTo(documentType, mode)` is the single place that answers "does this
  type carry one for this tenant", and every consumer — the picker's visibility, the resolver's
  null-versus-HeadOffice decision, the settings DTO the client reads — goes through it;
- `LocationResolver` returns a real null for an out-of-scope type **even when the caller supplied a
  location**, so a client that keeps sending one after an Admin narrows the scope cannot quietly keep
  writing it. Narrowing the setting has to actually narrow the data.

**The general rule:** when a setting selects among sets, the schema owes the widest set. Ask what the
widest option costs *before* choosing which columns to add, because the cost of adding them later is
not the migration — it is the window in which the setting was a lie.

## The confirm-live that was only blocked on one tenant (phase 32)

`docs/roadmap.md`'s phase-32 entry stated that Billing Location "cannot be read here", because it is
an entitlement that is **off** on the Moonbeam UAT tenant every earlier phase was read against, and
it pre-authorised phase-21c's "derive when confirm-live is impossible" precedent for the whole phase.

That was true of the tenant, and false of the world. A second tenant with `Location Enabled = Yes`
was available for the asking, and reading it changed four decisions that had already been written
down as scope:

| The roadmap assumed | The tenant showed |
|---|---|
| `BillingLocation.LocationType` is part of the create form | there is **no type field** — it is system-assigned |
| a Location field on the document form, beside Warehouse | a borderless picker in the document **header**, `Name (Code)` |
| a fixed list of location-bearing document types | a **runtime tenant setting** (the Advanced panel) choosing between two lists |
| a second permission matrix of unknown shape | the **Transactions group alone**, replicated per location — 94 × N |

None of those errors would have failed a test. All four would have shipped as confident prose in a
status doc, which is the expensive kind of wrong.

**The general rule:** "we cannot observe this" is a statement about the environment you have, not
about the feature. Before invoking the derive-instead precedent, ask whether a different tenant,
account, plan or environment can observe it — the cost of asking is one question.

## A permission that depends on a row nobody has read yet, at scale (phase 32b)

Phase 27a's `AttachmentAccess` and phase 31's cheque bounce both handled this by declaring a blanket
key on the request and re-checking the real one inside the handler. Phase 32b is the third instance
and the first where that does not work: a location-scoped grant applies to **~120 requests** across
every module, and one handler that forgets to re-check is not a bug, it is a door left open for a
caller who should have been narrowed to a single branch.

So the check moved into `AuthorizationBehavior` itself, as a second chance taken only when the
organization-wide check has already failed. Two details are load-bearing:

- the organization-wide grant join now filters `LocationId == null`. Without it a single-branch grant
  satisfies the tenant-wide check, which is the exact escalation the feature exists to prevent;
- it is **not** a sixth pipeline behavior. The location half needs to know whether the first half
  passed, and passing that between two behaviors means a scoped context object that a nested
  `ISender.Send` would overwrite — a bug visible only on the handful of handlers that send other
  requests.

Four marker interfaces say what each request is, and `LocationScopeSweepGuardTests` fails the build on
any request whose declared key is location-scopable and which declares none of them. It reads
`PermissionKey` off `RuntimeHelpers.GetUninitializedObject`, so a key computed from request data
throws and is treated as scopable conservatively rather than skipped.

## A confirm-live pass can falsify an earlier confirm-live pass (phase 32b)

Phase 32's lesson was that a blocked confirm-live is a state, not a verdict — ask whether another
tenant can observe it. Phase 32b is the next step: **a screen someone already read is not thereby
settled, when what was recorded is an inference about a control nobody operated.**

Four documents said that turning `TenantSettings.LocationWiseReportPermission` on is what pulls the
52 Reports keys into the per-location permission matrix: `roadmap.md`'s 32b entry,
`phase-32-status.md`'s Decision A, `erp-module-scan.md`'s 2026-09-07 appendix, and the field's own C#
doc comment. Ticking that checkbox on the live tenant and hard-reloading the app left the matrix at
0 of 282 = 94 x 3 with no Reports group anywhere. The toggle narrows report **rows** to the locations
a role holds grants at; it adds no keys at all.

The cost of finding out was one reversible tenant setting, flipped with the user's explicit
permission and reverted and verified afterwards. The cost of not finding out would have been a
per-location Reports matrix of 52 x N keys that nothing could ever have enforced.

## Reuse a marker by reading it, not by merging it (phase 32b)

Phase-31 lesson (c) says to reuse an existing marker set rather than invent a third — but only when
the set is the same. `ILocationScopedDocument` and `ILockDateSensitiveDocument` have identical shapes
and different sets: a lock date freezes writes, so it is on Approve/Void only, while a location grant
also governs reads (a Member scoped to HeadOffice must not open, print or comment on a POS invoice).

So they stay separate types, and `LocationScopeResolver` simply *reads* the lock-date marker when the
location one is absent. Thirty Approve/Void commands needed no edit at all, the two interfaces cannot
drift into each other, and a guard test pins that the reuse still covers them — so an Approve command
that ever drops the lock-date marker fails the build rather than silently losing its location check.

## An extraction nobody finished (phase 33)

`GrantedPermissionReader`'s doc comment reads: *"Phase 12's `TransactionApprovalQueryHandler` and
phase 23's `RecentTransactionsQueryHandler` each inlined their own copy of this join with a comment
saying it was copied from the other. Phase 27a would have made a third and fourth copy, so it is one
method now."*

It was not one method. Phase 27a wrote the shared reader and gave it its new callers, but never
switched the two handlers that motivated it. That was invisible until phase 32b split the join —
adding `&& rolePermission.LocationId == null`, because *"letting one fall through an unqualified
'does this user hold key K' question would turn a single-branch grant into an organization-wide one
— the exact privilege escalation this phase exists to prevent."* The shared method got the filter.
The two forgotten copies did not.

So a role granted `Sales.Invoice.View` **at HeadOffice only** had that key in both handlers'
granted-key sets and saw **every** location's rows, in the Transaction Approval queue and in the Home
dashboard's recent-activity feed. No test failed; no screen looked broken; a branch-scoped user
simply saw more than they should.

It was found because phase 33's global search was about to become the *third* multi-type feed gated
by the same per-type key check — and reading the two existing ones for the pattern showed the
pattern had rotted. **The rule: a doc comment saying an extraction is complete is not evidence that
it is. When you are about to write copy N+1 of a pattern, grep for copies 1..N and check they were
actually retired.** A shared helper that half the callers ignore is worse than no shared helper,
because the next person to change the helper believes they have changed everyone.

## An expression tree has no short-circuit (phase 33)

The obvious way to make a filter optional inside one predicate:

```csharp
var scoped = allowedLocations is not null;
query.Where(x => x.Code.Contains(term)
    && (!scoped || (x.LocationId != null && allowedLocations!.Contains(x.LocationId.Value))));
```

In ordinary C# `!scoped ||` short-circuits and the null-forgiving `!` is honest. Inside a LINQ
**expression tree** nothing executes: EF walks the whole tree and evaluates
`allowedLocations.Contains(…)` at translation time — against a null list. It throws while
translating, and only on the branch where `scoped` is false, which is the *unrestricted* caller, i.e.
almost every request. The failure therefore lands on the common path and never on the path the test
was written for.

**Compose a second `Where` instead**, which is exactly what every phase-32b list handler already
does:

```csharp
var query = db.Set<T>().Where(x => …);
if (allowedLocations is not null) { var ids = allowedLocations.ToList(); query = query.Where(x => …); }
```

The same applies to any `flag ? a : b` or `x == null || …` over a captured reference in a predicate.
Neither the compiler nor an InMemory test can see it; only a real provider can, and only on the
branch you were not testing.

## `sed -i` damaging the file you aimed it at (phase 33)

CLAUDE.md already records that `sed -i` over a glob rewrites every file it matches and flips CRLF to
LF even where the pattern never fires. The narrower case is worth its own line: it does that to the
**intended** file too. A one-token fix (`VatRate.Zero` → `VatRate.NoVat`) silently converted a
newly-written test file from CRLF to LF, after which every subsequent `
`-anchored patch script
failed its assertion with no explanation — the anchor was right, the line endings were not. Prefer
the Edit tool for a single substitution, and if a script must patch a file, read its existing
newline (`io.open(..., newline='')`) rather than assuming one.

## A control a control-shaped scan cannot see (phase 34a)

The accessibility sweep enumerated every `<input>`, `<select>` and `<textarea>` with no accessible
name, found 348, and fixed all of them. It was complete, and it had missed **121 date fields** whose
visible label sat right above a real text input with nothing joining the two -- because the input is
inside `<app-bs-date-input>`, and a scan for control elements does not see a control element that a
component wraps. `BsDateInput` had accepted an `inputId` since phase 23; the app passed it **once in
122 uses**, so 121 labels pointed at nothing and 121 inputs had no name.

No refinement of the original scan finds these. What finds them is the **mirror** question -- not
"which controls have no label?" but "which labels name no control?" -- and the two together are what
make the rule total, because every wrapped case shows up on exactly one side. `a11y-sweep-guard.spec.ts`
asserts both directions, plus a third that keeps the pair honest: every caller of
`<app-bs-date-input>` must pass `[inputId]`, because that component is the one entry on the
"needs no name" allow-list *on the grounds that its caller names it*.

The general form: **whenever a sweep enumerates one side of a relationship, ask what enumerating the
other side would find.** The difference between the two counts is where the indirection is hiding.

## Bootstrap's contrast margin is the white in the worked example (phase 34a)

Bootstrap's brand colours are chosen to clear WCAG 1.4.3's 4.5:1 against **pure white**, and they
clear it by nothing: `text-primary` (#0d6efd) is 4.50:1, `text-danger` and `text-success` 4.53:1.
This app's page background is `#f8f9fa`, not `#fff`, and on that ground the very same colours are
4.27-4.45:1 -- failing. Any Bootstrap app with a tinted body has this, invisibly.

Phase 34a re-points `.text-primary`, `.text-secondary`, `.text-success` and `.text-danger` (and
`--bs-link-color`) at Bootstrap's own `-600` shades in `styles.scss`, which clear 6.1:1 on both
grounds. **Text utilities only** -- `bg-primary`, `btn-primary` and the rest keep the brand colour,
because white-on-colour is a separate pairing that already passes.

Two things about how this was found matter more than the fix:

* **It was found by computing, not by auditing.** The sweep began as a hand-written rule ("never pair
  `bg-*-subtle` with the plain tone"), which would have fixed the 161 badges and never looked at the
  page ground. `contrast-rules.ts` records the *palette* and derives which pairs fail; a written-down
  list keeps the answer and loses the reason, so it cannot be extended and cannot be checked.
* **The defect was also a consistency defect.** The codebase paired `bg-*-subtle` with `-emphasis`
  50 times and with the plain tone 161 times -- one screen right, the rest wrong. WCAG 1.4.3 and
  NFR-6.1 were the same bug seen from two directions.

The guard covers colours that come from utility classes, which today is all of them. An inline
`style` or a component stylesheet is outside it, and nothing will announce when that changes.

## A positional derivation is confidently wrong where the position lies (phase 34a)

Naming the 96 line-item grid controls from their own column header -- walk back to the enclosing
`<td>`, count `<td>`s since the `<tr>`, take that `<th>` -- is right for a per-column data cell and
wrong for the two shapes it cannot see: a control inside a `<td colspan="5">` expansion row (index 0
is the whole row, not the first column), and a cell that carries its own visible label. Six of the 96
came out wrong, including an **Amount** field announced as "Name".

**A confidently wrong accessible name is worse than no name at all.** A missing name announces as
"edit text" and the user knows they are missing something; a wrong one is indistinguishable from the
truth. So the remedy is not a more careful first pass but a **second pass that audits for what the
first could not see**, and refuses to write until it reports zero -- which is how these six were
found and converted into real `for`/`id` pairs.

## A guard must assert its input is non-empty, not merely present (phase 34a)

The test that keeps `styles.scss` in step with the palette `contrast-rules.ts` measures read the
stylesheet with `import.meta.glob('/src/styles.scss', { query: '?raw', … })`. **Vite compiles SCSS,
so `?raw` returns an empty string** -- not an error, not `undefined`. `expect(stylesheet).toBeDefined()`
passed on `''`, and every assertion over the contents was vacuously true. `?inline` does the same.

This happened inside the very file whose header sets out the three rules that make a guard real, the
first of which is "assert the glob matched a plausible number of templates first, so a broken glob
cannot make every other assertion pass vacuously". That rule is not about globs. **It is about every
input a guard reads**, and the check has to be on the *content* (`length > 500`), never on presence.
It was caught only because the failure message happened to print the length.

The fix reads through `node:fs`. This project has no `@types/node`, and rather than add a dependency
for one `readFileSync` there is a two-declaration shim in `src/testing/node-shims.d.ts`, which
`tsconfig.app.json` excludes -- so the app build cannot see it and no application file can start
importing `node:fs` and still typecheck.

## A lazy `.*?` spans the instances between its anchors (phase 34a)

A sweep associating date-field labels matched
`<label…>(.*?)</label>\s*<app-bs-date-input`. Where a label was *not* followed by a date input, the
regex engine did not give up: it expanded the lazy group across that label's own `</label>` until it
reached a later label that was, **merging two labels and rewriting everything between them**. The
body has to exclude the closing marker itself -- `((?:(?!</label>).)*?)`.

The tell was not the regex. It was that **two independent counts disagreed**: an ad-hoc scan said 121
labels were followed by a date input, the sweep's own tally said 99. Phase-32's rule is that a
scripted multi-file edit asserts its anchor count before writing; this adds that the expected number
is worth deriving a **second way**, because a sweep that miscounts will also mis-edit, and its own
tally is not independent evidence of anything.

## Reverting one file to undo a test regression reverts the phase's work on it (phase 34a)

Proving the accessibility guard actually bites means injecting a regression into a real template and
watching the assertion fail. Undoing that with `git checkout -- <file>` restores **HEAD**, which
silently threw away that file's contrast, `th scope`, `aria-hidden` and label edits along with the
injected damage -- and the suite went green again, because the guard reads every template and the
only offences it would have reported were the ones just removed.

Back the file up to the scratchpad first and restore it by **SHA-256**, which is also what makes a
before/after measurement trustworthy: the same before/after toggle was then used to measure one badge
at 3.49:1 on HEAD's markup and 10.35:1 on the phase's, in the same running app, on the same element.


---

## A global filter a screen renders but does not reload on (phase 34b)

The top bar's date range is stored per user and loaded asynchronously. A list page's constructor
issues its first request immediately, so on the first paint the two disagree: the chrome rendered
**"Last 30 days: 2026-08-11 – 2026-09-10"** above an invoice dated **2026-07-20**, because the label
read the settled signal and the rows came from the default the request was built with.

Nothing was broken in a way a test could see. The request was well-formed, the handler filtered
correctly, the label was accurate about the *current* setting. The defect is only visible when the
two are on screen together, which is why the browser pass found it and 271 unit tests did not.

**The rule:** anything global that a screen both *shows* and *sends* has to reload that screen when
it changes. `ListFilter` therefore takes a `reload` callback and drives it from an `effect` over the
range, skipping the first run by comparing the value rather than by a flag — the window may
legitimately settle to the one it started at.

**The stronger form:** a filter a page displays but did not apply is worse than no filter at all. No
filter shows you everything and you know it. A displayed-but-unapplied filter shows you a wrong
answer that looks checked.

---

## An `effect()` racing the handler that wrote the signal (phase 34b)

The list chrome's search box debounces: `onInput` sets `term` and schedules `searchChange.emit(value)`
after 300ms, or after **0ms** when the box has been cleared, so clearing feels instant.

An `effect()` was then added over `term` to "make clearing immediate" — it saw an empty term with a
pending timer and cancelled it. But the timer it cancelled was the one `onInput` had *just* scheduled
to do exactly that job. Clearing the search never restored the unfiltered list; the symptom was a
list stuck at its filtered count with an empty box above it.

**The rule:** an effect cannot distinguish "the write that is already being acted on" from "a write
that needs acting on". If the handler that wrote the signal already schedules the response, an effect
over that signal is a race and not a safety net. Delete it and let the handler own the whole
transition.

---

## `overflow: hidden` on a shell container clips the popups inside it (phase 34b)

The left rail had `overflow: hidden` to keep its flex children in bounds. The Create New flyout is
`position: absolute` inside the rail's header and is 46rem wide — so two of its four columns were
simply not rendered, clipped to the rail's 240px.

This is phase-22's gotcha (`.dropdown-menu` inside `.table-responsive` is clipped by the implied
`overflow-y`) reaching a second container. The remedy there was `position: fixed` at captured
coordinates; here the simpler one applies, because the scrolling the rail actually needs belongs to
its middle section alone (`.left-nav-list` has its own `overflow-y: auto`). Removing `overflow` from
the outer container costs nothing.

**The rule:** before putting `overflow` on a layout container, ask what is anchored inside it. Any
menu, popover or flyout that means to escape its parent will be cut by it, silently and without a
console error.

---

## A shared matcher cannot live inside a LINQ-to-Entities predicate (phase 34b)

Phase 34b gave 25 list queries a search term, and the obvious first move was one shared rule:

```csharp
public static bool Matches(string? column, string term) =>
    column != null && column.Contains(term, StringComparison.OrdinalIgnoreCase);
```

It cannot work, for two independent reasons: **a static method call inside a `Where` is not
translatable at all**, and **`string.Contains(term, StringComparison)` is not translatable either** —
only the single-argument overload is, which SQL Server renders as `LIKE '%term%'`.

The dangerous part is the failure mode. Every handler test in this codebase runs on the InMemory
provider, which evaluates the expression in C#, where both forms work perfectly. The helper would
have passed every test and 500'd all 25 endpoints in production. That is phase-25's captured-`Func`
gotcha (a validator selector that 500s every endpoint it guards, invisible to handler tests) arriving
through a different door.

Each handler therefore writes `x.Code.Contains(term)` inline, and `SearchableQuery.cs` carries a
comment saying why the duplication must stay.

**The related trap:** the single-argument `Contains` matches **case-insensitively** on SQL Server
(the collation does it, not the expression) and **case-sensitively** on InMemory. A handler test must
search with the stored casing, or it is asserting a behaviour the real database does not have — in
either direction.

---

## Seeding traps added by phase 34b's E2E

Three request-shape traps and one shell trap, all of which surface as errors that name the wrong
thing:

- **`POST /products` takes `primaryUnitId`**, not `unitOfMeasurementId`. The wrong name is a 400
  naming `PrimaryUnitId`, which reads like the unit was never created rather than like a typo.
- **`POST /accounts` takes `{name, groupId, kind}` and nothing else.** There is no `code` (the
  numbering engine generates it) and no `openingBalance`, and `kind` is an `AccountKind`
  (`Other`/`Bank`/`Cash`) — sending `"Normal"` makes the whole body fail to deserialise, and the 400
  says only *"failed to read parameter CreateAccountRequest from the request body as JSON"*, naming
  neither field.
- **A fresh organization has no warehouse**, the same shape as phase-27a's "a fresh organization has
  no chart of accounts".
- **A bash helper that both prints and returns is a trap under `$( )`.** A `mkaccount` helper called
  the shared `req` (which prints a status line) and then echoed the new id; captured as
  `SALES=$(mkaccount ...)` it returned *the status line and the guid*. Fourteen malformed ids
  produced a `PUT /accounting-defaults` that stored fourteen nulls, and the failure surfaced three
  steps later as a 409 at Approve whose message named a missing Sales Account — i.e. it looked like a
  product misconfiguration, in a completely different part of the seed. Have such a helper set a
  global instead of echoing. (Same family as the earlier `BODY` trap: a function whose assignment is
  discarded because `$( )` ran it in a subshell.)

---

## The browser pane's measurements lag the rendering under viewport emulation (phase 34b)

After `resize_window` to a mobile preset on an already-rendered page, `getComputedStyle(rail).transform`
reported the identity matrix and `getBoundingClientRect()` put the nav rail on screen at `left: 0` —
while the screenshot showed it correctly tucked off-screen. The CSS rule was present, the media query
matched, and the selector matched the element; only the measurement was stale.

A fresh load at the emulated size agreed with the screenshot.

**The rule:** in the browser pane, the screenshot is ground truth. Reload after emulating a viewport
rather than trusting measurements taken across a resize — and when a measurement contradicts a
screenshot, believe the screenshot. Related to the standing trap that the screenshot frame and
`innerWidth` differ, so the scale factor must be read per tab and never reused.

---

## Gotcha entries as written in CLAUDE.md before the 2026-09-10 trim (phases 31–32b)

These thirteen entries had grown past one line in CLAUDE.md; they were shortened there on 2026-09-10 and are kept here verbatim. Their full narratives are the phase-31/32/32b headings above.

- A tenant-level field is reachable only if you can name the **command** that writes it and the screen that calls it; being *read* by a handler proves the read path and makes the missing write path invisible (phase-31, extending phase-29's grep-`web/` rule).
- Two confirmable warnings on one document need two override flags and a `warningKind` on the 422, or confirming the first silently waives the second (phase-31).
- Adding a **non-nullable** column to a populated table needs its backfill written by hand: the scaffold's `DEFAULT '0001-01-01'` back-dates every historical row and leaves a stray constraint (phase-31, extending the replace-or-retype rule).
- A permission whose applicability depends on the **requested value** as well as the loaded row is phase-27a's `AttachmentAccess` pattern again; the E2E must show 404 on a missing row *and* 403 on a real one, or it has proved nothing (phase-31's cheque bounce).
- Never weaken a Domain invariant so a test can reach a state only time produces; reach through EF's change tracker instead (phase-31's expired `TenantSubscription`).
- Before applying the nullable-unique-index filter rule, ask what a NULL in that column *means*: when NULL is a sentinel with an at-most-one invariant, the **unfiltered** index is the enforcement and EF's automatic `IS NOT NULL` filter destroys it — `HasFilter(null)` is load-bearing (phase-32's numbering counter, inverting the standing gotcha).
- When a tenant setting selects among *sets* of document types, the schema owes the **widest** set, not the current one — otherwise flipping the setting is a lie until a later phase ships the columns (phase-32's `LocationScopeMode`, phase-31 lesson (a) inside out).
- The `AttachmentAccess` pattern stops being a per-handler re-check at the point where missing one instance is an open door rather than a bug: phase 32b's ~120 requests moved the check *into* `AuthorizationBehavior` (not a sixth behavior — the two halves must share one decision, and a scoped context between behaviors is corrupted by a nested `ISender.Send`), behind four marker interfaces and a sweep guard that fails the build on any location-scopable request declaring none of them (phase-32b).
- **A confirm-live pass can falsify an earlier confirm-live pass.** Four documents recorded that `LocationWiseReportPermission` pulls the 52 Reports keys into the per-location matrix; flipping it on the live tenant showed the matrix unchanged — it scopes report *rows*. A screen someone already read is not settled when what was recorded is an inference about a control nobody operated (phase-32b, extending 32's another-tenant rule).
- Reuse a marker interface by **reading** it, not by merging it: phase-31 lesson (c) applies only when the sets match, and `ILockDateSensitiveDocument`'s is narrower than a location grant's (a lock date never gates a read). Reading it from the resolver spared all thirty Approve/Void commands an edit, with a guard test pinning that the reuse still covers them (phase-32b).
- `POST /api/organizations` also needs `industry` and a **non-empty** `turnstileToken` (any string passes against the dummy secret); accept-invitation is `/api/organizations/memberships/{id}/accept-invitation` with **no org segment**, and calling it with one returns a 404 that reads like a bad membership id while the membership silently stays `Invited` — which makes any later Member-403 proof meaningless; units are `/units-of-measurement` (field `shortName`); credit terms are under `/configuration/` (phase-31).
- `POST /accounts` takes `groupId`, not `accountGroupId`; and `POST /products` takes **`type`, not `productType`** — the wrong name silently yields a *Goods* product whose line then consumes stock and 409s at Approve with a message about the warehouse, which reads like a seeding fault rather than a typo (phase-32).
- A scripted multi-file edit must assert its **anchor count** before writing (and preserve each file's CRLF/BOM); phase 32's sweep touched 32 commands safely that way, and the one edit that matched three records where two were meant was caught by exactly that check.

---

## Every tenant-scoped table needs an index leading on `OrganizationId` (phase 34c)

Eighteen did not, and the eighteen were not a random eighteen: they were **exactly the transactional
documents plus `GlJournalEntry`**. The reason is structural rather than editorial. Master data
carries a per-tenant uniqueness rule — `(OrganizationId, Code)` on Contacts and Products,
`(OrganizationId, Name)` on the lookups — so every one of those tables got a leading-`OrganizationId`
index *for free* as a side effect of a constraint nobody added for performance. A document number is
not unique-indexed, so no document table ever got one. The gap tracked the absence of a uniqueness
rule.

The cost, measured on 50,000 invoices in one tenant: the invoice list's first page issued two
statements (`CountAsync`, then `Skip/Take`) that each read **2,644 pages** — a full clustered scan —
plus a sort of every row the tenant owns to satisfy `ORDER BY CreatedAt DESC`. 469 ms p95 for fifty
rows.

Because the gap had a rule behind it, the fix does too. `TenantIndexConvention` runs after
`ApplyConfigurationsFromAssembly` and derives three families from the model:

- an entity with `OrganizationId` **and a business date** (`Date`, else `PostedAt`) is a document:
  `(OrganizationId, BusinessDate)` for the reports that range over it, and
  `(OrganizationId, CreatedAt DESC)` for the list screen, since all fourteen document lists order
  `CreatedAt DESC`;
- the searchable columns get a covering index — `(OrganizationId, Name?, Code)` with the wide
  reference fields as `INCLUDE`s;
- anything else must already carry a leading-`OrganizationId` index, and the convention **throws at
  model build** naming the entity if it does not, unless it is listed in `NotIndexedByTenant` with a
  reason. That is stronger than a guard test: it creates the index rather than checking someone
  wrote one, and it fires in `dotnet ef migrations add` and in every test that boots the host.

"An aggregate with a business `Date` is a document, master data is not" is phase 34b's own rule for
which lists get a date filter. The same sentence decides which tables get the date index, which is
why it is stated once instead of decided per table.

Write cost, measured on an identical 190,000-row bulk `INSERT` with no reads at all — the most
hostile framing available: 48.8 s with none of them, 60.3 s with the 33 document indexes (+23.6 %),
64.8 s with all 50 (+32.8 %). Every key column is write-once, so there is no update cost, only
insert.

## An index added for one access path changes the plan for every other path (phase 34c)

`(OrganizationId, CreatedAt DESC)` took the unfiltered invoice list from 469 ms to 49 ms. On the same
table, in the same deployment, a search term matching nothing went from **651 ms to 1,173 ms**.

The mechanism: `WHERE OrganizationId = @o AND (Code LIKE @t OR Reference LIKE @t) ORDER BY CreatedAt
DESC OFFSET 0 ROWS FETCH NEXT 50`. Before the index there was one plan available — scan the clustered
index once (5,825 pages), filter, sort. After it, the optimizer bets it can avoid the sort by walking
the new index in `CreatedAt` order and doing a **key lookup per row** to test the `LIKE`, stopping as
soon as it has fifty matches. When the term matches something recent the bet pays (283 ms). When it
matches nothing the bet costs a full walk with 50,001 lookups — **83,000 logical reads per
execution**.

The covering search index this phase then added fixes the *raw* predicate (6,743 logical reads to 700
on Contacts, 5,825 to 451 on Invoices) and therefore fixes **global search**, whose queries are
`… Code LIKE @t ORDER BY Code TOP 5` and can be answered from the index alone: 999 ms to 595 ms.
It does **not** fix the list search, because `ORDER BY CreatedAt DESC` still makes the ordering index
look cheaper. Adding `CreatedAt` to the covering index's `INCLUDE` list was tried and made it worse
(153,439 reads) — the optimizer kept its choice.

Two things to take from it. **Re-measure the paths you did not change**, over the same table, before
calling an index a win. And a `LIKE '%term%'` is non-sargable no matter what you do: an index can
only make the scan narrow and per-tenant, never a seek. The remaining fix is full-text indexing or a
two-step handler (match ids from the covering index, then order and page), which is twenty handlers
and belongs to whoever has a tenant complaining.

## A report's cost is its period, not its page size (phase 34c)

Seven of the report handlers materialise every row in the requested period and then page the
resulting list, so `pageSize` bounds the response and not the work. Measured on 50,000 invoices: the
Sales Register costs 0.65 s for one month, 1.19 s for a year and 2.1–3.5 s for three; the Detail
General Ledger 1.8 s / 4.3 s / 6.1–14.9 s, returning **59.5 MB at `pageSize=50`** because its page
unit is the *account* and the rows inside a page are unbounded.

The full read is not laziness. Phase 16c requires footer totals over the whole filtered set, never a
reduce over one page, and `SalesRegisterQueryHandler` sums its four totals over every row in the
period. Removing the full read therefore means two queries — a SQL aggregate for the totals and a SQL
page for the rows — per report.

`JournalReportQueryHandler` is the counter-example, and it is already in the repo: it loads only the
entry *keys* for the period, pages those in memory, and then fetches lines for the page alone. Over
the same 70,002 entries it costs 250–344 ms. That is the shape, and it is why this is a conversion
rather than a design problem.

The related trap is the `Contains` that carries it: a materialised id list handed back to SQL becomes
an `OPENJSON` parameter as long as the list. `ContactAgeingSummaryQueryHandler` ships a
50,000-element array on every call and costs 7.5–8.9 s.

## An inference about a design is not a measurement (phase 34c)

`GlLine` carries no `OrganizationId`. Every financial statement therefore joins through
`GlJournalEntries` and hash-joins against a **full scan of the whole GL line table**, across every
tenant in the database — 7,586 logical reads at 421,780 rows. The obvious conclusion is that every
tenant's Trial Balance pays for every other tenant's ledger, and it was one edit away from being
written up as a finding.

Seeding a *second* 50,000-invoice tenant and re-measuring the first refused it. The scan does double
(3,800 to 7,586 logical reads) and the wall time does not move outside run-to-run noise, because a
logical read from a warm buffer pool is nearly free next to the aggregate that follows it. The
structural fact is real; the cost it implies is not, yet. It becomes real when `GlLines` no longer
fits the buffer pool and those logical reads become physical — which is the re-entry condition, and
is checkable rather than speculative.

The general form: **a plausible mechanism plus a plausible magnitude is still not a measurement.**
The experiment that separates them here cost one seed run.

## Measuring performance on a working machine (phase 34c)

Two identical passes of the same 33-endpoint harness, against the same database with the same
statistics treatment, disagreed by up to **4×** on the report class — the three financial statements
read 1.0 s, 1.3 s and 3.7–4.1 s across three passes with no schema change between the last two. The
list class agreed within 30 %. The difference is that a list page is a handful of milliseconds of
work and the reports are seconds of CPU that compete with whatever else the machine is doing (a
build, a leftover dev server, a 400,000-row delete).

Three rules came out of it:

- **Refresh statistics identically on both sides.** On a dataset that arrives by bulk `INSERT`,
  statistics quality moves a report that joins to a line table by 2× — more than most changes under
  test. `tools/scale/refresh-stats.sql` does it, and clears the plan cache.
- **Judge a row over more than one pass**, and say `MIXED` where the passes straddle the budget
  rather than reporting whichever ran last. `summarise.sh` does this.
- **Prefer within-pass comparisons for anything expensive.** The period-sensitivity readings that
  carry this phase's report finding are consecutive requests inside one pass, which controls for the
  machine in a way that two passes an hour apart do not.

## A benchmark against an empty tenant looks like a fast one (phase 34c)

A repeat pass came back at 20–30 ms p95 on every endpoint with every status code `200`. It was
reading an empty organization: `seed-master.sh` rewrites `tools/scale/.seed-ids.env` every time it
runs, so seeding a second tenant silently repointed the harness at it.

Nothing in the timings distinguishes the two cases — that is the point. The only signal is the
**response size**, so `run-measurement.sh` now refuses to start unless a 50-row invoice page returns
more than 1,000 bytes.

This is phase 34b's prints-and-returns trap in another costume: **a helper called for one purpose
that also writes shared state**. The other half of the same lesson is that a fast number is the one
you should distrust first.

## `sqlcmd -i` runs with `QUOTED_IDENTIFIER OFF` (phase 34c)

The first bulk insert into `catalog.Products` failed with `INSERT failed because the following SET
options have incorrect settings: 'QUOTED_IDENTIFIER'`, which reads like a syntax problem and is not:
the table carries a filtered index (`IX_Products_OrganizationId_ParentProductId_CombinationKey`), and
SQL Server refuses any insert into a table with one unless `QUOTED_IDENTIFIER` is ON. `sqlcmd -i`
does not set it. Put `SET QUOTED_IDENTIFIER ON;` at the top of every script that writes to this
schema.

Two smaller frictions from the same session: `sqlcmd -W` and `-y`/`-Y` are mutually exclusive, and a
`SELECT` naming a column present on two joined tables fails with `Ambiguous column name` rather than
picking one.

## A write-path guard proves nothing about the read path (phase 35a)

Phase 32 put `LocationId` on all 17 location-bearing types and shipped
`LocationBearingCommandSweepGuardTests`, which asserts by reflection that every one of them has a
command able to *write* the field. It has been green ever since. Meanwhile **fourteen of the fifteen
document detail queries dropped the field on the way back out**, and only `InvoiceDetailDto` named
it.

The asymmetry is structural and worth stating as a rule:

- a **list** query returning the aggregate exposes a new column for free;
- a **detail** query projecting an explicit DTO drops it unless told;
- a **conversion template** is a read path wearing a different hat, and drops it the same way — all
  five of this codebase's templates did, so converting a document raised at a branch produced one at
  HeadOffice.

The failure mode is worse than "the form cannot show the value". The header picker defaults to the
tenant's HeadOffice when the DTO gives it nothing, so **opening a branch document and pressing Save
moved it**. No test failed, and phase 32's own carried note predicted exactly this — for one type.

So: a phase that adds a field to many aggregates owes **three** assertions, not one — write, read,
and every prefill in between. `LocationReadPathSweepGuardTests` is that shape, derived from
`DocumentMechanisms.LocationBearing` rather than listed.

## The expression-tree gotcha is narrower than it was written (phase 35a, correcting phase 33)

"An expression tree has no short-circuit (phase 33)" above ends with: *"The same applies to any
`flag ? a : b` or `x == null || …` over a captured reference in a predicate."* **The second half of
that is not true for a captured collection compared to null**, at least on EF Core 10 + SQL Server.

Five phase-32b list handlers shipped with

```csharp
.Where(x => allowedLocations == null
    || (x.LocationId != null && allowedLocations.Contains(x.LocationId.Value)));
```

which is the forbidden shape. Phase 35a rewrote them as composed `.Where()` calls and then, to
demonstrate the bug, put the old shape back in one handler and ran it against SQL Server with real
rows. It returned **200 and the correct row**.

The worked example phase 33 measured used a captured **bool** (`!scoped || …`), and that one does
throw. The likely difference — a hypothesis, not a measurement — is that EF parameterises a captured
scalar, so `!@__scoped_0 || …` cannot be folded and both sides must translate against a null list,
while `allowedLocations == null` is parameter-independent, funcletized to a constant, and the `||`
optimised away before `Contains` is reached.

**Keep composing** — it is the codebase's stated shape and immune to either mechanism. But do not
describe an instance of the collection form as broken without running it: a gotcha's worked example
is evidence, its closing generalisation is a hypothesis.

## `ng test` must be run from `web/` (phase 35a)

`a11y-sweep-guard.spec.ts` reads the shipped stylesheet with
`readFileSync(resolve(process.cwd(), 'src/styles.scss'))`. Run it as `npx --prefix web ng test` from
the repo root and that one test fails — with a message about the stylesheet reading back empty,
which looks exactly like the phase-34a gotcha the assertion exists to prevent. `cd web && npx ng test`.

## A lazily-created cached signal cannot be written by a synchronous source (phase 35a)

`BillingLocationStore` creates its signal on first read and subscribes in the same call. Its first
reader is always a `computed()` (a picker's `visible()`, a cell's `name()`), so a source that
resolves **synchronously** writes the signal *inside* that computed and Angular throws
`NG0600: Writing to signals is not allowed in a computed`.

Real HTTP resolves on a later tick with no active consumer, so this never appears in the app and
appears immediately under a test double returning `of([...])`. Wrap the subscribe and both writes in
`untracked()`, which removes the difference between the two rather than papering over the test.

## Seeding traps added by phase 35a's E2E

- `POST /accounts` takes `kind` in **`Other` | `Bank` | `Cash`** — there is no `Normal`, and the 400
  names no field (CLAUDE.md already warned that `"Normal"` fails; these are the real members).
- `POST /organizations` needs `accountingStartDate` and `workspaceName`, and takes the entitlement
  flags (`trackInventory`, `multipleLocations`, `multipleWarehouses`, `manufacturing`, …) directly —
  there is no separate features PUT to make afterwards.
- `POST /auth/register` requires **`phone`**; without it the 400 lists no missing field usefully.
- `PUT /roles/{id}/permissions` takes `grants` as a **dictionary** and `locationGrants` as a **list**
  of `{locationId, grants}` — the two halves have different shapes, and the mismatch surfaces as
  "Failed to read parameter … as JSON" rather than a validation error.
- Invitations are `POST /organizations/{id}/invitations`, not under `/memberships`.
- Master-data list endpoints return a **paged** envelope, so it is `['items'][0]['id']`; a seed
  script reading `[0]['id']` silently yields an empty id and the next POST fails as malformed JSON.
- A **Service** product cannot be transferred, adjusted or produced — `ConsumeAsync` refuses it with
  a 409. An E2E touching those three types needs a Goods product even when it never approves
  anything.

## A script that inserts an import after "the last import line" (phase 35a)

The obvious way to add an import — find the last `\nimport ` and insert after that line — breaks on
a **multi-line** `import { … } from '…'`, because the last such line is the `import {` of a braced
block and the insertion lands inside it. The file then fails to parse with a cascade of TS1003 /
TS1005 errors that name no useful location. Insert after the *closing* line of the last import
statement, or assert the anchor line ends with `';`.

## A shared helper must own every condition the `Where` it replaced carried (phase 35b)

Nine GL report handlers each wrote the same join:

```csharp
from line in db.GlLines
join entry in db.GlJournalEntries on line.GlJournalEntryId equals entry.Id
where entry.OrganizationId == request.OrganizationId && entry.PostedAt <= cutoff
```

Phase 35b needed a Billing Location narrowing in all nine, so it extracted `GlEntryLocations`. The
first version took an `IQueryable<GlJournalEntry>` and applied only the location conditions — which
meant each handler had to keep `entry.OrganizationId == …` in its own `where` while everything around
it moved. **This codebase has no EF global query filter**; CLAUDE.md states the rule plainly (every
handler filters by `OrganizationId` in LINQ, by hand), so nothing would have caught a miss but a
reader. Nine rewrites is nine chances.

The fix is a signature, not discipline: the helper takes the organization and builds the base query
itself.

```csharp
internal static IQueryable<GlJournalEntry> ForReport(
    IAppDbContext db, Guid organizationId, Guid? requested, IReadOnlyList<Guid>? scope)
{
    var entries = db.GlJournalEntries.Where(x => x.OrganizationId == organizationId);
    ...
}
```

The general form: **when an extraction removes a `Where` from N call sites, it inherits every
condition that `Where` carried, not only the one the extraction is about.** The tell is a diff that
deletes a predicate the new helper never mentions.

## An append-only fact table can only be filtered by a column (phase 35b)

`GlJournalEntry`, `StockMovement` and `StockLedgerEntry` all point back at their origin with
`(SourceDocumentType, SourceDocumentId)` and nothing else. That is enough to *display* a document's
number or date — `GlSourceDocumentResolver` (phase 26a) does exactly that, over one page — and not
enough to *filter*, because a filter has to run before aggregation, and a report aggregates its whole
period before it pages (phase 34c). An 11-way join would therefore run over every row in the window
on every request, on nine reports.

So: **an append-only fact row carries the billing location of the document that created it, stamped
at write time.** Three things the rule owes, all of which have bitten before in other forms:

- **A reversal inherits, never re-derives.** `GlJournalEntry.PostReversalOf` copies the original's
  `LocationId`; `StockLedgerService.ReverseIncrementAsync` takes `layer.LocationId`. A void landing
  at a different location leaves the original branch's Trial Balance permanently off by the
  document's value while the organization-wide total still balances — phase-6 bug #3 with the
  location as the axis instead of an account.
- **The backfill is per producing type, and the sets are not the same.** The GL has 11 producers and
  the stock tables have 8; `JournalVoucher`, `CashTransfer`, `Expense` and `Payment` post no stock,
  while `WarehouseTransfer` and `OpeningStock` post no GL. Copying one list into the other silently
  under- or over-reaches.
- **Null means three different things** — older than the backfill, raised while the type was out of
  the tenant's scope, or a tenant without the entitlement — and all three are correctly excluded by
  `x.LocationId == id`, because a null never equals a value.

No index was added on any of the three: the location is always applied alongside the period, so the
seek is the date index that already exists and the location is a residual. Phase 34c measured what a
second index over the same table does to the paths that do not use it.

## A `Reports.*` permission cannot be granted per location (phase 35b)

Every location proof in this codebase since phase 32b uses the same grant shape: the key **false
organization-wide and true at one location**, because an organization-wide grant makes
`LocationAccessScope` return "unrestricted" and the test passes without the mechanism firing. Phase
35b's first negative E2E applied that shape to `Reports.TrialBalance.View` and got a 400:

> `'Reports.TrialBalance.View'` cannot be granted per location. Only transaction permissions are
> location-scoped; General, Settings and Reports permissions are organization-wide.

`LocationScopedPermissions.IsLocationScopable` admits only keys whose document type is in
`LocationBearing`, and `LocationAccessScope.ForReportsAsync` says so in its own doc comment — the
toggle's label is *"Restrict users to view reports only for locations they have access to"*, and a
user's locations are the ones their role holds **transaction** grants at
(`AnyGrantedLocationsAsync`). There is no per-location report grant to store.

So a report-scope proof needs:

- the report key held **organization-wide** (or the caller cannot open the report at all), and
- a *transaction* key — e.g. `Sales.Invoice.View` — false organization-wide and true at the branch,
- with `TenantSettings.LocationWiseReportPermission` on.

The lesson beyond the mechanics: a convention that holds in fifteen places is not a rule, and the one
place it does not hold is exactly where the test belongs.

## A swept-in change handler must copy its neighbours, not a template (phase 35b)

Phase 35b generated an `onLocationChange` for 43 report pages:

```ts
protected onLocationChange(value: string): void {
  this.locationId.set(value);
  this.load();
}
```

Correct on 23 of them and subtly wrong on 20. Report pages come in two shapes: some have a
`reload()` that does `page.set(1); load();`, and some only have `load()` — and on *those*, every
individual filter handler resets the page itself. Without the reset, narrowing to a branch while on
page 3 returns an empty page, which reads as "this branch has no data" rather than "you are past the
end".

Nothing failed. The separation was made by asking each file what its own sibling handlers do
(`this.page.set(1)` present, and `load()` not already resetting) rather than by classifying pages
from a template. **A sweep copies the behaviour beside it; it does not impose one.**

## Seeding traps added by phase 35b's E2E

Four more, each of which fails in a way that points somewhere else:

- **`POST /organizations` takes `multipleLocations` / `multipleWarehouses` / `trackInventory`.** An
  `...Enabled` suffix binds to `false` with a 201, and the failure surfaces several calls later as a
  403 saying the feature is not enabled — which reads as a permission problem.
- **`POST /billing-locations` requires `address`**, despite the API request record defaulting it to
  null. The validator is the authority, not the record.
- **Inviting is `POST /organizations/{id}/invitations`** (not `/memberships/invite`) and it returns
  **`membershipId`**, not `id`.
- **`POST /products` needs the whole record, and `vatRate` is `ThirteenPercentVat`.** A wrong enum
  member fails as `Failed to read parameter "CreateProductRequest request" from the request body as
  JSON` — naming no field, exactly the phase-34b `AccountKind` trap.

Also: **`sqlcmd -Q` prints "(N rows affected)" into a captured value.** `SET NOCOUNT ON` belongs
beside phase-34c's `SET QUOTED_IDENTIFIER ON` at the top of every script — otherwise a count comes
back as `1(1rowsaffected)` and the assertion fails for a formatting reason.

---

## "One GL entry per document" was a habit, not an invariant (phase 36)

`GlJournalEntry`'s own doc comment has always said `(SourceDocumentType, SourceDocumentId)` is
non-unique, and its index has always allowed a second row — that is how phase 16a's void works: a
mirror entry, never a mutation. What nobody wrote down is the narrower thing six call sites assumed:
**while a document is still Approved, it has posted exactly one entry.** Two voids, a cheque bounce,
two detail queries and the Opening Balance editor all leaned on it, four of them through
`SingleAsync`.

Phase 36 broke the assumption on purpose — allocating further against an Approved payment or journal
voucher posts the realised forex leg as its own entry, because an entry is append-only and the
correction cannot be folded into the first one. But the assumption was **already false**, and had
been since phase 17: `CreateOrUpdateOpeningBalanceLineCommandHandler` reverses "the prior entry"
before re-posting, so after one edit a line has *three* entries (original, reversal, corrected), and
the **second** edit threw `InvalidOperationException` out of `SingleAsync` as a 500. Nobody had
edited one twice.

`SourceDocumentGlEntries.ReverseOutstandingAsync` is the remedy, and it **nets rather than mirroring
entry by entry**. Netting is the only form that is also correct when reversals are already present:
a void from Approved has none, so the net *is* the sum of the originals and the behaviour is
unchanged; the Opening Balance case has two, and the net is exactly the posting still outstanding.
Entries are grouped by `LocationId` first, so a document whose location changed between postings has
each location's balance reversed where it was posted — phase 35b's rule applied across entries
rather than within one.

**The reusable question** is not "does this document post twice?" but "which readers would a second
entry break?" — and the way to answer it is to grep for the pair, not to remember.

## A settlement folds at the rate of what it settles (phase 36)

Phase 31 recorded, as a carried limitation, that the credit-limit comparison does not convert
currency. It could not be fixed on its own: the check compares the new document against
`ContactLedgerReader`'s running total and `Contact.CreditLimit`, and the reader summed a USD invoice
and an NPR receipt as though they were the same unit. So the fix is the whole family folding to base,
each document at its own stored rate.

The trap is the payment. Fold it at **its own** rate and a fully settled invoice keeps a residual
balance: the invoice booked 13,300 at 133, the receipt relieved 13,000 at 130, and the customer is
shown as still owing 300 — a real number, but it is the *realised exchange difference*, which
`PaymentForexCalculator` books to the forex account at Approve, and which has no business in what a
customer owes. So a payment folds **allocation by allocation at each target document's rate**, with
only its unallocated remainder at its own. The ageing side folds each document's *net* at that
document's rate, which is exact because a cross-currency allocation is refused outright, and the two
readers therefore still agree.

The general shape: **when you convert a relieving amount, convert it at the rate of the thing being
relieved.** Converting it at its own rate silently moves a P&L figure into a balance.

## Patching two reports into agreement is not the same as making them agree (phase 36)

Phase 9 built the Ageing Summary; phase 26b built Invoice Age / Purchase Bill Age. Two
implementations of one netting. They drifted twice — over JournalVoucher-sourced allocations, and
over bucketing from the due date rather than the document date — and phase 31 patched both by editing
the older handler to match the newer one.

That left them agreeing *by coincidence*, and still disagreeing about the question nobody had
compared: **which documents are ageable at all.** The summary saw neither a contact-tagged Journal
Voucher nor a contact's own opening balance, both of which the per-document report had listed since
26b. No test caught it because each report's tests only ever looked at that report.

`OutstandingDocumentReader` now answers for both, and `AgeingReportsAgreeTests` reads *both reports
on the same data* — including a test that every bucket equals the sum of the document rows falling
in it, because agreement in total can hide two compensating errors. Making them agree changed the
older report's output for any tenant with a tagged voucher or a contact opening balance, which is
stated in the handler's own doc comment rather than slipped in.

## A stored set nobody can see (phase 36's product-to-location)

The reference product's New Product form has a Location multi-select, default *All*. Its Products
grid has no LOCATION column and no location filter, so **nothing in the product's own UI reveals what
the field does** — which is why phase 35b deferred it rather than storing a set nothing enforces
(phase 31's dead-setting lesson).

One write settled it. A product scoped to *POS Retail* alone: the Invoice form's line picker returned
**No data** while the header location was HeadOffice, and offered the product the instant the header
was switched. `performance.getEntriesByType('resource')` showed why — one
`products-minimized?…&location_id=<the document's location>` call per switch, so the filtering is the
**server's**, keyed on the document's location, not a client-side hide.

Two details the experiment also settled: an **empty set means every location** (the control renders
`All` when nothing is ticked and carries no required marker), and the **Products grid asks for no
location at all**, so a restricted product stays findable and editable there. What it did *not*
settle is whether a line naming an out-of-location product is refused at save — so that is not built,
because rejecting would invalidate documents already saved.

## Seeding and tooling traps added by phase 36's E2E

- **`sqlcmd -i` cannot take a forward-slash absolute path.** `-i C:/…/verify.sql` fails with *"The -E
  and the -U/-P options are mutually exclusive"* — it parses part of the path as options, so the
  error names authentication and the cause is the path. Copy the script somewhere relative and run it
  unchanged. (`-Q` with the same absolute path is fine.)
- **`POST /auth/register` needs `turnstileToken`** as well as phase-31's `phone`. Any non-empty token
  passes against the dummy secret.
- **`POST /organizations/{id}/invitations` takes `roleId`, not a role name** — the system Member role
  is `00000000-0000-0000-0001-000000000002`, readable from `GET /roles`. A `role: "Member"` fails as
  `'Role Id' must not be empty`.
- **An empty id still surfaces three steps later.** A re-run of a seed script 409'd on an
  already-created account group, left the id empty, and the failure appeared on the *payment* as
  `Failed to read parameter "PaymentRequest request" … as JSON`. Create a fresh organization per run
  and assert every captured id is non-empty before continuing — phase 28's trap, and it will keep
  coming back until seeds are idempotent.
- **The Angular suite times out nondeterministically under machine load.** Two runs of the full
  43-file suite failed 2 and then 7 tests — always the *first* test in a file, always at exactly
  5000 ms, including files the change never touched — and the third passed 301/301. Every failing
  spec passed alone. Re-run before chasing it.

## A shortfall is a layer, and its cost is an assumption (phase 37)

Until phase 37, `StockLedgerService.ConsumeAsync` threw a 409 whenever a document would take more
than the FIFO layers hold. The tenant setting that is supposed to govern this — *Negative Item
Balance: Reject / Warn / Do Nothing*, built in phase 31 — therefore had one real branch: confirming
the Warn dialog got you the engine's 409 anyway, so Warn and Do Nothing were both Reject wearing a
different hat. The screen was complete, the command was wired, and the engine three layers down
refused: phase 31's "a tenant field is reachable only if you can name the command that writes it and
the screen that calls it" has a third clause.

What negative stock *is* matters more than that it is allowed. It is a **shortfall layer** — a
`StockLedgerEntry` whose `QuantityIn` and `QuantityRemaining` are both negative — so that the sum of
`QuantityRemaining`, which every stock figure in the codebase derives from, goes negative by exactly
the amount owed with no special case anywhere. The FIFO walk already filtered `QuantityRemaining > 0`,
so you cannot sell out of a debt. `Fill` walks it back towards zero and never past; `QuantityIn` is
left alone, exactly as `Consume` leaves it, because it is the layer's original size and the kardex
reconstruction depends on it.

Its unit cost is the product's **last known cost in that warehouse** — the newest positive layer, by
the same `TransactionDate` then `CreatedAt` ordering the FIFO walk uses — and zero when the product
has never been received there. Shortfall layers are excluded from that lookup: chaining an
assumption off an assumption compounds it.

`ConsumeAsync` takes `allowNegative` rather than reading the setting, because a Warn verdict also has
to have been *confirmed*, and only the command handler knows whether it was. Two callers pass true
(Invoice, Production Journal — the two that already consult `IStockAvailabilityPolicy`). Warehouse
Transfer, Inventory Adjustment's Decrease side and Debit Note stay hard rejects, each for its own
recorded reason rather than by inheritance from "the setting is global".

## The cost catch-up has to reach three places, not two (phase 37)

A shortfall is issued at an assumed cost and covered later at the real one. The difference,
`filled × (real − assumed)`, is the **cost catch-up**, and it is the number that decides whether
phase 25's conservation law survives negative stock. It has to move all three views of stock value
at once:

1. the **FIFO layers** — `Σ QuantityRemaining × UnitCost`, shortfall layers included (negative on
   both counts, so a positive product);
2. the **Inventory account** in the general ledger;
3. the **movement history** — In value minus Out value, which is what every dated stock report
   reconstructs from (phase 26c) and what a report for a closed period answers from.

Any two of them can be made to agree by patching one of them. `StockConservation.AssertHoldsAsync`
asserts all three together, which is phase 36's lesson in a new place: two things patched into
agreement agree by coincidence.

**On the ledger side it is its own GL entry**, against the same `(SourceDocumentType,
SourceDocumentId)` pair as the covering document. Eleven call sites can produce one and they build
their entries through six different `IGlPostingRule`s, so threading one more optional amount through
all six inputs, rules and tests would have been the same two lines in six places (phase 33's
copy-N+1 trap). That shape is only available because phase 36 had already replaced every
`SingleAsync` over that pair with `SourceDocumentGlEntries` — the tool built for phase 36's own
problem is what made phase 37's design possible, and phase 37 finished the sweep, deleting
`GlJournalEntry.PostReversalOf` once it had no callers left.

It posts to the tenant's **Inventory Adjustment** account. Charging it back to whichever account
originally took the wrong figure would mean remembering, per shortfall layer, which document
consumed it and which account that document's rule debited — and one shortfall can be filled by
several receipts across periods. A new tenant-default account would have owed a migration, a screen
and a backfill for a figure that is zero on every tenant that never oversells (phase 29: an
unscreened default account is an unreachable one).

**On the movement side it is a value-only row.** `StockMovement.ValueAdjustment` is non-zero on a
row whose `Quantity` is zero, and `Value` is `(Quantity × UnitCost) + ValueAdjustment` so that no
caller has to distinguish the two kinds. The two alternatives are both worse and both tempting: a
matched In/Out pair of the filled quantity needs no schema and nets to the right value, but inflates
the In and Out **quantity** columns of every movement report — a wrong quantity is more visible and
far less defensible than a wrong value; and folding the catch-up into the receipt's own unit cost
keeps one row while misstating what the receipt cost.

One more consequence worth knowing: the catch-up's period is the **fill's**, not the sale's. That is
the only answer that does not restate a closed period, and it does mean a margin report for the
earlier period keeps the assumed cost.

## A return relieves at the cost the layers give up (phase 37)

Phase 6 modelled a Debit Note's Inventory credit as the **return price**. Phase 29 found half the
gap — the capitalised freight riding in the returned units — and patched that half with a release
leg. The other half is the price itself: `ConsumeAsync` relieves whatever FIFO chooses, which need
not be the document being returned against. Return five units to the supplier who sold them at 20
while an older 14 layer is still on the shelf, and the ledger gives up 70 while the old rule credited
Inventory 100 — a permanent 30 drift, on an entry that balanced perfectly.

This is phase 36's settlement rule in a second costume: *a relieving amount folds at the rate of the
thing being relieved*. So Inventory is credited what the layers actually lost (landed cost already
inside it — the phase-29 release leg's Inventory half is deleted, or the freight is relieved twice),
Accounts Payable is debited the return price the supplier is crediting, Landed Cost Clearing is
debited its released share, and the difference is a real gain or loss on the return.

**Derive that difference as the plug that balances the entry**, never as a separately computed
figure. It is the only construction that keeps `sum(Debit) == sum(Credit)` under the currency fold
(phase 28's rule that a rule's balancing leg is a sum of the others) and absorbs the rounding residue
in the same place. Demand the account it posts to only when the plug is non-zero: a return whose
price equals its FIFO cost is the ordinary case and must not start requiring an account every such
return has managed without.

The clearing unwind is the same arithmetic stated twice: a full return takes the account to zero, a
partial one leaves exactly the share of the accrual the unreturned units still carry.

And the mirror question is worth asking out loud rather than skipping: **a Credit Note needs none of
this**, because a sales return *adds* stock at the cost its source invoice recorded when that stock
left — a figure stored on the invoice line, not one FIFO picks. What it puts into the ledger and what
it credits COGS are the same number by construction. The problem is specific to relieving.

## A NOT NULL column's default is safe exactly when it is true of the existing rows (phase 37)

Phase 31's rule — "a non-nullable column on a populated table needs a hand-written backfill, because
the scaffold's `DEFAULT '0001-01-01'` back-dates every row" — is not about NOT NULL columns. It is
about defaults that are *false about the data already there*. `StockMovement.ValueAdjustment` ships
NOT NULL with `defaultValue: 0m` and no backfill at all, and is correct, because every movement
written before phase 37 carried its whole value in quantity times unit cost: zero is what those rows
mean. Ask what the existing rows would say if they could answer, not how many of them there are.

## Seeding and tooling traps added by phase 37's E2E

- `PUT /organizations/{id}/general-settings` takes six enums, and three of the guessable names are
  wrong: it is `RecentSellingPrice` (not `LastSellingPrice`), `ExclusiveOfVat` (not `WithoutTax`) and
  `AccountingMovement` (not `None`). A wrong member fails as
  `400 "Failed to read parameter … as JSON"`, naming no field — phase 35b's trap, and the cure is to
  `GET` the resource first and echo its own values back.
- A negative-permission proof for a key the **Member** role legitimately holds (here
  `Purchasing.PurchaseBill.Approve`) needs a **custom role with no grants at all**, since system
  roles cannot be edited (409). `POST /organizations/{id}/roles` takes `{name, description}` and a
  fresh role starts with zero grants, so it is a one-call setup.
- The prints-and-returns bash trap is easy to walk into even while writing a comment warning about
  it: a helper that echoes a progress line *and* is called under `$( )` returns the progress line.
  Have it set a global. (Same family as phase 34b's.)
- A heredoc in the Bash tool still mis-parses a Python patch script containing triple-quoted
  strings, well below the size the gotcha names. Write the script to a file with the Write tool.

## A Minimal API binds an array from the body on a POST (phase 38)

`POST /export-jobs?categories=Products&categories=Payments` bound `categories` to **null**. The
command's fallback — "no selection means every category", which is what the button meant before the
parameter existed and is the right default — then ran all eight sheets, while the job row and the
screen both said two.

Nothing caught it. It compiles. The 1,035 Application unit tests construct the command directly and
so never touch binding. The Angular suite asserts the service sends the right `params`. Only a real
HTTP call against the running API showed it, which is the entire argument for CLAUDE.md's manual-E2E
bar existing at all.

**The rule**: a Minimal API binds a **complex or array** parameter from the request *body* by
default on a POST, and from the query only when told. `[FromQuery]` on the array is load-bearing.
The trap is that the two `DateOnly?` parameters sitting right beside it bind from the query with no
attribute at all, because a **simple** type does — so the endpoint looks internally consistent and
half of it is wrong.

This is phase-27b's *"a trailing optional parameter reaches nothing until the Api's own request
record carries it too"* in a third costume, and phase-32's read-path gap in a fourth: the write path
looked perfect at every layer a test could see.

## EF refuses a set operation after a client projection (phase 38)

`SalesDocumentExportReader` began as two LINQ queries ending in `select new DocumentLineRow(...)`,
`Concat`-ed and then ordered. It threw at run time:

> Unable to translate set operation after client projection has been applied. Consider moving the
> set operation before the last 'Select' call.

A constructor call is a **client** projection, and a set operation cannot follow one. This is not an
InMemory quirk — the relational provider refuses it too, so it is one of the rare EF failures the
unit suite catches honestly rather than hiding.

**The remedy**: `Concat` while both halves are still anonymous — an anonymous type with identical
member names, types and order unifies across the two queries — then build the shared record from the
materialised page. The shared record earns its keep either way: two document sheets with fourteen
identical columns would otherwise be two chances to drift, and each reader's own test only ever looks
at its own sheet.

## A dry run that reports correct files as broken (phase 38)

The near-miss worth writing down, because it would have passed every test that did not specifically
pair a hierarchical importer with the validate pass.

A pre-commit review validates rows **without writing**. A hierarchical importer resolves a row's
parent by name against the database. Put those together and a file whose parent appears in a *later*
row — which the reference product's own template explicitly permits, and which phase 38's sequencer
exists to make work — resolves to nothing during validation. The review would have reported a
perfectly correct file as full of errors, and then applied it successfully on Confirm.

The symptom teaches users to ignore the review, which is worse than not having one.

**The fix names the difference instead of hiding it.** `ImportRowContext.PendingKeys` is empty during
the apply pass (the sequencer has already created those parents, so the ordinary lookup is correct)
and holds every in-file key during validation. An importer that cannot find a parent asks
`WillCreate` before rejecting, and answers a yes with `ImportRowPlan.Provisional` — a plan that
**throws if executed**, so a later caller cannot lose the distinction by sending one whose parent
reference quietly resolved to "no parent", which would flatten an imported tree and look like a
success.

## A template generated from the document in front of you (phase 38)

Phase 29 recorded the Purchase Bill's product-wise Additional Cost grid as offering "an Import (a
bulk paste of the grid)". Read live on 2026-09-12, it is a **file import**: a drawer with a drop
zone, an instruction to download the template first, and a **Download Template (.xlsx)** button.
(The button is inert until the bill has product lines and the matrix has a product, which is why a
first pass looked like a dead control.)

The template's bytes are unlike the other eight in this codebase. One sheet; row 1 is `Products` plus
**one column per tenant cost term**, in the tenant's own order; rows 2+ are **that bill's own product
lines**, pre-filled, with the amount cells blank; no instruction block, no sample row, no `**`
marker. It is generated from the bill in front of you, because a landed-cost matrix has no meaning
away from its bill.

Two consequences: it is **not** an `ImportTemplateDefinition` (that type is fixed columns, one sample
row and an instruction block — bending it would have meant dynamic columns and N sample rows, i.e. a
different type wearing the same name), and it is **not** an `ImportJob` (nothing is written, so there
is nothing to resume, and a background job would put the answer somewhere the unsaved form cannot see
it).

The wider lesson is phase-8f's, again: a one-line note about a control nobody operated is a guess,
and the guess had the mechanism wrong, not merely the details.

## Import ordering is a property of self-reference, not of parents (phase 38)

The roadmap grouped Account, Product Category and Account Group as three importers needing
intra-file ordering, and said to solve it once rather than three times. Two of them need it.

`ProductCategory.ParentCategoryId` and `AccountGroup.ParentGroupId` point at **their own type**, so a
row's parent can be another row of the same file. `Account`'s "Account Group" column points at a
*different aggregate*, which an Account file cannot contain and which must already exist — there are
no edges inside an Account file at all. Giving it the sequencer would have implied a relationship the
model does not have, so `AccountImporter`'s doc comment says why it is absent, where somebody would
otherwise "fix" it.

The same distinction decides the failure modes. A **cycle** or a **duplicate key** is a property of
the file and fails the whole job with its rows named, importing nothing — half-importing a tree
leaves a catalogue in a state neither the tenant nor the file describes, and the user cannot correct
the file and re-upload, because the good rows would then collide. An **unknown parent** is one row's
error, because the sequencer deliberately only knows about edges inside the file: a name no row
claims is external, and guessing would turn a single bad cell into a whole-file failure.

## Sanitise by re-emission, not by filtering (phase 39)

`Domain/Common/RichText` is this codebase's one rich-text sanitiser, and it never asks "is this tag
safe?" or "is this attribute safe?" and passes the answer through. The input is parsed into a
`RichTextNode` tree whose only attribute slot is a four-valued enum, and the output is generated from
that tree out of string constants the emitter owns, with every text run escaped on the way.

The property that follows is what makes a hand-written parser acceptable at all, and it is worth
being able to state before you touch the file:

> A bug in the tokenizer can produce wrong **formatting**, and cannot produce an attribute, a tag
> name or a URL that came from the input. The only user bytes that survive are text-node characters,
> and those are escaped.

Alignment looks like an exception and is not. The parser matches `text-align` against a closed set of
four keywords and then **discards the string**; the emitter writes one of four constants it owns. No
byte of user input reaches an attribute position. If you ever find yourself wanting to keep a
declared value rather than a constant that matched it, you have left this design and need the rest of
its argument again.

The corollary for tests: assert the blanket property, not the payload list. "These payloads are
removed" is satisfied by any blocklist, one payload at a time. *The output's only attribute is the
one the emitter writes* fails for a new evasion technique without anybody having had to think of it
first.

## Sanitise on write, in the Domain (phase 39)

Three places were available: at render, in the handler, in the Domain setter. It runs in the Domain
setter, and the reasons are not interchangeable.

**Not at render**, because that puts the obligation on every future read path, and phase-35a's
finding is that read paths are precisely what a sweep forgets — its write-path guard stayed green
while 14 of 15 detail DTOs and all 5 conversion templates dropped the same field. A sanitiser
everybody has to remember is a sanitiser somebody will not.

**Not in the handler**, because a Domain caller could then store markup that skipped the gate, and
the Domain is where the invariant belongs.

**In the setter**, so stored content is already safe and nothing downstream has to know. The cost is
that `Sanitize` must be **idempotent**, because a document is loaded into the editor and saved again
on every edit — a non-idempotent sanitiser rots a field visibly over a few saves, ampersands
doubling and whitespace growing. That is a product requirement, not a nicety, and it is asserted over
the whole adversarial corpus rather than over a happy path.

## A rich-text grammar is its renderer's capability list (phase 39)

The editor's toolbar has eleven buttons because that is what `RichTextPdfRenderer` can draw. The
reference product's own editor offers five more things — text colour, font family, font size, tables,
images — and every one is dropped for the same reason: the PDF is the copy a customer receives and
argues about, and an editor offering formatting the printed copy silently discards is worse than one
that offers less.

So the order of work is fixed, and it is the opposite of the intuitive one: **decide what the
renderer can draw, then build the toolbar.** Widening the grammar without widening the renderer
produces a field that looks one way on screen and another in print, and nothing in the type system
will tell you.

What the grammar does instead of dropping content is also load-bearing. `<a>` unwraps and keeps its
text (a link's words are content; its destination is an attribute, and no attribute survives);
`<img>` disappears; a `<table>` becomes one paragraph per row with cells space-separated, because a
pasted price list is data and dropping it silently would be the worse failure. The one family that
must lose its **content** as well as its tag is `script`/`style`/`iframe`/`svg`/`math`/`template`/
`noscript`: an unwrapped `<span>` must keep its text and a dropped `<script>` must not leave
`alert(1)` standing as visible prose.

## Two implementations of one rule need a table, not a reading of each other (phase 39)

There are two rich-text sanitisers and there have to be: the server's decides what is stored, the
client's decides what the user sees while typing. Without the client's, pasting a coloured table from
Word shows a coloured table, the save returns a plain paragraph, and the field appears to have eaten
the content.

`web/src/app/shared/rich-text/rich-text-cases.json` is the contract both are pinned to — read by the
Angular spec and linked into `Domain.UnitTests` as an **embedded resource**, so a moved or deleted
file is a build error rather than a green test over nothing. This is phase-26b's arrangement for
`BsCalendar` and its `bs-date.ts` twin, applied to the second pair of twins.

It earned its place within the hour: it caught the two halves disagreeing about collapsing runs of
whitespace. The client's `DOMParser` collapses natively; the server's tokenizer was replacing each
whitespace character with a space and not collapsing runs. Invisible until somebody indents their
markup, at which point the PDF prints a paragraph pushed halfway across the page.

The table carries **only well-formed input**, deliberately. The two parsers reach the tree by
different routes — one tokenizes by hand, one uses `DOMParser` — and they are not required to agree
about how to *repair* malformed markup, because the server's answer is the one that gets stored.
Pinning repair behaviour would pin two HTML parsers to each other rather than to a contract.

## Read an uploaded image's format from its bytes (phase 39)

The reference product's logo rules are "JPG/PNG/GIF, min 300×300, max 5 MB". Two of those can be
checked from the upload's declared content type and its length — and both of those are written by the
client, so neither is worth anything against a file that is not what it says it is. The organization
logo is embedded in every PDF the tenant sends a customer and served back to every browser that opens
the profile page, which makes "is this actually an image" the question that matters.

`Domain/Common/ImageHeader` reads PNG's IHDR, GIF's logical screen descriptor and JPEG's SOF marker
chain — about sixty lines, no pixels decoded, no dependency. **A parser that can find the dimensions
has already answered the format question**, so the two checks are one parser and there is no way to
have one without the other. The stored `LogoContentType` comes from those bytes, so the serving
endpoint never echoes a client's claim back to a browser.

Two details the tests pin because they are where this gets written wrong. A JPEG's frame header sits
after its APPn segments, which carry EXIF and can run to kilobytes, so the parser walks the marker
chain rather than reading a fixed offset. And **DHT (0xC4) sits inside the 0xC0–0xCF range and is not
a frame header** — reading one as a frame yields confident nonsense rather than a failure.

The renderer re-checks with the same parser rather than trusting the upload, because QuestPDF throws
for an undecodable image at `GeneratePdf` time — *after* composition, so there is no try/catch around
the draw call that would help — and an organization whose stored logo has somehow gone bad still
needs its invoices to print.

## A guard stops covering what it was written for, silently (phase 39)

`SearchSweepGuardTests` exists to notice a paginated list nobody gave a search box. It recognised a
"paginated list query" as one returning `PagedResult<T>`.

`ListTasksQuery` and `ListDealsQuery` predate that type and return their own
`{Rows, Page, PageSize, TotalCount}` record. They were therefore invisible to the guard — and they
were **exactly** the two lists phase 39 had to give a search box. A guard whose definition is
narrower than its subject does not announce that; it just goes on passing.

Widening it to recognise the envelope by **shape** rather than by type surfaced seven more
previously-invisible queries. All seven turned out to be the rule's own exempt case — a panel already
scoped to one parent row, or a sequence whose rows only mean anything in order — which is the answer
rather than a shortcut: the point of asking the question of every list is that most lists have a good
reason. The one that would genuinely earn a term (`ListSmsLogsQuery`) is named in the exemption with
its re-entry condition rather than left as a silent gap.

The generalisable half: when a guard's predicate names a *type*, ask what the predicate would miss if
somebody had solved the same problem a different way before that type existed.

## A Domain invariant reached through the API is a 500 (phase 39)

`EmailSendLog.Queue` refuses a `BalanceAsOfDate` on a context that has no letter to attach. Correct
as an invariant, and correct to keep — and an invariant reached through an endpoint surfaces as
`{"title":"An unexpected error occurred.","status":500}`, which tells a caller nothing and reads to
an operator like a server fault.

The fix is not to weaken the invariant. It is to add the same rule to the **validator**, so an
ordinary caller mistake is a 400 that names the field, and leave the Domain check as the backstop for
the caller that skips the pipeline.

Worth knowing where this is visible: only where the two layers are exercised together. Every handler
test passed, because a handler test constructs a valid command. This surfaced in the manual E2E,
which is the only thing that sends the body a person would actually send.

## `curl -F name=<value` reads the value as a file path (phase 39)

`-F 'body=<p>Payment due…'` makes curl try to open a file named `p>Payment due…`, and it reports exit
26 with HTTP 000 — which is exactly the symptom phase 30 recorded for its own failed `-F` file
upload, and so reads as the same problem. It is not: `--form-string` sends the value literally and
works.

The rule: any `-F` value that might begin with `<` needs `--form-string`. Terms, email bodies and
anything else rich-text-shaped begin with `<p>` essentially always.

## The Angular app routes by path; the reference product routes by hash (phase 39)

Three browser passes bounced to Sign In against `https://localhost:4200/#/organizations/…`. The
cookie was fine — `fetch('/api/auth/me', {credentials:'include'})` from the page returned 200 through
it — and `location.href` read `https://localhost:4200/login#/organizations/…`: the app uses
`PathLocationStrategy`, so the whole route was a fragment on the login page.

The reference product's URLs are hash-based (`me.tiggapp.com/erp/#/…`) and appear all through
`erp-module-scan.md`, which is what makes this easy to carry over without noticing. A bounce to Sign
In has no other symptom, so check `location.href` before suspecting the cookie.
## A quoted heredoc still eats backslash escapes (phase 39)

CLAUDE.md's standing rule is that a `cat > file <<'EOF'` heredoc in the Bash tool is silently
truncated or mis-parsed, and that the Write tool is the answer. Phase 39 found the sharper reason,
twice, and the second time is the one worth recognising on sight.

A **quoted** delimiter (`<<'PY'`) is supposed to suppress all expansion. It does not suppress this
shell's handling of backslashes. A Python script embedded in one wrote `line.rstrip("\n").split("\t")`
and the escapes arrived as a literal newline and a literal tab *inside the string literal*, producing
`SyntaxError: unterminated string literal` — annoying, but it points at itself.

The same heredoc then turned an MSBuild path — `..\..\web\src\app\shared\rich-text\...` — into
`..\..\web\src`, a BEL byte, `pp\shared`, a CR, `ich-text`: `\a` and `\r` had been interpreted. That
one does **not** point at itself. It surfaced as
`'', hexadecimal value 0x07, is an invalid character` from the XML parser, several steps later, in a
file whose generator looked perfectly correct.

So: any embedded script containing `\n`, `\t`, or a Windows path goes through the Write tool. And if a
generated file fails to parse with a character-code complaint, suspect the heredoc before suspecting
the generator.

There is a third instance of this in the phase's own history, which is the reason it is written down
rather than filed as bad luck: the first draft of *this very section* was appended through a heredoc,
and every escape in it was eaten.

## A live region created holding its text announces nothing (phase 40)

The app's house idiom for a status message was, in 145 templates:

```html
@if (errorMessage()) {
  <div class="alert alert-danger …" role="alert">…{{ errorMessage() }}…</div>
}
```

Every attribute is right. Nothing is announced.

A live region is announced when its *contents change*, and ARIA requires the region to already be in
the accessibility tree when that happens — what the screen reader is watching is the region, not the
document. An element inserted into the DOM with its text already inside it is **one** mutation, and
there was no region beforehand to watch.

Confirmed against the running app: a `MutationObserver` on the New Invoice form, with Save pressed
and no customer selected, recorded exactly one event — *live region ADDED to DOM, already carrying
"Select a Customer."* — and never a change inside an existing region.

The fix is structural, not an attribute: the region moves outside the `@if`. `app-status-banner`'s
host is always rendered and only the visible alert box inside it comes and goes, which is a change to
a region that was already being watched — verified by the same observer after the sweep. So call sites
render `<app-status-banner [message]="…" />` **unconditionally**; wrapping it in the `@if` it used to
have puts the bug straight back, which is what the sweep-guard assertion exists to prevent.

Two corollaries worth keeping:

* **`aria-atomic="true"`**, or an AT may read only the words that differ from the previous message.
  Two validation errors sharing a prefix then read as gibberish.
* **A spinner is not a status message.** 4.1.3 is about the *result* of an action; "loading" announced
  on every keystroke of a search box drowns the result when it arrives. Every `aria-live` region in
  this app announces a result *count*, never a request in flight.

And the reverse error, which the same sweep found six times: `role="alert"` on **unconditional page
furniture** (the migration page's explanatory note, four register-page notices, the warehouse-cap
notice) fires on page load and interrupts whatever is being read to recite a paragraph. Standing prose
is not a status message either.

A footnote on scoping: the sweep was scoped by grepping `role="alert"` and found 163. The guard, once
written, found **seven more** spelled `role="status"`. The grep that scopes a sweep is itself a
predicate that can be too narrow.

## The focus ring is a colour, and 34a's rules did not measure it (phase 40)

Phase 34a's `contrast-rules.ts` asks one question of every colour pair in the app: does the *text*
clear 4.5:1. That predicate names a **property**, so the file said nothing about the other thing WCAG
puts a contrast floor under — SC 1.4.11 Non-text Contrast, which requires **3:1** for a focus
indicator against what it sits on.

Bootstrap paints `:focus-visible` as `box-shadow: 0 0 0 .25rem rgba(<the control's own tone>, .5)`.
34a had already established that those tones clear 4.5:1 against pure white *and only just*; halved by
the alpha and composited over white or `#f8f9fa`, they measure **1.21:1 (btn-light) to 2.53:1
(btn-dark)** across the twelve variants this app uses. Not one reaches 3:1, which is why this is not a
tuning problem — there was nothing to tune.

`styles.scss` therefore replaces the ring for everything focusable with a single 2px opaque `#0a58ca`
outline at `outline-offset: 2px`, clearing the box-shadow so two indicators cannot stack. The offset
matters: it puts a 2px gap of page background between the ring and the control's fill, so the pair the
ring must contrast against is the *page* on every variant at once — 6.11:1 on the body, 8.6:1 on a
card. The left nav's hand-written version (34b) becomes the general case rather than a special one.

`FOCUS_RING_RULES` computes all thirteen numbers and the guard asserts **both** halves: that every
stock ring fails, and that the shipped stylesheet still paints the replacement. The first is the
unusual assertion and it is deliberate — it is what stops the rule being "simplified" away later on
the grounds that Bootstrap already has a focus style.

### Measuring it is its own trap

The first sweep read `getComputedStyle` inside a `focusin` handler and reported `outline: none` on
every control in the content area — an app-wide 2.4.7 failure that did not exist; the style had not
recalculated for `:focus-visible` yet. That is CLAUDE.md's phase-34b rule (*in the browser pane the
screenshot is ground truth*) in a second costume, and the screenshot is what corrected it. Measure
after a **real** key event, and look at the picture.

## An element that is not a control cannot be found by a scan for controls (phase 40)

The organization picker — the screen after sign-in, the only way into any organization — rendered each
row as `<div (click)="openOrganization(id)">`: no `tabindex`, no `role`, no key handler. A focusable
census found **six** focusable elements on a page with 114 rows, and not one of them was an
organization. A keyboard-only user could sign in and go no further; all 140 other screens sat behind a
control `Tab` could not reach. WCAG 2.1.1 Keyboard, Level A.

No guard in this codebase could have found it, and the reason generalises: `a11y-sweep-guard` asks
whether controls are *named*, `<th>`s are *scoped*, icons are *hidden*. Every one of those is a
property of an element it can see. It cannot ask why a click handler has no element around it.

So the check that finds this class of defect is **a census, not a scan**: enumerate what `Tab` can
reach and compare it against what the page offers. Two more turned up the same way — the document
inbox's `<tr (click)>` row selection (the preview pane could not be opened from a keyboard), and the
rich-text toolbar's eleven tab stops under a `role="toolbar"` that promises one.

The fix for a row that navigates is an `<a routerLink>`, not a `tabindex` and a key handler: a link
gets focus, `Enter`, middle-click and the right role for free.

## A label whose control disappears at runtime (phase 40)

Phase 34a asked the mirror question that found 121 orphaned date fields — *which labels name no
control?* — of **template source**. `ListChrome` rendered:

```html
@if (locationDocumentType()) {
  <span class="form-label …">Billing location</span>
  <app-location-list-filter … />
}
```

which is sound in source: a caption, then a control. But `LocationListFilter` renders nothing at all
on a tenant with one location or without the Multiple Locations feature — which is the **default**
tenant. So on most tenants, on all 22 chrome'd list screens, a visible label named nothing.

A source-level guard cannot see this, because the control is present in source and absent at runtime.
The rule that prevents it: **a control that hides itself owns its own label.** `ReportLocationFilter`
already did, which is why it never had the bug; `LocationListFilter` now does too, and the host chrome
renders no caption of its own.

The wider version, since a second tenant shape is what exposed it: a conditional control and its
caption must be governed by **one** condition, wherever that condition lives.

## An ARIA grouping with no name is worse than no grouping (phase 40)

25 containers in the app declared `role="group"`, `role="radiogroup"` or `role="tablist"` and named
none of them — including the All/Draft/Approved status filter on sixteen document lists and the
pagination control. An unnamed group announces "group" and nothing else: it adds a boundary a
screen-reader user has to cross, carrying no information about what is inside, which is strictly worse
than the plain `<div>` it would otherwise have been. Either name it, or drop the role.

Phase 34a's guard asked whether every *control* was named. Nobody had asked the same question of a
*grouping* — the same predicate-shaped blind spot as the focus ring, one level up the tree.

Related, and 34a's carried item #2 resolved: `role="group"` on a set of `btn-check` radios understates
what is there. The children are native `<input type="radio">`, so the browser is already doing the
roving arrow-key selection `radiogroup` promises — the role was the only missing part, and 34a's
hesitation ("a UI-behaviour question") was about a behaviour change that does not happen.

## An ordering may be offered when an index leads on it (phase 40)

Phase 34b built the list chrome's `Sort by` control and shipped it with no consumer, setting the
re-entry condition as "the first list whose default ordering someone complains about". Nobody can
check that, and waiting for a complaint is how a seam stays empty for six phases.

34c supplies a condition that *can* be checked and that is also the stronger rule: **an ordering may be
offered exactly when an index already leads on `(OrganizationId, <that column>)`.** 34c measured the
invoice list at 50 000 rows and found `(OrganizationId, CreatedAt)` is what makes it fast; adding
`ORDER BY Code` beside it would turn the same screen into a sort over the whole filtered set — and the
pager would hide how slow it had become, because page 1 still returns ten rows.

So a document list's menu is a reading of `TenantIndexConvention`, not a design choice: it builds
exactly two useful indexes per document, `(OrganizationId, CreatedAt DESC)` for the list screen and
`(OrganizationId, <business date>)` for the range filter, and those are the two options. An unknown
value is a 400 naming the field (`ValidateSort`), never a silent fall back to the default — a list
that quietly ignores the ordering it was asked for is phase 35a's read-side gap in a new place: the
control looks like it works and the rows never change.

## "Is this screen a list or a report" can be the wrong question (phase 40)

Phase 34b excluded `transaction-list-page` and `alert-list-page` from its chrome sweep because neither
had the standard `totalCount`/`page` signal pair, and left "a later phase should decide whether they
are lists at all". The question could not be answered because its premise is false: this codebase has
**three** list shapes — the paginated document list, the unpaginated configuration lookup, and the
report screen — and the two undecidable screens are one of the last two each.

What settles it is evidence already in the codebase rather than taste. `transaction-list-page` is
routed under `/reports/`, filed under *Reports > Accounting* in `nav-tree.ts`, gated by a `Reports.*`
key, and has an export — four things every report has and no list has; and its rows are a projection
across thirteen document types rather than an aggregate you can open. `alert-list-page` fetches every
row through `listAll`, carries an inline add/edit form, and its rows are an aggregate you edit — the
configuration-lookup shape, so it gets that shape's client-side search rather than the chrome.

When a binary classification resists, count the shapes that actually exist before answering.

## A backtick inside an inline `template:` literal (phase 40)

A prose comment written inside a component's inline `template:` string used backticks to name two
other components. The template literal ended at the first one. The three compiler errors that followed
all pointed at the `@Component` decorator (`NG1002: Incorrect number of arguments`) and none of them
at the comment.

Worth knowing twice over, because `a11y-sweep-guard`'s inline-template extraction has the same failure
mode by construction — its `TEMPLATE_LITERAL` regex stops at the first backtick — which is why it
asserts every extracted template is non-empty before asserting anything about its contents. That is
phase 34a's empty-stylesheet lesson applied *before* it bit rather than after.

## The dev server can serve a bundle older than the source (phase 40)

Two template edits were verified as "not applied" against a browser showing the pre-fix markup, while
`ng build` on the same source was clean. The dev server had failed a rebuild (a compile error since
fixed) and kept serving the last good bundle; the tell is the component's `_ngcontent-ng-cNNNNNN`
attribute being unchanged across an edit that should have recompiled it. Restart the preview. A green
`ng build` and a browser that disagrees means the browser is looking at something older, not that the
change did not land.

## Seeding traps added by phase 40's E2E

* Every create here returns **201**, not 200 — including `POST /organizations`.
* Configuration lookups are under `/api/organizations/{id}/configuration/...`, not at the org root; a
  `POST` to the root path is a 404 with no body, which reads like a missing organization.
* A fresh organization has **no warehouse, no unit of measurement, no product category and no
  product**, and an invoice line needs all four — even a Service line, and even on a Draft. The 400s
  name the field, so it is four rounds of seeding rather than a mystery, but it is four rounds.
* `POST /products` needs `categoryId` alongside `primaryUnitId` and `type`.

---

## A field that is dead on every tenant you can reach (phase 41)

Phase 33 Decision D retired three fields — Subscription Amount, the transaction quota, the product
quota — after reading `0.00` and `Standard ( 0 Txn, 0 Products)` on both reference tenants. The
reasoning was careful and correct in form: phase 31 had established that *a tenant-level field is
reachable only if you can name the command that writes it*, no command could write these, so building
the columns would hide the missing write path. Phase 32 had already added the caveat — a field being
dead on the tenant you looked at is a fact about that tenant — and the remedy it prescribed was
**another tenant, account or plan**. None was available.

**The conclusion was still wrong, and a third tenant was not what would have shown it.** Both tenants
were **free trials**. The vendor's public price list (tiggapp.com/pricing) sells three tiers —
Rs 15,000 / 20,000 / 32,000 a year — whose entire commercial difference *is* those three fields, plus
add-ons priced per additional 1,000 products and per additional 10,000 transactions. `0.00` and
`( 0 Txn, 0 Products)` is what a trial looks like, not what an unused column looks like.

So the rule generalises phase 30's ("a list sampled from a few screens becomes a wrong list — find the
**rule**") one level up: **a value sampled from two instances of one kind becomes a wrong fact about
the field.** Two tenants of the same kind are one sample, not two.

And the practical instruction is cheaper than the one phase 32 wrote: **before asking for another
tenant, read what the vendor publishes.** One page load settled the question *and* supplied the exact
ceilings, the exact prices, the whole add-on catalogue, and a one-sentence definition of the metered
unit (*"Active transactions refers to all transactions in which accounting entry are affected"*) that
no amount of tenant-reading would have produced — because it is a contractual term, not a screen.

The same page also answered two questions filed as open elsewhere: that read-only access past expiry
is something the vendor **sells** at 25% of the fee (phase 31 #5), and that multi-currency ships in
every tier, which is why phase 20f found Multi-Currency to be the one user-operable switch on an
otherwise read-only Features page — it is not an entitlement Tigg sells.

---

## A ceiling the constrained party can raise (phase 41)

`SubscriptionQuotaBehavior` refuses an Approve once the tenant has spent the transaction allowance its
plan sold it, and a Create once it has spent its product allowance. Both ceilings live on
`TenantSubscription`, and the command that writes them — `SetTenantSubscriptionCommand` — is gated by
`Tenancy.Subscription.Manage`, **which is seeded to the tenant's own Admin role**. One `PUT` sets any
plan, any end date and any ceiling. The same is true of phase 31's expiry.

This is not a mis-seeded permission, and moving the key does not fix it: **there is no other role to
give it to.** Every actor this codebase can express is a member of some tenant — every table carries
an `OrganizationId` discriminator, every permission is a tenant role permission, and there is no
cross-tenant entity anywhere. A subscription is inherently a **two-party** record, and the model has
one party.

Two consequences worth stating separately:

- **How to describe it.** These ceilings are an accurate record of what was sold and a real guard
  against a tenant drifting past it *unnoticed*. They are not a control that survives an adversary.
  Never write "enforced" without that qualification.
- **Where to say it.** In the behavior, in the command, and in the status doc — not only in one of
  them. The failure mode of a gap like this is that a later phase reads the behavior, sees a clean
  refusal, and builds something that assumes the number cannot move.

The general form: **before calling any tenant-level limit "enforcement", ask who can write it.** That
is phase 31's "name the command that writes the field and the screen that calls it" turned around —
here the writer *is* nameable, and that is exactly the problem. Re-entry condition: a vendor-side
console, or any actor outside `OrganizationId`.

---

## State two surfaces show and one of them changes (phase 41)

Phase 41 put a subscription warning in the shell header — it renders on every in-organization screen —
and reworked the Subscription screen to record a new term. Both read `GET /subscription`. Both were
individually correct, and every unit test passed.

Driving the app: recording a **Standard** plan updated the page (heading, amount, both usage meters)
while the banner directly above it still read *"366 days remaining in your trial"*. Two reads of one
fact, and only the one that issued the write refreshed.

This is phase 34b's rule one surface over — *a filter a screen displays but did not apply is worse
than no filter; anything global a screen both shows and sends must reload that screen when it changes,
from the first version* — with "global thing a screen shows" now meaning a banner rather than a filter.

The fix is a shared store (`SubscriptionStore`, on phase 35a's `BillingLocationStore` shape) that the
banner **reads** and the screen **saves through**, so the write's own response folds straight into the
signal both consume. That costs nothing extra because the save returns the same DTO the read does — a
property phase 31 deliberately created by extracting `ToDto`, and which is worth preserving for exactly
this reason.

**What to take from it:** no unit test finds this class of defect, because each component is right on
its own. The check is a question to ask at design time — *does anything else on screen show this, and
does it know I changed it?* — and a browser pass to confirm the answer.

---

## Two E2E traps that make a negative test vacuous (phase 41)

**1. `sqlcmd -S localhost` against a named instance returns nothing, silently.** The local server is
`DESKTOP-H0R00ME\SQLEXPRESS`; `-S localhost` connects to nothing and prints nothing, with no error on
stderr that the script sees. The verification-code read came back empty, `verify-email` 400'd, and the
symptom surfaced three steps later as a 401 on accept-invitation. Only the discipline of **printing
every status code** (phase 26c) made the 400 visible at all. Read the instance name from the
connection string rather than assuming.

**2. `accept-invitation` is an authenticated call, and registering does not sign you in.** An invited
user must `POST /auth/login` **before** `POST /organizations/memberships/{id}/accept-invitation`.
Accepting first returns 401 and leaves the membership `Invited` — after which the user's 403s look
like permission denials but are really "not a member of this organization", and
`AuthorizationBehavior` returns the *same message* for both.

That second one is the dangerous half, because it produces a **passing-looking negative test that
proves nothing**. The remedy is structural: a negative permission proof needs its positive half in the
same run. Phase 41's E2E asserts the same Member gets **200** on `GET /subscription` and
`GET /subscription-plans` (they hold `SubscriptionView`) and **403 naming
`Tenancy.Subscription.Manage`** on the `PUT`. The 200 is what makes the 403 mean *the key* rather than
*the tenancy*.

The companion check remains phase 12's: a 403 against a **nonexistent** organization id proves the
gate fired before the handler ever looked for the row — 404 there would mean the gate is decorative.

---

## A list long enough to be worth avoiding is a list too long to send (phase 42)

Phase 34c's version of this reads: *a materialised id list handed back to SQL becomes an `OPENJSON` parameter as long as the list*. That is the half you meet when a report loads a
period and then re-queries its children. Phase 42 met the other half, which is sharper and less
obvious: **the narrowing itself can be the cost.**

Every instance removed in phase 42 was written as an optimisation, and read like one.
`ContactAgeingSummaryQueryHandler` did not fetch all of a tenant's contacts; it fetched only
the ones with an outstanding document, by id. On the 50,000-invoice dataset that is 35,001 ids,
and the measurement is not close:

| | logical reads | wall time |
|---|---|---|
| `Contacts WHERE OrganizationId = @o AND Id IN (OPENJSON(@35,001 ids))` | **182,545** | 699 ms |
| `Contacts WHERE OrganizationId = @o` (ordered index scan, every row) | ~1,500 | — |

The worst case in the phase was worse than a join. `OutstandingDocumentReader` narrowed its
allocation lookup by 50,000 candidate document ids, which EF serialises into an `nvarchar(max)`
JSON parameter of about **1.8 MB** — against a `PaymentAllocations` table holding almost no
rows. That request measured **7,480 ms** end to end while the server reported roughly **1.2 s**
across every statement it ran. The missing six seconds were the client building and shipping the
parameter, which is why a wall-clock harness can see the symptom and never the cause: it looks
like application time, and there is nothing in the SQL to blame. Removing the narrowing took the
endpoint to **1,134 ms**.

Two remedies, in order of preference:

1. **Join the query, not the list.** Where the ids came from a query you still have, join that
   `IQueryable` and let the filter stay where the optimizer can see it — and if what you
   wanted was a total per parent, make it a store-side `GroupBy` so one small row comes back
   per parent instead of every child row. Phase 42 did this five times in
   `OutstandingDocumentReader` and three times in `SalesRegisterQueryHandler`.
2. **Drop the narrowing.** Where the result is read back as a dictionary with
   `GetValueOrDefault`, extra entries cost nothing: scope the query by the tenant and the
   thing that actually decides membership (the Contact Group filter, the document type) and let
   the lookup do the rest. Check the read side first — the test is whether an unasked-for row can
   reach the output, not whether it can reach the dictionary.

The threshold is not a number anyone should try to tune. It is a shape: **if you are about to send
more ids than the table you are sending them to has interesting rows, you have written a join by
hand, badly.**

---

## A page's rows are fetched before they are eliminated (phase 42)

Phase 34c carried two separate list findings — the offset tail (item #5) and search on a term
matching nothing (item #6) — and proposed a different remedy for each: keyset pagination for the
first, full-text indexing or a two-step handler for the second. Measured at the statement level
they are one defect.

`ToPagedResultAsync` asked SQL Server for whole entities in the same statement that ordered
and offset them. The engine takes the ordering index and does a key lookup into the clustered
index for **every row it is about to throw away**:

| statement (50,000 invoices, one tenant) | logical reads | CPU |
|---|---|---|
| `SELECT * … ORDER BY CreatedAt DESC OFFSET 0 FETCH 50` | 166 | 6 ms |
| `SELECT * … ORDER BY CreatedAt DESC OFFSET 49950 FETCH 50` | **153,705** | 513 ms |
| `SELECT Id … ORDER BY CreatedAt DESC OFFSET 49950 FETCH 50` | **571** | 24 ms |

Neither the offset nor the `LIKE` is the cost. The **projection** is. Ask for ids and the
whole thing runs inside an index that already carries them.

So `PagedResultExtensions.ToKeyPagedResultAsync` counts, takes the page's *keys*, then
fetches exactly those rows — `JournalReportQueryHandler`'s shape (page the keys, fetch
detail for the page) applied to a list instead of a report. Three consequences worth keeping:

- **It fits inside `PagedResult<T>`.** That is the answer to the keyset question 34c left
  open: keyset pagination would have changed the envelope, every list screen and both sweep
  guards, and it is **not needed** — its re-entry condition is now unmet, not deferred.
- **The key must be an `Expression`, never a captured `Func`** (phase-9 bug #1). The
  helper composes `keys.Contains(key(x))` from the caller's expression tree.
- **It composes onto the caller's query and never rebuilds one**, so it cannot lose the caller's
  `OrganizationId` — phase-35b's rule satisfied structurally rather than by care. A version
  that fetched by key alone would lose it and would pass every other test, so there is a test.

---

## A count of zero is a complete answer (phase 42)

Half of phase 42's search win is one branch, and it is the half that needed no cleverness at all:
when the count comes back zero, **do not issue the page query.** There is no page.

The reason this is worth a heading is what it replaced. The first design of
`ToKeyPagedResultAsync` took a `termApplied` flag, because 34c's carried item is about
*search* and the fix was going to switch on whether a search term was present. The measurement
removed the flag: on the scale dataset `status=Draft` matches nothing and cost **386 ms**,
for precisely the reason `search=ZZQQXX` cost **1,262 ms**. A filter that matches nothing
behaves the same way whatever it filtered on. The helper therefore asks the only question that
matters and one code path serves both.

The general form: **a branch that names the feature you came to fix is usually narrower than the
defect.** If it can be re-expressed as a question about the data rather than about the caller's
intent, it covers cases nobody listed.

---

## A ledger report's cost is the history before its period (phase 42)

After phase 42's conversion the Detail General Ledger is **faster over three years (1,825 ms) than
over one month (3,059 ms)**, and the General Ledger Summary always was (1,242 ms against
2,320 ms). That is not a measurement error and it is not paradoxical once stated:

> Every ledger report computes an **opening balance** before it does anything else, and an opening
> balance is an aggregate over `PostedAt <= fromDate - 1` — *all* of it. A period that starts
> later has more history in front of it.

On this dataset that query alone is **182,270 logical reads and 637 ms**, and it is a scan of the
whole `GlLines` table across every tenant, because `GlLine` still carries no
`OrganizationId` (phase-34c's carried item #7). 34c measured that design as costing nothing;
it costs nothing *for the statements*, which read the whole history anyway. For a dated ledger
report narrowed to a recent month, it is the entire bill.

Two things follow. **Probe the short period, not only the long one** — 34c's period-sensitivity
table ran 1 month / 1 year / 3 years and read as monotonic because the period term still
dominated; once it stopped dominating, the ordering reversed. And **do not read a report's
improvement off its longest period**: the longest period is now its best case.

---

## A store-side `Sum` over an already-projected record (phase 42)

The third appearance of the same family — phase 25's captured `Func`, phase 34b's static
matcher, phase 38's set operation after a client projection — and the one with the worst symptom,
because it is invisible to the whole test suite.

Phase 42's Detail General Ledger needed the balance an account had carried before the page's first
row, which is a `Take(n).Sum(...)` over the section's ordered postings. Written against a
query that had already projected into a `LedgerLine` record:

```csharp
var accountLines = from line in periodQuery … select new LedgerLine(line.Id, …, line.Debit, …);
var carried = await accountLines.Take(rowsBefore).SumAsync(x => x.Debit - x.Credit, ct);  // throws
```

EF has to construct the record server-side to read `.Debit`, cannot, and throws
`InvalidOperationException`. **The InMemory provider evaluates it in C# instead.** So all ten
of the phase's new tests passed — *including* the one that walks every page at `pageSize=1`,
which exercises exactly this branch and nothing else — while the real endpoint returned **500 on
every page after the first**. Page 1 worked because `rowsBefore == 0` short-circuits the
query entirely, which is the most misleading possible smoke test.

Only the E2E against SQL Server saw it. The fix is to keep the query on raw columns and project
after `Skip`/`Take`:

```csharp
var orderedLines = from line in periodQuery … select new { Line = line, Entry = entry };
var carried = await orderedLines.Take(rowsBefore).SumAsync(x => x.Line.Debit - x.Line.Credit, ct);
var pageLines = await orderedLines.Skip(rowsBefore).Take(wanted)
    .Select(x => new LedgerLine(x.Line.Id, …)).ToListAsync(ct);
```

The rule, stated so it covers the next member of the family: **a client projection is the end of
what the store can do with a query.** Anything store-side — a `Sum`, a set operation, a
further `Where` over a computed member — has to happen before it.

---

## A quoted heredoc eats backslash escapes when piped to an interpreter too (phase 42)

Phase 39 recorded this for `cat > file <<'EOF'`. It applies just as well to
`python - <<'PYEOF'`, and phase 42 hit it twice: a `chr(92) + 'n'` inside a Python string
literal arrived as a **literal newline**, so an anchor string that looked correct in the script
failed its `count() == 2` assertion against a file that visibly contained it twice.

The reliable form, and what the rest of phase 42 used: **write the script to a file with the Write
tool and run that file.** Where a literal is unavoidable inline, `chr(10)` cannot be eaten.

---

## Two small traps from phase 42's measurement

- **`sqlcmd` will not cast a `datetimeoffset` literal without being told to.**
  Backdating a subscription term failed with *Conversion failed when converting date and/or time
  from character string* until the value was wrapped in
  `CAST('2023-07-01T00:00:00+00:00' AS datetimeoffset)`.
- **`dotnet build` cannot copy over a running API.** It reports
  `MSB3027 … locked by: ErpApp.Api (PID)` and fails the whole solution build. It matters
  more than it sounds in a measurement phase, where every pass needs a rebuild between it and the
  last — stop the API first, and expect to restart it before each pass.

---

## Gotcha entries as written in CLAUDE.md before the 2026-09-14 trim (the 39 longest)

Shortened in CLAUDE.md on 2026-09-14 and kept here verbatim; each has its narrative under the phase headings above.

- An extraction is not done until the copies it replaced are deleted: `GrantedPermissionReader` said it had replaced two inlined joins and had not, so both missed phase-32b's `LocationId == null` filter and read a branch grant as organization-wide. Before writing copy N+1 of a pattern, grep that copies 1..N were retired (phase-33).
- An append-only fact row (`GlJournalEntry`, `StockMovement`, `StockLedgerEntry`) points back with `(SourceDocumentType, SourceDocumentId)` and nothing else, so it cannot be filtered by any of its document's attributes without a column; stamp at write time and have reversals **inherit** rather than re-derive (phase-35b).
- Every tenant-scoped table needs an index **leading on `OrganizationId`**; 18 had none, and they were exactly the documents, because master data got one free from its per-tenant uniqueness rule. `TenantIndexConvention` derives all three families and throws at model build for an entity it cannot classify (phase-34c).
- An index added for one access path changes the plan for **every other path over the same table**: `(OrganizationId, CreatedAt)` made the invoice list 10× faster and a non-matching search 1.8× *slower*, because the optimizer swapped one scan for a seek plus a key lookup per row. Re-measure the paths you did not change (phase-34c).
- A list may offer an ordering exactly when an index leads on `(OrganizationId, <that column>)` — `TenantIndexConvention` builds a document two, so a document list's sort menu has two entries and is a reading of the schema, not a preference. An unknown value is a 400 naming the field, never a silent default (phase-40).
- "One GL entry per Approved document" is a per-type habit, not an invariant: `SingleAsync` over `(SourceDocumentType, SourceDocumentId)` was already a 500 on a twice-edited Opening Balance line. Reverse what is **outstanding** — the net of every entry, grouped by location — via `SourceDocumentGlEntries` (phase-36).
- A dated stock report must derive from `StockMovement`, never from `StockLedgerEntry`: `QuantityRemaining` is decremented **in place**, so the FIFO table only ever answers "as of now" and a report for a closed period would silently answer today's question. Opening+In-Out over the append-only movements reconstructs both quantity and value at any date, and equals the FIFO figure today (phase-26c).
- An oversell leaves a **shortfall layer** (a `StockLedgerEntry` negative on both quantities, at the product's last known cost in that warehouse, zero if never received there) when the tenant's Negative Item Balance setting allows it; `ConsumeAsync` takes the *verdict*, never the setting, because Warn also has to have been confirmed (phase-37, replacing phase-26c's pin).
- Changing what a posting rule debits changes what every *reversal* of it owes: a Debit Note credits Inventory the return price while `ConsumeAsync` relieves layers at their landed cost, so phase 29's capitalised cost needed its own release leg or Inventory drifted above the ledger one return at a time (phase-29, phase-6 bug #3 again).
- Build a capitalisation leg from the value the ledger actually received (`layer value created − goods amount`), never the figure the user typed, and round each unit cost **once** at the ledger's own scale from the line's total landed value; the gap is the named residue (phase-29, phase-25's rule on a second aggregate).
- A return relieves at the cost the layers **give up**, which FIFO chooses and which need not belong to the document being returned against; credit that, debit the supplier the return price, and derive the difference as the **plug that balances the entry** (phase-37, phase-36's settlement rule in a second costume).
- A catch-up leg that eleven call sites can raise is its **own** GL entry against the same source document, not an optional amount threaded through six posting rules — which is only safe because phase 36 retired every `SingleAsync` over `(SourceDocumentType, SourceDocumentId)`; phase 37 finished that sweep and deleted `GlJournalEntry.PostReversalOf` with its last caller (phase-37).
- Rich text is sanitised **on write, in the Domain setter**, by *re-emission*: parsed into a tree whose only attribute slot is a four-valued enum, re-emitted from the emitter's own constants, so a tokenizer bug can produce wrong formatting and never an attribute, tag name or URL from the input. `Sanitize` must stay idempotent — a document is re-saved on every edit (phase-39).
- …and the mirror of that: **before calling any tenant-level limit "enforcement", ask who can write it.** `Tenancy.Subscription.Manage` is seeded to the tenant's own Admin, so the party a quota or an expiry constrains can raise it — not a mis-seeded key but the consequence of modelling exactly one actor while a subscription is a two-party record. Such a ceiling is an accurate record and a guard against drifting past it unnoticed, never a control that survives an adversary; say so in the behavior, the command *and* the doc (phase-41).
- A field dead on the two tenants you could reach is **one sample, not two** — phase 33 retired Subscription Amount and both quotas after reading zeros on two *free trials*, and the vendor's public price list sells three tiers whose whole commercial difference is those fields. Before asking for another tenant, read what the seller publishes: it also carries the contractual definitions no screen ever shows (phase-41, generalising phase-30's find-the-rule and phase-32's it's-a-fact-about-that-tenant).
- A non-nullable column on a populated table needs a hand-written backfill; the scaffold's `DEFAULT '0001-01-01'` back-dates every row and leaves a stray constraint (phase-31) — but a default is *safe* exactly when it is the truth about the rows already there, which is why `StockMovement.ValueAdjustment` needed none (phase-37).
- Read an uploaded image's format from its **bytes**, never its declared content type: `Domain/Common/ImageHeader` answers "is this really a PNG" and "is it at least 300×300" with one parser and no dependency, and the PDF renderer re-checks with it because QuestPDF throws for an undecodable image *after* composition, where no try/catch around the draw call helps (phase-39).
- The mirror of that on the read side: a **list** query returning the aggregate exposes a new field for free, while a **detail** query projecting a DTO drops it silently — the write path looks perfect and the form can never show the stored value (phase-32's `GetInvoiceQuery`, caught only by an E2E that re-read what it wrote).
- That read-side gap is the default, not the exception: phase 32's write-path sweep guard stayed green while **14 of 15 detail DTOs and all 5 conversion templates** dropped the same field, so a form stored a branch, never showed it, and overwrote it on the next save. Adding a field to many aggregates owes three assertions — write, read, and every prefill between (phase-35a).
- The same rule with a banner instead of a filter: **state two surfaces show, and one of them changes, needs a shared store** — the shell subscription banner still read "366 days remaining in your trial" on the page that had just recorded a paid plan. Both components were individually correct, so no unit test saw it; the screen must *save through* the store the banner reads (phase-41).
- A swept-in filter handler must copy what the **sibling** handlers on that page do, not a template: on a paginated page whose reload is `load()` rather than `reload()`, every other filter also calls `page.set(1)`, and without it a narrowing filter applied on page 3 reads as "this branch has no data" (phase-35b).
- A live region is announced when its **contents change**, so `@if (msg) { <div role="alert"> }` announces nothing — the region is created already holding its text. Render `app-status-banner` unconditionally; the grep that scopes such a sweep is itself a predicate, and `role="status"` hid seven more (phase-40).
- Bootstrap's `:focus-visible` ring is the control's own tone at 50% alpha and measures 1.21–2.53:1 against WCAG 1.4.11's 3:1 — phase 34a's palette lesson one property over. One opaque `#0a58ca` outline at `outline-offset: 2px` contrasts against the *page*, so one number is right for every variant (phase-40).
- A shared UI *panel* is not evidence of a shared *model*: the reference product shows email templates inside its Custom Templates panel but serves them from a different resource with six extra fields and a disjoint type vocabulary, so `EmailTemplate` is its own aggregate and phase 27b's placeholder `CustomTemplateType.Email` was deleted rather than left dead (phase-30).
- When a request's real permission key depends on a column of the row the handler is about to load (not on the request itself), `IRequirePermission.PermissionKey` cannot express it — that property is evaluated before the handler runs. Declare a blanket key (Admin+Member, seeded, gates nothing on its own — same shape as `TransactionApprovalView`/`RecentTransactionsQuery`'s pattern) to get through `AuthorizationBehavior`, then re-check the real key inside the handler once the row is loaded, throwing the identical `ForbiddenException` shape so a caller can't tell the layers apart (phase-27a, `AttachmentAccess`).
- Three enums that each name an overlapping-but-distinct vocabulary (a `DocumentType`, and two or three "what can this attach to" parent enums) must never be bridged by ordinal cast — they cannot share an ordinal order once one of them starts with non-document members. Bridge by member *name* (`Enum.TryParse`) and add a guard test picking a member with deliberately divergent ordinals across the enums (phase-27a, `DocumentParentTypes`, restating phase-26a's lesson structurally).
- A negative permission proof needs its **positive half in the same run**: `AuthorizationBehavior` returns the identical message for "not a member" and "lacks the key", so a 403 only means the key if the same user gets a 200 on a read of the same organization. `accept-invitation` is authenticated — log in *before* accepting, or the membership stays `Invited` and every later 403 is vacuous (phase-41).
- Two implementations of one rule in two languages are pinned to a **shared table read by both suites**, not to each other (`rich-text-cases.json`, linked in as an embedded resource so a moved file is a build error) — phase-26b's `BsCalendar`/`bs-date.ts` arrangement; it caught a real whitespace divergence within the hour (phase-39).
- Patching two reports into agreement leaves them agreeing by coincidence: phase 31 fixed both known divergences between the ageing pair and they still disagreed about *which documents are ageable*. One reader, two presentations, and a test that reads both on the same data (phase-36's `OutstandingDocumentReader`).
- A curl seed script that pipes approvals to `/dev/null` hides its own failures — the first report just comes back empty. Print every approval's status code. Two live traps: `POST /api/organizations` returns `organizationId`, not `id`, and the GL defaults are **one** `PUT /accounting-defaults` taking all eleven accounts (phase-26c).
- A fresh Organization has zero Accounts and zero Account Groups — nothing seeds a chart of accounts — so any E2E needing a Journal Voucher, Cash Transfer, Payment or Expense line must `POST` its own account groups (one per `AccountRootType`, spelled `Asset`/`Liability`/`Equity`/`Income`/`Expense` — singular, unlike the plural `rootType` groupings a list response returns them under) before it can create an account (phase-27a).
- `POST /accounts`'s `kind` is `Other`/`Bank`/`Cash`; `POST /organizations` needs `accountingStartDate` + `workspaceName` and takes the entitlement flags directly; `POST /auth/register` needs `phone`; `grants` is a dictionary but `locationGrants` is a **list** of `{locationId, grants}`; master-data lists are paged, so it is `['items'][0]['id']` (phase-35a).
- A `Reports.*` key **cannot be granted per location** (400: "Only transaction permissions are location-scoped") — a report's location scope comes from the caller's *transaction* grants via `AnyGrantedLocationsAsync`, so the report key is organization-wide and the branch grant goes on e.g. `Sales.Invoice.View` (phase-35b).
- `POST /organizations`'s entitlement flags are `multipleLocations`/`multipleWarehouses`/`trackInventory` — an `...Enabled` suffix binds to false silently and surfaces three steps later as a feature 403; `POST /billing-locations` requires `address`; the invite route is `POST /organizations/{id}/invitations` returning **`membershipId`**; `vatRate` is `ThirteenPercentVat` (a wrong enum member fails as "Failed to read parameter … as JSON", naming no field) (phase-35b).
- `POST /api/organizations` needs `industry` and a non-empty `turnstileToken`; accept-invitation is `/api/organizations/memberships/{id}/accept-invitation` with no org segment (with one, a 404 leaves the membership `Invited`); units are `/units-of-measurement` (`shortName`); credit terms are under `/configuration/` (phase-31).
- `POST /products` also needs `categoryId`; every create returns **201**, not 200; configuration lookups live under `/organizations/{id}/configuration/...`, not the org root; and a fresh organization has no warehouse, unit, category or product, all four of which an invoice line needs even for a Service (phase-40).
- A lazy `.*?` between two anchors spans the instances in between (it expands past `</label>` to reach a later match), silently merging them; exclude the closing marker — `((?:(?!</label>).)*?)`. The tell is two independent counts disagreeing, so derive the expected number a second way (phase-34a, on top of phase-32's assert-before-writing rule).
- A `sed -i` over a glob rewrites **every** file it matches, and on Windows that flips CRLF to LF even where the pattern never fires — `git diff` stays empty while `git status` shows a hundred extra modified files. Undoing it needs `rm` *then* `git checkout --`; restrict the file list instead (phase-30).
- A benchmark run against an **empty** tenant looks like a spectacularly fast one — 20 ms p95, every status 200. Only the response size tells them apart, so a harness must assert its target is populated before timing it; `seed-master.sh` rewriting `.seed-ids.env` is the side effect that caused it (phase-34c, the prints-and-returns trap in another costume).

## Two column renames on one table, paired by ordinal (phase 43)

`dotnet ef migrations add` cannot infer a rename — it compares two models and sees a column gone
and a column arrived. When the diff contains **two** renames on one table it pairs them by their
ordinal position, and phase 43's rename of `TrialStartsAt → OriginatedAt` and `TrialEndsAt →
TermEndsAt` scaffolded as `TrialStartsAt → TermEndsAt` and `TrialEndsAt → OriginatedAt`: the two
values **swapped** on every existing row.

Nothing downstream would have said so, because the model only ever sees the names. Every tenant's
origin would have become its term end, `SubscriptionExpiryBehavior` reads that end on every
request, and the whole database would have read as expired the moment the migration applied.

The lesson is not "read the migration" — phase-1c already says that. It is what to verify **with**.
Checking that the columns exist passes in both worlds. The check that only passes in the right one
is a predicate that is true of the *data*: `SELECT COUNT(*) FROM tenancy.TenantSubscriptions WHERE
OriginatedAt >= TermEndsAt` returns 0, and would have returned every row had the swap shipped.
Whenever a migration moves data rather than shape, find the statement that distinguishes the two
outcomes and run it after applying.

## The reversal owes what the document did to the *stock* ledger (phase 43)

Phase-6 bug #3 and phase 29 both say that changing what a document posts changes what its reversal
owes. Phase 43 is the same rule arriving through the stock ledger instead of the general one, and
it is worse because the two handlers look nothing alike.

`ApproveDebitNoteCommandHandler` consumed FIFO layers at the **source Purchase Bill's** warehouse.
Phase 43 gave `DebitNote` its own `WarehouseId` so a *standalone* Goods return consumes from
somewhere — closing the divergence where the GL credited the Inventory account and the ledger
never moved. `VoidDebitNoteCommandHandler` still looked its warehouse up from the source bill,
which is null for a standalone note, so after the fix stock went **out** on Approve and never came
back on Void: strictly worse than the bug being fixed.

No handler test could see it. Every test in the suite exercised the converted path, where the old
lookup worked — which is the general shape: **when a change adds a case to one side of a
document's lifecycle, the tests that exist are the ones written for the case that already worked.**
Only the E2E, reading the FIFO layers and the Inventory account after *both* Approve and Void, put
the two numbers side by side.

## One gap on one side of a document family is not the same gap on the other (phase 43)

Phase 43's brief recommended a `WarehouseId` on `DebitNote` "because that is what the Credit Note
already does on the sales side". The Credit Note does not: it has no `WarehouseId` either, and a
standalone Credit Note skips the stock ledger entirely.

The two cases are genuinely different, and the difference is what makes one a bug:

- A standalone **Credit Note** resolves inventory accounts only when a source invoice exists
  (`resolveInventoryAccounts: sourceInvoice is not null`), so it posts no Inventory leg at all.
  Its GL and its ledger agree — both untouched. A limitation, not a divergence.
- A standalone **Debit Note** credited the Inventory account unconditionally, because a Goods line
  resolves through `PurchaseBillAccountResolver` (post-phase-19). Its two views disagreed.

So before mirroring a fix onto a document's twin, check what each side actually posts. "The other
side already solved this" is a premise to verify, not a reason — and here the premise was wrong in
a way that would have produced a worse design (a warehouse on the sales side answering a question
nobody had asked).

## Folding a report to base currency (phase 43)

A statutory register filed with the IRD is denominated in NPR, so its money columns are NPR — a
foreign invoice contributing its own 100 beside a domestic invoice's 100 is not a display quirk,
it is a wrong number on a filed return. Three things decide whether the fold is correct:

**Fold the lines, not the buckets.** `Total` is derived as a sum of the other three magnitudes, so
converting the finished buckets independently lets `Total` differ from `TaxExempt + Taxable + VAT`
by a paisa at some rates — and the footer, the page and the per-line view each round it
differently. This is phase-28's posting-rule rule (*convert the inputs, never the finished legs*)
arriving in a report, for exactly the same reason.

**Fold in the shared reader.** `SalesReturnReader` is read by both the Sales Register and the Sales
Return Register, and phase 36 built it precisely so the two cannot disagree about one note. Folding
in the caller would have made one register report a foreign return in rupees and the other report
the same return in dollars — a regression, not a partial fix.

**Fold in memory.** `ExchangeRates.ToBase` is a static call: inside the query it is untranslatable
on SQL Server and silently evaluated in C# by InMemory, so every handler test passes while the
endpoint 500s. Select the raw amounts and the rate, `ToListAsync`, then convert.

The rule also selects more than one report. Phase 43 folded the sales side and stopped at the
shared reader's edge, naming the Purchase Register, the Purchase Return Register, the VAT Summary
and Annex 13 as carrying the same defect — because half a rule applied is better recorded than
half-applied silently.

## A record parent is a DocumentType that is not transactional (phase 43)

Phase 27a swept four mechanisms across the 15 transactional document types and bridged three parent
enums to `DocumentType` by name. Phase 43 added two parents that are **not documents** — `Deal` and
`WorkTask` — and the whole sweep fell out of one property rather than a special case.

Both gained a `DocumentType` member, because `ListActivitiesQuery` keys the audit feed on
`(DocumentType, DocumentId)` and a record with an Activity tab needs a name in that vocabulary. But
both are classified in `DocumentMechanisms.NotApplicableReasons`, so
`DocumentParentTypes.TryToDocumentType` returns **null** for them — exactly as it already did for
`Contact`. That null is what routes them through `ParentPermissions` to their own aggregates' keys
(`Crm.Deal.*`, `Workflow.Task.*`) with no branch anywhere else in the codebase.

Two things worth stating because they read like omissions:

- **The tab lists are not symmetric.** The live Deal page has Overview / Contact Personnel / Tasks
  plus Documents and Activity; the live Task page has Overview / Documents / Activity. So `Deal`
  joins all three parent enums and `WorkTask` joins two — a task does not parent tasks. Asserted in
  both directions, because a missing member and a deliberately absent one look identical.
- **A file on a task is not a task on a task.** `CreateTask`/`ListTasks` keep phase 13's blanket
  `Workflow.Task.*` pair for every parent; what `ParentPermissions` now answers for is an
  attachment or comment *on* a WorkTask.

The route family is separate too (`RecordTabsEndpoints`): `DocumentTabsEndpoints` binds its segment
to `DocumentType` and resolves through `DocumentParentTypes.For<T>()`, which answers only for
transactional types, and relaxing it would have given up the property that makes an unroutable type
a 404 from routing rather than something a validator has to catch.

## Product-to-location filters the picker and nothing else (phase 43)

Phase 36 proved by experiment that a product scoped to one location vanishes from another
location's line picker, server-side — and deliberately left open whether the reference product also
*refuses* such a line at save or approve. Phase 43 ran that experiment on Cadehi (2026-09-14).

With phase 36's `Location Probe Product` still restricted to POS Retail: the picker returns "No
data" at HeadOffice and offers the product at POS Retail (36's finding reproduced); adding the line
at POS Retail and then **switching the header back to HeadOffice leaves the line on the form**; Save
succeeds; Approve succeeds, numbering the invoice `INV0001HO|83-84` from the HeadOffice pool — which
independently confirms the header location really was HeadOffice.

So the restriction is a property of the picker, not an invariant of the document. The enforcement
idea is **retired**, not carried: building it would have shipped a rule the reference product does
not have, and one that could invalidate documents a tenant had legitimately saved. Phase-32b's
lesson in the pleasant direction — 35b's recorded inference happened to be right, but it was right
the way a guess is right, and one write settled it.

## A refused field should be absent, not ignored (phase 43)

`UpdateOrganizationCommand` deliberately cannot change `WorkspaceName`: it is a login-adjacent
unique slug that *addresses* the tenant, and changing it breaks every bookmarked workspace URL.

The implementation detail that matters is that `WorkspaceName` is **absent** from the Api request
record, not present-and-unused. A field a client can send and the server silently drops reads, from
the client's side, exactly like a field that was accepted — the same failure mode as phase-38's
array parameter binding to null while the request looked fine. If a command refuses a field, take
it out of the shape, so sending it is a shape a caller can notice.

## A sweep guard over query records cannot see the handler (phase 44)

`ReportLocationSweepGuardTests` checks three things, and all three are about the *query record*: that
it implements `ILocationFilteredReport`, that its `LocationId` defaults to null, and that its handler
takes an `ICurrentUserService` at all. None of them can see whether the handler ever puts the filter
into a `Where`.

`SalesRegisterQueryHandler` and `PurchaseRegisterQueryHandler` passed every check from phase 35b to
phase 44 while applying the filter to their **return half only** — `SalesReturnReader` and
`PurchaseReturnReader` honoured it, and the invoice and purchase-bill queries beside them did not.
The `Where` on the Sales Register's invoice query was untouched since phase 19. So choosing a
location removed the credit and debit notes from a statutory register and left every invoice and
bill in it, which is worse than not offering the control: phase-34b's rule with IRD numbers behind
it.

The remedy is not a cleverer guard. A handler with two document queries that filters one of them is
not decidable by reflection, so the claim is pinned behaviourally — one test per report, each shown
to fail against the pre-phase-44 handler — and the guard's own doc comment now states the blind spot,
so the next reader does not mistake "passes the guard" for "applies the filter".

## Reporting Tags: OR within a category, AND across categories (phase 44)

Phase 19 chose "a document matches when it carries *any* of the selected options" as a judgement
call. Phase 36 kept it deliberately, recording that what the live drawer does across two categories
"was not observable on a tenant with tagged data to hand", and that inventing a second rule for a
sibling report to disagree with was exactly what that phase existed to undo.

Moonbeam has six tag categories. Four runs on Inventory Position settle it:

| Selection | Rows |
|---|---|
| BUSINESS{BUSINESS} | 3 |
| BUSINESS{BUSINESS, asdasd} | 4 — exactly the union |
| BUSINESS{BUSINESS} + SERVICE{sdfsdfds} | **2** — a strict subset |
| BUSINESS{both} + SERVICE{sdfsdfds} | 3 |

Adding an option inside a category **grew** the set, which AND-within cannot do. Adding a second
category **shrank** it, which OR-across cannot do. Standard faceted filtering, and not what was
built.

The general lesson is about the record, not the rule: a decision written down as *inherited* reads
later exactly like one written down as *observed*, and only the first is cheap to overturn. Phase
36's own words are what made this correction quick — it said which tenant it could not see, and why.

## A backfill over approved history needs its own mutator (phase 44)

`Invoice.SetLocation` calls `EnsureDraft()`, and the doc comment says why: an Approved document's
location is what its numbering pool was drawn from and what every location-filtered report has
already counted it under, so moving it afterwards would silently restate both.

`BackfillDocumentLocationsCommand` exists precisely for Approved documents — the ones written while
their type sat outside `LocationScopeMode`, which carry **no** location. Both of `SetLocation`'s
reasons fail for them: a document with no location was numbered from the *unscoped* pool, so there is
no location-wise numbering to restate, and it was counted under *no* location, so no filtered
report's past answer is revised — it was missing from every one of them.

So fifteen aggregates gained `BackfillLocation(Guid)`, which fills a null and throws if a location is
already set. The guard is not weakened by adding a method that cannot reach past it. Reaching through
EF's change tracker instead was considered and rejected: the rule belongs in the Domain, where the
next reader will look for it.

## A stamped audit column is about the past, not the present (phase 44)

`AuditBehavior` now reads the document's location once, after the handler, and freezes it on the
`Audit` row. Existing rows are left null rather than backfilled, and the phase's own E2E shows why:
after `BackfillDocumentLocationsCommand` assigned HeadOffice to two purchase bills, the *documents*
read HeadOffice and their *audit rows* still read null — because at the moment those actions
happened, the bills had no location.

That is the intended semantics of a stamped column and the reason phase-35b's rule (an append-only
fact needs a stamped column, never a join back to the document) is about honesty as well as cost. A
join back would have reported today's location for yesterday's action.


## An allow-list reason can be the argument for the opposite conclusion (phase 45)

Phase 24 swept the rule that a variant **parent** may never reach a document line across every
line-taking handler, and pinned it with `ProductVariantSweepGuardTests` — a guard that reads every
`*CommandHandler.cs` off disk and fails the build on one that takes product ids without routing
through `ProductVariantRules`. Six handlers are exempted, each with a written reason. One of them
read:

> A secondary unit is catalog metadata, not a stock or document line — attaching one to a parent
> moves nothing and reconciles against nothing. Multi-UOM × variants is explicitly out of scope for
> Phase 24.

Read once with fresh eyes, that sentence is **the argument for refusing a parent a secondary unit**,
not for permitting it. "Moves nothing and reconciles against nothing" is precisely what phase 24
gave as the reason a parent may not carry stock; a conversion rate on a bucket that never receives
anything converts nothing. The live read agreed — a variant parent's detail page in the reference
product has no Inventory Details panel at all, no stock figures, no Secondary Unit tab, no Warehouse
tab, while every variant child has all three.

This is phase 44's lesson from a new direction. There, a control recorded as unbuilt because two
options could not be told apart turned out to be a parent and its modifier. Here, an exemption's
stated reason had quietly become the case against itself, and nothing in the guard could notice:
a guard checks that every exemption names a file that still exists (phase-34a's rule), never that
the reason still holds.

**The practice:** when a phase's scope finally reaches an area an earlier phase excused, re-read the
excuse as a claim rather than as a boundary. The remedy is the standing three-layer split — the
Domain keeps the invariant (`InvalidOperationException`), a rule in `ProductVariantRules` throws the
`ConflictException` that names the reason, because a Domain invariant reached through an endpoint is
a 500 that tells the caller nothing (phase-39), and the allow-list entry is **deleted** so the guard
covers the handler from then on.

## An importer whose row adds to a set its command replaces (phase 45)

`SetProductVariantAttributesCommand` replaces a product's "Attributes Used" pool wholesale. That is
right for a form, which submits the whole set at once, and wrong for a file, whose rows arrive one
at a time: phase 45's `ProductAttributePoolImporter` is one row per (product, attribute, option), so
a straight replace per row would leave each product holding only the last row that named it.

The importer re-reads the product's current pool per row and sends the **union**. Three consequences,
each deliberate:

- Re-running a file is **idempotent** — a pair already present produces the same pool.
- An import can never **remove** an option, so it can never strand a variant built from one. Removal
  stays on the product's own Variants tab, where the refusal that protects those children lives.
- During the **dry run** nothing is written, so two rows naming one product both plan against the
  same starting pool. **That is correct**: a validation pass checks rows, it does not accumulate
  them. `ImportJobProcessor` plans *and then executes* each row in turn during apply (each in its own
  scope, with its own `IJobActingUser`), so the union really does accumulate there.

That last point is the mirror of `ImportRowContext.PendingKeys`. In a hierarchical importer the dry
run needs *extra* knowledge — the set of keys the file will create — or it reports a correct file as
broken. Here it needs none, because a row whose effect an earlier row already had is still a valid
row. The distinction worth keeping is: **a dry run must not report a correct file as broken; it is
under no obligation to predict the file's cumulative end state.**


## Guard the add and the edit, never the delete (phase 45)

Phase 45 refused a variant **parent** a unit matrix, because a parent holds no stock and a conversion
rate on it converts nothing. The first version guarded all three verbs -- add, update and delete --
and that was wrong in a way no test could see.

`SetProductVariantAttributesCommand` promotes an ordinary product to a variant parent, and it does
not look at the product's secondary units. It should not: the pool is the thing being set. So a
product can be created with secondary units and become a parent afterwards. Its rows are then stale
-- and with the delete guarded, they were **permanent**: the client's new visibility gate hid the
whole card, and the API answered the only verb that could remove them with a 409.

**A delete is never the thing to refuse.** Removing a row that should not exist is the repair, not
the damage; refusing it turns a recoverable state into an unrecoverable one. The rule generalises
past this case: when a guard exists because an entity has moved into a role that makes some child
data meaningless, guard **creating and changing** that data and leave **removing** it open, then make
the removal reachable -- here the card renders for a parent that still holds rows, read-only except
Delete, with the reason on screen.

**Why no test caught it**, which is the transferable part. Every handler test constructed the product
in its *final* role -- an ordinary product, a parent, or a child -- so none could reach "ordinary,
given units, then promoted". A fixture builds an object in the state the test is about; a user walks
it through states in an order nobody wrote down. That is the class of bug a browser pass finds and a
unit test does not, and it is the reason the pass is part of the exit bar rather than a formality.


## stopPropagation inside a router link is what causes the navigation (phase 45)

The Custom Status picker renders inside each list row's `<a [routerLink]>`, and clicking it opened
the document instead of the dropdown. The control already called `$event.stopPropagation()`.

That call was not insufficient — **it was the cause**. `RouterLink` listens for `click` on its own
host and, when it fires, calls `preventDefault()` and navigates through the router. Stopping
propagation keeps the event from reaching that listener, so `preventDefault()` is never called — and
the browser's *native* activation of the enclosing anchor is left as the only remaining behaviour.
Removing the guard entirely would have navigated too (in-app rather than natively), so the symptom
looked like "the guard does nothing" when it was really "the guard disabled the good half".

**The fix needs both halves, and they are deliberately asymmetric:**

- `click` → `stopPropagation()` **and** `preventDefault()`. By then the popup is already open, and
  the default action left to prevent is the anchor's navigation.
- `mousedown` → `stopPropagation()` **only**. A native `<select>` opens its popup as the *default
  action* of mousedown, so preventing it there stops the dropdown opening at all. This is the half a
  later "consistency" edit would most plausibly break, so it is asserted in the opposite direction:
  a test dispatches a cancelable mousedown and requires `defaultPrevented === false`.

**The wider shape.** An `<a>` containing a `<select>` is invalid HTML — interactive content may not
nest inside an anchor — so the real fix is structural: take the control out of the anchor and use
Bootstrap's `stretched-link` over the row's non-interactive half. Phase 45 fixed the behaviour in the
shared component (one change, all four grids) and left the nesting, because a four-template layout
change had its own risk. A sweep for the shape — anchors with `routerLink` containing
`select|button|input|textarea|app-*-picker` — finds exactly four instances, all the same component,
which is what made the contained fix safe to reason about.

## A per-day count comes from the audit trail, never the aggregate's own timestamp (phase 46)

`UploadedDocument.ExtractionAttemptedAt` records when a document was **last** scanned. It is
overwritten by every re-extraction, so it holds one instant per document and cannot answer "how many
scans happened today" — ten re-runs against one supplier bill would count as one, and those ten are
ten real, paid API calls. The expensive case is exactly the case it cannot see.

The append-only source already existed: phase 22 audits every extraction, because it is the one
action in the product that sends a customer's document outward. `SubscriptionUsageReader
.CountAiScansTodayAsync` counts `Audit` rows with `DocumentType.DocumentExtraction` inside the
Nepal-local day. Two properties make that work and both are worth stating:

- `AuditBehavior` writes its row **after** the handler succeeds, so a request currently being metered
  has not written one yet. The count is exactly "attempts before this one today", which is the figure
  a ceiling compares against — no off-by-one, and no need to exclude self.
- The window is `NepalTime.StartOfLocalDay`, not `DateTime.UtcNow.Date`. UTC midnight is 05:45 in
  Kathmandu: a UTC key hands every tenant a fresh allowance each morning between 00:00 and 05:45
  local, and refuses them in the evening against scans belonging to a day already over.

This is phase 26c's rule — *a dated stock report derives from `StockMovement`, never
`StockLedgerEntry`, whose `QuantityRemaining` is decremented in place* — in an area with no stock in
it. The generalisation: **a figure "as of a date" comes from the history; a figure "as of now" may
come from the field.**

## Zero means unmetered for an allowance, and the opposite for a cost control (phase 46)

Phase 31 confirmed live that a stored `0` credit limit means *no limit*, and phase 41 made the same
reading the sentinel on both subscription quotas. That is right for an allowance somebody **bought**:
a trial has purchased nothing, and a trial that could create no products would be a trial of nothing.

It is exactly wrong for the AI-scan ceiling, which is not a purchase but a cap on outward spend.
Unmetered there means a free trial with uncapped calls against a paid API. So `CreateTrial` seeds the
published 20 rather than the sentinel, `SetTenantSubscriptionCommand` falls back to 20 rather than to
0 when there is no plan, and the sentinel stays available only as a deliberate exemption nothing
reaches by accident.

The migration is where this bites hardest. `dotnet ef` scaffolded
`DailyAiScanQuota int NOT NULL DEFAULT 0` — and unlike phase 31's `DueDate '0001-01-01'` or phase
41's back-dated `TermStartsAt`, that value looks *entirely plausible*. Every existing tenant would
have come out unmetered, the ceiling would have applied to nobody, nothing would have failed, no test
would have caught it, and the only evidence would have been the API bill. Rewritten by hand as
add-nullable → `UPDATE … SET 20` → alter-to-NOT-NULL, and verified with a predicate that reads
backwards under the scaffolded version (156/156 at 20, 0 at 0).

`LocationQuota` in the same migration **kept** its scaffolded `DEFAULT 0`, and the contrast is the
rule: phase 37's refinement says a default is safe exactly when it is already the truth about the
rows that are there. No tenant had recorded a purchased location count, so 0 is a fact about them.

## A purchased quantity is not automatically a ceiling (phase 46)

The published price list sells Billing Location at Rs 5,000 per location per year, which reads like a
count ceiling and was planned as one. The live product caps nothing: the reference tenant runs three
locations on a plain *Enabled* flag, the list is unbounded, the Add New Location dialog asks for code,
name, address and warehouse and mentions no cap, no remaining count and no charge, and the
Subscriptions screen carries no location row at all. The metering is commercial and happens on an
invoice, not in software.

So `TenantSubscription.LocationQuota` is stored, displayed beside the count in use, and never
compared in order to refuse; phase 32's boolean cap-at-one remains the only refusal. This is phase
43's product-to-location moment — *the reference product saves and approves a document naming an
out-of-location product, so the enforcement idea is retired with the evidence* — reached the same way
and reversing the phase's own written plan.

The absence is pinned rather than left implicit: `SubscriptionQuotaKind` has exactly three members,
asserted by name, so adding a location refusal fails a test whose comment explains why it should not
exist.

## Two lists describing one thing in two vocabularies cannot be joined by name (phase 46)

A subscription plan's tick-list is the price list's wording — "Multiple currency", "Inventory
tracking", "POS (Retail/Restro)". A tenant's entitlement list is the signup wizard's —
"Multi-Currency Support", "Track Inventory", "Point of Sale (Retail)". Surfacing phase 41's
plan-vs-entitlement mismatch means pairing them, and the first attempt did it in the browser by
display name.

It matches nothing. Worse, it renders as *"no mismatches"*, which is also the correct output most of
the time, so it would have looked right on every tenant that had no mismatch and stayed wrong on
every tenant that did. This is phase 27a's ordinal-enum bridge through a different door and it fails
identically: quietly, and looking like agreement.

The lists are not even the same length. One POS row covers two tenant flags; Landed Cost and
Developer API are published per tier with no tenant flag at all; Multiple Locations has a tenant flag
but no tier includes it, because it is an add-on. The comparison moved to
`GetTenantSubscriptionQueryHandler`, where it is a typed pairing of the six entitlements both sides
have an opinion about, with the four exclusions written down and reasoned.

## A guard predicate naming a dependency is not naming the behaviour (phase 46)

The AI-scan ceiling's second sweep-guard direction asks "what reaches the extractor without being
metered", and looked for handlers whose constructor takes `IDocumentExtractor`. That found three, and
two of them — `GetAiDocumentExtractionSettingQueryHandler` and
`UpdateAiDocumentExtractionSettingCommandHandler` — only read `IsConfigured` and `ModelId`, local
properties describing whether a credential exists and which model would be used, to render the
settings panel. Neither sends anything anywhere.

Taking the dependency is not spending the allowance. The two are named as exclusions with their
reasons, in the same `IReadOnlyDictionary<string, string>` shape phase 41 used for the unmetered
GL-posting types — and a second test asserts every named exclusion still exists, so deleting or
renaming a handler cannot leave a stale excuse behind that silently exempts a future handler that
happens to share its name.

## A `computed()` that short-circuits past its signal never recomputes (phase 47)

Angular's `computed()` records, as its dependency set, exactly the signals the evaluation it is
memoising actually **read**. That is not a subtlety in a graph where every branch reads something; it
is a trap the moment a `&&` can skip the signal.

Phase 47's `FieldError` derived "which control is the current message about" like this:

```ts
this.active = computed(() => {
  const owner = this.owner;                                       // a plain field, not a signal
  return owner !== null && this.errorMessage() === owner.message  // <- short-circuits
    ? owner.control : null;
});
```

On a form that has not failed yet — which is the first render of every form — `owner` is null, `&&`
short-circuits, and `this.errorMessage()` is **never reached**. The computed memoises `null` with an
*empty* dependency set, and no later `fail()` can wake it: there is no signal whose change would
invalidate it. Every document form in the app rendered without `aria-invalid`, without
`aria-describedby` and without `is-invalid`, while the banner announced correctly and focus moved
correctly, because both of those are done imperatively by `fail()` itself. A confusing symptom: two
thirds of the feature worked.

**Read the signal first and unconditionally.** One line, and the ordering is load-bearing enough to
carry a comment saying so.

**Why no test caught it.** All six of `field-error.spec.ts`'s tests called `fail()` before reading
anything, so the first evaluation always had `owner` set and always reached the signal. The test that
now pins it reads the computed **before** the first failure and then asserts it reacts — which is the
ordering a real page has and a test naturally does not.

This is the mirror of CLAUDE.md's standing expression-tree gotcha: there, the problem is that an
expression tree does **not** short-circuit and hands EF a null; here, the problem is that a reactive
closure **does** short-circuit and hands the graph nothing. Both are about a `||`/`&&` whose skipped
half was the part that mattered.

## An HTML comment is prose, and a source-scanning guard cannot tell (phase 47)

`a11y-sweep-guard.spec.ts` is regexes over raw template text, deliberately — an Angular template is
not HTML and every parser worth using would either choke on the control-flow blocks or normalise away
the attributes being checked. The cost of that decision only appeared when phase 47 added a check for
*interactive controls nested inside a link*: its first run named five templates, and all five were
**comments explaining the rule being checked**, including the phase's own "a &lt;select&gt; nested in
an &lt;a&gt; is invalid HTML that no event handler makes valid".

Comments are now stripped once, where `entries` is built, so every assertion in the file benefits and
the character offsets stay consistent for the ones that use them. Stripping can only remove false
positives: markup that is commented out does not render, so it cannot be an accessibility defect.

Two halves have to be asserted, and the second is the one that matters: that comments are gone **and
that nothing else is**. A greedy `<!--[\s\S]*-->` would swallow everything between the first `<!--`
and the last `-->`, which on these templates is most of the file — and every other assertion in the
guard would then pass over an empty string, which is phase 34a's empty-stylesheet failure in a new
place.

Phase 40 met the same class of trap from the other side: a backtick inside a comment inside an inline
`template:` literal terminates the template, and the compiler blames the decorator.

## A nested control is the defect; the navigation was only the symptom (phase 47)

Phase 45 found that clicking the Custom Status picker on a document list opened the document, traced
it exactly right (`stopPropagation` suppresses RouterLink's own listener — the one that calls
`preventDefault` — leaving native anchor navigation as the only behaviour), and fixed it with a
click handler that does both and a mousedown handler that does neither.

It left the `<select>` inside the `<a>`, and that is the accessibility defect:

* the control's text is folded into the **link's accessible name**, so the row announces as one
  control with the options read out as part of its label;
* a keyboard user who tabs onto the select is standing **inside a link** they never chose to enter;
* HTML's parser is entitled to hoist the element out of the anchor, which is how identical markup
  behaves differently in two browsers.

The shape that works: the row is a `<div>` carrying the row classes plus `position-relative`, the
link is an `<a class="stretched-link">` on the row's title, and the controls are siblings with
`position-relative z-2`.

**`z-2` is load-bearing and `position-relative` alone is not.** Bootstrap paints `stretched-link`'s
overlay at `z-index: 1`, so a merely-positioned control still sits underneath it and the row link
swallows the click. Verify it rather than reasoning about it: `document.elementFromPoint` at the
control's own centre must return the control.

**And the focus ring has to follow.** The link is now a small anchor on the title, so `:focus-visible`
on it shrinks the indicator from a whole row to a few characters of text — trading one defect for a
worse one, since 2.4.7 is about seeing *where you are*. Suppress the link's own ring and paint the
row's through `:has(.stretched-link:focus-visible)`.

**What replaces a component defending itself is a guard over every template — and its interesting
half must be derived.** A regex for `<select>` inside `<a>` finds **nothing** on the four grids that
actually shipped this, because what they nest is a *component*. So collect the selector of every
component whose own template renders a control, and treat a nested one of those the same as a nested
`<select>`. The sixteenth control component is then covered on the day it is written.

## A scripted insert must anchor on the whole declaration, comment included (phase 47)

Phase 35a's lesson was that a script inserting an import after "the last `import ` line" lands
*inside* a multi-line `import { … }` block, and the fix was to anchor on the statement's **closing**
line. Phase 47 hit its mirror image. A sweep that inserted a new field and method before
`protected onSearch(term: string): void {` landed between that method and its own doc comment, on
fifteen files at once, orphaning the comment above an unrelated field:

```ts
  /**
   * Phase 34b -- the shared list chrome's search box. Resets to page 1, because …
   */
  /**
   * Phase 47 -- the chrome's `Sort by` …
   */
  protected readonly sortOptions = …
```

The unit being anchored on is **the comment plus the declaration**, not the declaration. Anchor on
the comment block's opening line when inserting before, and on the statement's closing line when
inserting after. Both failures compile, and neither is visible in a diff stat.

## One sweep, two newline conventions (phase 47)

Three of the forty-two files in phase 47's server-side sweep are LF while the rest are CRLF, in a
repository that is otherwise CRLF throughout. A script anchored on `\r\n` aborted on the third file
with an anchor that *looked* correct — which is the useful failure, and only because the script
asserted its anchor count before writing anything. A script that had merely written would have
rewritten three whole files invisibly, which is phase 33's `sed -i` gotcha reached from a different
direction.

Detect the newline per file (`"\r\n" if "\r\n" in raw else "\n"`), normalise to LF for matching, and
write back with the file's own. Phase 32's rule — assert every anchor count before writing — is what
turns this from a silent rewrite into an abort.

## A parity test between two implementations must run against the real one (phase 47)

Phase 40 shipped two answers to "does this row match what I typed": the paginated lists send a term
to the server (`x.Column.Contains(term)` in a handler's `Where`), the Configurations lookups filter
in the browser (`matchesLookup`). It said plainly that nothing made them agree.

The shared-table arrangement is phase 26b's and phase 39's — one JSON file both suites read, neither
implementation the source of truth for the other. What is specific here is **where the server half
runs**: `Api.IntegrationTests`, against SQL Server in a container, *not* `Application.UnitTests`.
The property the table is mostly about is case-insensitivity, and that comes from the collation, not
from the expression: single-argument `Contains` is case-insensitive on SQL Server and
case-**sensitive** on InMemory. A parity test on InMemory would pin a behaviour production does not
have — which is the standing gotcha the whole exercise lives inside.

The cases worth writing are the ones where a `LIKE` pattern built by string concatenation would
genuinely have diverged: `%`, a bare `_`, and `[a-z]` are characters the user typed, not wildcards
and not a character class. They agree today — and that is a *verified* answer rather than an
assumption, which is the reason to write the case rather than reason about it.

## A convention that matches by name is a list, and a list becomes a wrong list (phase 47)

`TenantIndexConvention` says, in its own docstring, that "an entity that carries `OrganizationId`
*and* a business date is a document" and gets both the report index and the list index. It recognises
that business date by **name**: `Date`, then `PostedAt`. `Cheque` spells its `ChequeDate`, so it
falls through to the master-data branch, keeps only its hand-written `(OrganizationId, Status)` — and
its list's existing `ORDER BY ChequeDate DESC` has been a sort over the whole filtered set since
phase 17.

The convention does not throw for it, because the throw asks a different question: *does this
tenant-scoped entity have any leading-`OrganizationId` index at all*, and `(OrganizationId, Status)`
satisfies that. So the gap is silent in both directions.

Phase 30's rule, in an unexpected place: **a list sampled from a few screens becomes a wrong list;
find the rule.** Here the rule would be a marker or a mapped property rather than a name list — and
the reason phase 47 carried it rather than adding `"ChequeDate"` is phase 34c's: an index added for
one access path changes the plan for every other path over the same table, so it wants a measurement
on the 50k dataset, not a one-line edit at the end of an unrelated phase.


## Angular's `DatePipe` silently ignores the calendar toggle (phase 48)

`NFR-1.1` says a date a user reads is rendered in the calendar that user chose, and phase 23 built
`NepaliDatePipe` plus a sweep guard to enforce it. The guard enforced one half. It banned native date
**inputs** (`<input type="date">`) and inline `.toFixed(2)`, and nobody had asked the mirror
question — *which dates does this app **output**, and do they honour the toggle?*

The answer was **23 uses of `| date:` across 19 templates**, plus four raw ISO interpolations in
`activity-panel.html`. `| date:` is not a near-miss on the house format; it is Angular's own
`DatePipe`, which knows nothing about `DatePreferenceService` and renders a Gregorian date in the
browser's locale. A tenant reading in BS saw `Approved Sep 14, 2026, 7:24:44 PM` underneath a
document whose own Invoice Date, three inches away, said `29-05-2083`. One screen, two calendars.

Three things make this worth a heading rather than a line.

**It hid behind a guard that looked like it covered the area.** `sweep-guard.spec.ts` is *the*
"dates a user reads" file, it had been green for 25 phases, and it was only ever asked about inputs.
A guard's name is not its scope.

**One of the sites had a recorded reason.** `user-log-page` carried, since phase 26c: *"The timestamp
is rendered with Angular's own `DatePipe`, not `NepaliDatePipe`: this is a to-the-second audit trail,
and the seconds matter more here than the calendar does."* Read once that is a trade-off. Read again
it is a false choice — a BS date carries seconds perfectly well — and the consequence was that the
one report where being certain *which day* a sign-in happened on matters most was the one report
showing the wrong calendar. That is phase 45's lesson (*an allow-list reason can be the argument for
the opposite conclusion*) arriving through a code comment instead of an allow-list. The fix was a
third pipe mode, `'datetime-seconds'`: keep the seconds, obey the calendar.

**The remedy is a mode, never a second pipe.** Two pipes for one calendar is exactly the divergence
phase-26b's shared `bs-date` table exists to prevent. `NepaliDateMode` is `'date' | 'datetime' |
'datetime-seconds'`, and the time is rendered from *the same shifted instant* that produced the date,
so the two halves cannot disagree about which day it is.

When mapping the old formats, preserve what each screen already showed rather than imposing a house
style: Angular's `'medium'` includes seconds and `'short'` does not, so they map to different modes.


## Slicing an instant to ten characters gives you the UTC day (phase 48)

`NepaliDatePipe` tolerated a full timestamp with `value.length > 10 ? value.slice(0, 10) : value`,
which reads as defensive and is a bug. Nepal is UTC+05:45, so between 18:15 and 24:00 UTC that slice
names the day **before** the one the event happened on in Kathmandu.

This was not confined to the phase that found it. `transaction-list-page` has piped `approvedAt` and
`createdAt` — both `DateTimeOffset` — through this pipe since phase 26a, and has therefore been
dating every late-evening approval to the previous day, on a screen whose entire purpose is telling
you when things happened.

**The shift belongs in the pipe, not at the call sites**, and the reason is the useful part: a caller
holding a string cannot tell whether it is a `DateOnly` (no time zone, render as-is) or a
`DateTimeOffset` (an instant, whose day is its Nepal day). The pipe can — the presence of a time
component *is* the discriminator. Anything that decides per call site will be got wrong by the next
person to add a field.

Test it with an instant that straddles the boundary (`20:30Z` renders as the *next* day) and assert
the absence of the UTC day as well as the presence of the Nepal one. A test at noon passes either
way.

And this was the client's **third** copy of `(5 * 60 + 45) * 60_000` — `home-dashboard-page` and
`bs-date-input` each had their own, each with its own paragraph about the same boundary. CLAUDE.md's
rule is to retire copies 1..N before writing copy N+1; `shared/formatting/nepal-time.ts` is now the
only one, mirroring `Domain/Common/NepalTime`.


## A carried item's own count is a starting point, not a measurement (phase 48)

Phase 43 recorded the timestamp defect as `{{ row.createdAt }}` "on two lines" of a component with
"17 hosts", and the roadmap carried that forward. Both facts are true. Neither is the size of the
problem, which was 27 render sites across 20 files — the other 23 being a *different* spelling of the
same mistake that nobody had grepped for.

Phase 43 also recorded that "editing a Deal or a Task still happens through the inline form on its
list". It does not. Those forms are **create-only**, and `grep` for callers of `updateDeal` /
`updateTask` across `web/src/app` returns nothing at all: two commands, reachable over HTTP since
phases 13 and 15, callable from no screen. A typo in a deal's title was permanent. That is phase 31's
rule (*a field is reachable only if you can name the command that writes it **and** the screen that
calls it*) failing on the screen half, in a note that asserted the screen existed.

This is phase 47's *derive the N* generalised: a carried item names a defect its own phase declined
to fix, written from the outside, often in one sentence. **Re-derive the extent before scoping the
work** — grep for the mechanism, not for the instance that was reported.


## A field error has to point at a control, and a table is not one (phase 48)

Phase 47 wired `aria-invalid` / `aria-describedby` / focus movement to 13 document forms and left two
messages banner-only, naming the fix: *"a message that points at the table, which needs a target
element the table does not have yet"*.

Building that shows why it stayed open. `FieldError`'s contract is three bindings on one element, and
**`aria-invalid` is not a supported state of the `table` role** — it is not a global ARIA attribute.
A `<div tabindex="-1">` wrapper would satisfy the guard's string match while making a weaker
accessibility claim than the guard implies, which is worse than leaving it.

The right target was never the table: it is the **Add Line button**. It is a real control, natively
focusable, carries `aria-invalid` legitimately, and it is *what the user presses to fix the problem* —
so `fail()`'s focus move lands on the fix rather than on the evidence.

The general rule: when a validation message is about a *collection*, point it at the control that
adds to the collection. Pointing at the collection's container is an accessibility gesture; pointing
at the button is an instruction.


## A read of the reference product is evidence about the reference product (phase 49)

On 2026-09-16 an expired trial tenant was read against a live one, one endpoint, control run. The
result is unambiguous: `GET /api/v1/me/namespaces` returns `{"data":[], "meta_data":{"total":0,…},
"error":false}` and the portal renders its first-run onboarding modal. **A user whose only tenant has
expired is presented as a user who has never had one.**

The temptation on reading something that clean is to implement it, and phase 49's whole first decision
was not to. What settled it was the **sample**, which is phase 41's rule generalised from fields to
behaviours: both tenants were trials (`subscription_status: "Demo"`, `amount: 0`), so this is one
sample of *trial* behaviour and **zero** samples of paid behaviour, and a vendor retiring a free
trial's workspace while keeping a lapsed customer's books is entirely ordinary — the two are different
products sharing an expiry date field.

Three further things decided it, in the order they carry weight:

- **It contradicts a promise this product already makes.** `SubscriptionExpiryBehavior`'s own 409 says
  *"Existing records can still be viewed, printed and exported"*, and the vendor's Terms price
  read-only access at 25% of the subscription fee — so the end state they *sell* is readable. A tenant
  absent from the list cannot be read at all.
- **It is architecturally much larger than a guard.** `AuthorizationBehavior` verifies org membership
  on every single request and is the only mechanism doing so. "The membership disappears" is therefore
  either a status nothing in the schema models, or a filter in one query that every deep link ignores
  — a lie the picker tells while the app still works.
- **It is a product decision with support consequences**, and this codebase models exactly one actor:
  the Admin who would see the badge is the Admin who can lift it.

The row's `inactive` / `inactive_by_id` / `inactive_at` triple is a plausible mechanism and is
recorded as **not observed** — the expired row cannot be seen at all, which is the whole point.

## A divergence you choose owes a surface (phase 49)

The corollary, and the part that turned a decision into a phase. Deciding not to copy a behaviour is
cheap. What is not cheap is that the alternative had never been shown to anyone: the dev database held
139 organizations accumulated over 49 phases, and **17 of them were already past their term end** —
read-only, indefinitely, with nothing in the product saying so until a write came back 409.

The reference product's choice is brutal and unmissable. Ours was gentle and invisible, which is the
worse of the two pairs. So the divergence had to ship with the surface that makes it defensible:
`OrganizationSummaryDto` carries `TermEndsAt` and `IsExpired`, the picker row renders *"Expired —
read-only"*, and the dashboard's Switch `<select>` puts *"— expired"* in its option text as well,
because an `<option>` carries no badge and a switcher that drops a user into a read-only tenant
unannounced is the same defect in a smaller box.

Two details worth keeping:

- **A tenant with no subscription row is reported live, not expired** — identical to the reading
  `SubscriptionExpiryBehavior` has always taken of a missing row. A picker that marked a tenant
  expired while every write succeeded would be worse than saying nothing.
- **The divergence sentence lives in the code and in a test**, not only in a status doc: the DTO doc
  comment, the Angular model, the template comment, and `ExpiredTenantVisibilityTests`, which asserts
  the row is present *and* that the same tenant's writes are refused. A divergence recorded only in a
  document is one the next person to read the scan will "fix".

## Putting an existing date on a second screen is a date audit (phase 49)

`termEndsAt` had not changed since phase 41. Phase 49 rendered it in one more place, through
`NepaliDatePipe` — which phase 48 correctly made instant-aware — and that is what exposed it.

The Subscription screen wrote `${chosenDay}T23:59:59Z` and prefilled `termEndsAt.slice(0, 10)`. The
two halves agreed with each other and with nothing else, because 23:59:59 UTC is **05:44 the next
morning** in Kathmandu: the moment the same value appeared on the picker, two screens named different
days for one term.

**Both halves had to move together.** The write becomes `${chosenDay}T23:59:59+05:45` and the prefill
becomes `instantToNepal(...).date`. Fixing only the read would have pushed the term end forward one
calendar day on **every save** — silently, on a Save button routinely pressed without editing the
date, with no test in either suite positioned to see it. Existing instants are untouched, so nothing
about enforcement moves; only the displayed day shifts, by one, in the tenant's favour, and the first
re-save writes the Nepal-anchored instant.

Phase 20e's rule — anything scheduled or dated for a tenant uses the Nepal wall clock — reaching the
last tenant-level date that had escaped it. The general form: a date that is *written* in one
convention and *read* in another is invisible until a third reader arrives, and the third reader is
usually a new screen rather than a test.

## A permission boundary is a reason to leave a read where it is (phase 49)

Phase 46 limitation #7 asked for the SMS credit balance on the Subscription screen. The obvious
implementation is to compute it in `GetTenantSubscriptionQueryHandler` beside the other four usage
figures, which is exactly where the other four belong.

It is the wrong place, for a reason that has nothing to do with cohesion. `GetTenantSubscriptionQuery`
is behind `Tenancy.Subscription.View`, which is Admin+**Member** deliberately and for a stated reason:
the Angular shell reads it to decide which feature-gated nav entries to render, so every signed-in
role needs it. The balance is behind `Crm.SmsCreditLedger.View`. Folding one into the other widens the
narrower key by the wider one, quietly, on the query with the largest audience in the app — and a
custom role holding one without the other is exactly the case phase 14's permission matrix exists to
allow.

So the screen reads `ListSmsCreditLedgerQuery` itself (page size 1; the balance is computed
server-side over the whole ledger, never summed over a page — phase 16c's footer-total rule). A role
without the key gets a 403 the screen swallows and **renders no row at all**, which is phase 47's rule
— a counter that says nothing is worse than no counter — applied to a permission instead of a loading
state. The decision stays the server's.
