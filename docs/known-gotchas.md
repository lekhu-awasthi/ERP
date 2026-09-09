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
