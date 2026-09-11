# ErpApp

A Tigg-style ERP/CRM/Accounting rebuild for Nepali SMEs. Clean Architecture + CQRS (MediatR) on .NET 10 (LTS), Angular 21 (LTS) frontend, SQL Server via EF Core.

**Read `docs/roadmap.md` first** for what phase we're on and what's next — it holds the completed-phase index table and the forward plan. Full context lives in `docs/`:
- `docs/product-requirements.md` — the PRD (what the product does, for whom, why)
- `docs/architecture-spec.md` — bounded contexts, aggregates, cross-cutting engines (GL posting, document numbering, FIFO costing, authorization)
- `docs/erp-module-scan.md` — raw research: a live walkthrough of the reference product (Tigg), module by module
- `docs/phase-lessons.md` — the "read this phase doc before touching X" index, one paragraph per phase (the detail that used to live in this file)
- `docs/known-gotchas.md` — the full narrative behind every one-line gotcha below, grouped by area
- `docs/phase-N-status.md` (one per phase) — each phase's full history: what was built, scope decisions with reasoning, bugs hit and fixed. **Read the relevant one before touching that phase's area.** Consult via TL;DR and targeted Grep, not full-file reads.

## Phase index (all complete — one line each; the "before X" hook says when to open the doc)
- Phase 0: Clean Architecture scaffold, CI, Testcontainers harness — `docs/phase-0-status.md`
- Phase 1a: registration, email verification, cookie-JWT login, password reset. Before touching auth/config wiring — `docs/phase-1a-status.md`
- Phase 1b: Organization aggregate, wizard, memberships, invites — `docs/phase-1b-status.md`
- Phase 1c: Role/RolePermission stub, `AuthorizationBehavior` — `docs/phase-1c-status.md`
- Phase 2: generic lookups, TenantSettings, race-safe document numbering, custom-field definitions. Before any raw-SQL EF query or generic-LINQ handler — `docs/phase-2-status.md`
- Phase 3: Contacts, Products, list/detail Angular chrome. Before one component serving `.../new` and `.../:id` — `docs/phase-3-status.md`
- Phase 4: chart of accounts, JournalVoucher, GL posting engine. Before replacing a child collection wholesale in a handler — `docs/phase-4-status.md`
- Phase 5: sales chain Quotation → Invoice → Payment, conversion pattern, Warehouse. Native `<select>` race, part 1 — `docs/phase-5-status.md`
- Phase 6: purchase chain, Expense, DebitNote, TDS. Before any "Convert to X" flow or "reverse of X" posting rule — `docs/phase-6-status.md`
- Phase 7: FIFO stock ledger, COGS, transfers/adjustments; addendum on the Inventory-account posting fix. Before changing what a posting rule debits/credits — `docs/phase-7-status.md`
- Phase 8a–8f: financial/statutory reports (TB, BS, P&L, VAT, TDS, Annex 13, Annex 5). 8b before a report-test suite seeding via real handlers; 8c before seeding a Goods Product in tests; 8f is the confirm-live-first precedent — `docs/phase-8a-status.md` … `phase-8f-status.md`
- Phase 9: Ageing Summary/Statement. Before a generic `IQueryable` helper with a `Func` selector, or a hand-written permission-seed migration — `docs/phase-9-status.md`
- Phase 10: Contact Overview tab — `docs/phase-10-status.md`
- Phase 11: payment-allocation fix. Scripted-E2E environment gotchas — `docs/phase-11-status.md`
- Phase 12: Transaction Approval queue. Why every `IOrganizationScoped` request must implement `IRequirePermission` — `docs/phase-12-status.md`
- Phase 13: Tasks (`WorkTask`). Before naming a Domain type after a BCL word — `docs/phase-13-status.md`
- Phase 14: custom roles + permission-matrix editor. `PermissionKeyCatalog` auto-discovery — `docs/phase-14-status.md`
- Phase 15: CRM Deals — `docs/phase-15-status.md`
- Phase 16a: Void lifecycle + LockDate. Before building any document's reversal, or running `dotnet ef` after a partial rebuild — `docs/phase-16a-status.md`
- Phase 16b: discounts retrofit. Before adding any per-line/per-document adjustment field — `docs/phase-16b-status.md`
- Phase 16c: pagination + `.xlsx` export. Before a footer total on a paginated screen or any file-download endpoint — `docs/phase-16c-status.md`
- Phase 16d: System Audit report (`AuditBehavior`) — `docs/phase-16d-status.md`
- Phase 17: Quick Payment/Receipt, Bank Accounts, Cheques, allocation, Opening Balances — `docs/phase-17-status.md`
- Phase 18: file storage, attachments, personnel, comments, SMS. Before a second polymorphic parent entity or an `IFormFile` endpoint — `docs/phase-18-status.md`
- Phase 19: reporting tags + remaining reports. GL-report tests must bracket `UtcNow`, not fixed dates — `docs/phase-19-status.md`
- Phase 20a: custom fields on forms (save inline with the document). Before a second "data attached to a document" editor — `docs/phase-20a-status.md`
- Phase 20b: Custom Status (a list-grid-only control). Before assuming where a control renders or which document types have it — `docs/phase-20b-status.md`
- Phase 20c: CostTerm lookup — `docs/phase-20c-status.md`
- Phase 20d: printing templates descoped to metadata + QuestPDF print pipeline. Before building a "gallery of layout variants" screen — `docs/phase-20d-status.md`
- Phase 20e: Alert Scheduler, the first background job (runner/decider split, claim-then-act idiom, no job identity). Before adding any background job — `docs/phase-20e-status.md`
- Phase 20f: feature-flag enforcement (`FeatureGateBehavior`). Before gating anything behind a tenant flag — `docs/phase-20f-status.md`
- Phase 20g: Turnstile bot-check on registration — `docs/phase-20g-status.md`
- Phase 21a: async job foundation + bulk import; `IJobActingUser`. Before a background job that writes, or a concurrency token on a job row — `docs/phase-21a-status.md`
- Phase 21b: full-tenant export; shared `QueuedJobRunnerHostedService`, artifact retention. Before a job that produces a file, or writing to `IFileStorage` from a job — `docs/phase-21b-status.md`
- Phase 21c: migrated tax-register import + migrated register reports. Before a fourth background job or a lifecycle-free reportable aggregate — `docs/phase-21c-status.md`
- Phase 22: document inbox with AI extraction. Before sending tenant data to a third party, or any cross-document prefill — `docs/phase-22-status.md`
- Phase 23: Nepali localization (dates stored AD, BS is presentation only, range 2000–2092). Before rendering any date/amount or any app-wide sweep — `docs/phase-23-status.md`
- Phase 24: variants are Products with a parent pointer. Before a second "child of a Product" concept or appending to a tracked parent's collection — `docs/phase-24-status.md`
- Phase 25: manufacturing (BOM → Production Order → Production Journal, perpetual-inventory posting). Before a value-transforming posting rule, a shared FluentValidation helper, or a browser pass in a non-interactive session — `docs/phase-25-status.md`
- Phase 26a: the five missing Accounting reports + FR-9.1's Compare column. Before a period-over-period comparison, or a report joining `GlLine` back to its document — `docs/phase-26a-status.md`
- Phase 26b: Receivable/Payable + Sales/Purchase analytics (13 reports), server-side BS calendar. Before ageing anything, or a fiscal-year-keyed report — `docs/phase-26b-status.md`
- Phase 26c: Reports catalogue completed (inventory reports, return registers, Net Trading Assets, Exceptional, User Log). Before a stock report, a report that must agree with a register, or a write on an unauthenticated path — `docs/phase-26c-status.md`
- Phase 27a: Custom Fields/Status/Reporting Tags/Tasks-Documents-Activity swept to every type; polymorphic `Comment`. Before adding a `DocumentType` member or a second mechanism sweep — `docs/phase-27a-status.md`
- Phase 27b: print for all 15 types, BS dates in server output, last pagers, wizard Turnstile, feature route guard, first `CustomTemplate` consumers. Before adding a print type, a server-rendered date, or a template consumer — `docs/phase-27b-status.md`
- Phase 28: multi-currency (Currency list, rate on 12 types, fold on posting-rule inputs, realised forex on allocation). Before converting anything into the GL or gating a feature flag — `docs/phase-28-status.md`
- Phase 29: landed cost (Additional Cost on the Purchase Bill, capitalised into FIFO layers, Landed Cost Clearing account). Before capitalising into a stock layer or adding a tenant-default GL account — `docs/phase-29-status.md`
- Phase 30: Communications (Send Email on 6 types + Contact, Email Logs, Email Templates, `AlertMedium.Sms`). Before wiring Send Email, or a job that reads through a permission-gated request — `docs/phase-30-status.md`
- Phase 31: credit control + the Configurations > General screen; three dead settings enforced; stored `DueDate`; cheque bounce voids its payment; subscription expiry. Before enforcing a tenant setting, a second confirmable warning, or a NOT NULL column on a populated table — `docs/phase-31-status.md`
- Phase 32: Billing Locations (HeadOffice seeded, cap at one, nullable `LocationId` on 17 types, Advanced panel, location-wise numbering). Before declaring a screen un-confirm-liveable, or letting a setting choose which types store a field — `docs/phase-32-status.md`
- Phase 32b: per-location permission scope (second matrix, nullable `RolePermission.LocationId`, one branch in `AuthorizationBehavior`). Before a permission that depends on an unread row, or a request over a location-bearing type — `docs/phase-32b-status.md`
- Phase 33: platform chrome (global search, History, Quick Links, the `UserPreference` per-user store). Before a per-user setting or a cross-type search — `docs/phase-33-status.md`
- Phase 34a: WCAG 2.1 AA sweep + `a11y-sweep-guard.spec.ts`. Before adding a template, choosing a colour, or scripting an edit between two anchors — `docs/phase-34a-status.md`
- Phase 34b: the shell on `NavigationCatalog` (zero page-template edits), Reports index, list chrome (search on 25 queries, date range on 16). Before a paginated list query, a displayed-but-unowned filter, or `overflow` on a layout container — `docs/phase-34b-status.md`
- Phase 34c: scale (NFR-5.1/5.2) — the 50k-invoice dataset in `tools/scale/`, a p95 budget per class of screen, `TenantIndexConvention` (50 indexes from a rule), the shell `@defer`ed. Before adding an index, mapping a tenant-scoped entity, or quoting a performance number — `docs/phase-34c-status.md`
- Phase 35a: ledger drill-down (`?accountId=` + a View Ledger row action) and the location picker/filter swept onto all 15 document forms and lists. Before adding a field to many aggregates at once, or trusting a gotcha's generalisation over an experiment — `docs/phase-35a-status.md`
- Phase 35b: the location dimension in the reports — `LocationId` stamped on `GlJournalEntry`/`StockMovement`/`StockLedgerEntry`, the filter on 36 queries and 43 screens, `LocationWiseReportPermission` made real. Before filtering an append-only fact table, extracting a shared `Where`, or proving a location permission — `docs/phase-35b-status.md`

## Stack & conventions
- Backend: .NET 10 (LTS), Clean Architecture (`src/Domain` → `src/Application` → `src/Infrastructure`/`src/Api`), CQRS via MediatR, FluentValidation, EF Core + SQL Server.
- Frontend: Angular 21 (LTS), in `web/`.
- Solution file is `ErpApp.slnx` (the new .NET 10 format, not `.sln`).
- Dependency rule: `Api → Application → Domain`; `Infrastructure → Application/Domain`. Nothing depends on `Infrastructure` or `Api` except `Api/Program.cs` (the composition root).
- Every command/query goes through the MediatR pipeline: `LoggingBehavior` then `ValidationBehavior` then `AuthorizationBehavior` (see `src/Application/Common/Behaviors/`). A command/query is only permission-gated if it implements `IRequirePermission` (`Application.Common.Security`) — and every `IOrganizationScoped` request **must** implement it, since `AuthorizationBehavior` is the only mechanism verifying org membership at all.
- Multi-tenancy: single database, shared schema, `OrganizationId` discriminator. No EF Core global query filter exists — every handler manually filters by `OrganizationId` in LINQ; a shared `ITenantEntity`/global-filter mechanism is a deliberately deferred infra decision, not assumed to exist.
- Every transactional aggregate (Invoice, PurchaseBill, JournalVoucher, etc.) follows Draft → Approve lifecycle; document numbers are assigned **at Approve, not at Create** (confirmed live in the reference product).

## Build & test commands
```
dotnet restore ErpApp.slnx
dotnet build ErpApp.slnx
dotnet test ErpApp.slnx          # Api.IntegrationTests needs Docker Desktop running (Testcontainers)
dotnet run --project src/Api     # Swagger at https://localhost:7104/swagger, health check at /health

cd web
npm ci
ng build
ng test --watch=false
ng serve                          # dev server, calls the API via web/src/environments/environment.development.ts
```

EF Core migrations:
```
dotnet ef migrations add <Name> --project src/Infrastructure --startup-project src/Api
dotnet ef database update --project src/Infrastructure --startup-project src/Api
```

Local SQL Server connection string, `Jwt:SigningKey`, and `Email:*` (SMTP) are all set via `dotnet user-secrets`, not in `appsettings.json` (see `src/Api/appsettings.Development.json` for the pointer commands). Never commit real values for any of these.

`gh` (GitHub CLI) is installed at `C:\Program Files\GitHub CLI\gh.exe` and authenticated as `lekhu-awasthi` — use it for PRs, CI log/artifact inspection, etc. If a fresh shell doesn't have it on `PATH`, invoke via the full path.

## Working practices (recurring cross-phase disciplines)
- **Every phase ends with its own `docs/phase-N-status.md`** (scope decisions with reasoning, bugs hit and fixed) and a refresh of this file's Current status section — see the update rule there.
- **Unconfirmed screen shape → confirm live before coding.** If `erp-module-scan.md` never opened the screen in its hands-on pass, read the live Tigg UAT tenant through the Browser pane first (the user logs in themselves — never enter credentials, never commit them). The Phase 8f Annex 5 lesson: the speculative design and the real screen had nothing in common. And when a screen appears unreadable because a feature is *off*, that is a fact about the tenant, not the feature — **ask whether another tenant, account or plan can show it before invoking phase-21c's derive-instead precedent** (phase 32: a second tenant existed, and four already-written scope decisions were wrong).
- **Permission keys are derived per feature, not defaulted.** Flat per-transaction registers and anything exposing PAN/contact identity → Admin-only; bounded rollups and routine daily-use working data → Admin+Member. Record the reasoning in the status doc. New `PermissionKeys.cs` constants are auto-discovered by `PermissionKeyCatalog` (reflection), but the permission-seed migration must go through `RolePermissionConfiguration.HasData` first or the scaffold is silently empty.
- **Manual E2E bar:** seed master data via direct API calls (curl + cookie jar), reserve browser clicks for the phase's own new UI; prove at least one negative path (a 403 naming the exact key — against a nonexistent id, so 403-not-404 proves the behavior fired before the handler); verify persisted data via `sqlcmd` when a UI value could lie (see the select-race gotcha). A reusable Admin test login (email/password) persists across phases in local `dotnet user-secrets` under the `Testing:*` keys (never committed) — reuse that identity, but still create a **fresh Organization per phase** so seeded data doesn't accumulate across phases' baselines; run `dotnet user-secrets list --project src/Api` to see the key names (not values).
- **Context discipline:** one phase = one session (start from `docs/roadmap.md` and the relevant `docs/phase-N-status.md`; don't continue a finished phase's thread). Each session ends by generating the **next session's kickoff prompt** (the user pastes it into a fresh session): name the phase from `docs/roadmap.md`, list what to read (roadmap entry, scan sections incl. the 2026-09-02 confirm-live appendix, `phase-lessons.md` paragraphs, `known-gotchas.md` headings, prior status-doc TL;DRs), the open confirm-live questions, the scope decisions to make, and the exit bar — without restating this file, which every session loads anyway. Start each `phase-N-status.md` with a short TL;DR block so future sessions can read just the header unless the task needs a specific section; consult docs via targeted search (Grep/section reads), not full-file reads.

## Known gotchas (one line each — the full story for every entry is in `docs/known-gotchas.md`, under the same headings)

**Configuration, hosting, pipeline and auth**
- Never read configuration as a top-level statement in `Program.cs` before `.Build()` if the value is captured into a later-running closure; bind lazily (`AddOptions<T>().Bind(...)`) — user-secrets mask the bug locally (phase-1a #8).
- Every new `AddOptions<T>()...ValidateOnStart()` needs its test-only keys added to all four host-booting `Api.IntegrationTests` suites' `AddInMemoryCollection` in the same commit, or CI alone goes red (currently `Jwt`, `Email`, `Turnstile`).
- Don't reproduce that CI failure with `ASPNETCORE_ENVIRONMENT=Production` — it also flips `ThrowOnBadRequest`, breaking `ExceptionHandlingTests` spuriously; delete the key from one suite's in-memory collection instead.
- `dotnet run --no-launch-profile` starts in **Production**, so user-secrets don't load and any `ValidateOnStart` option sourced from them kills start-up; pass `ASPNETCORE_ENVIRONMENT=Development` too (phase-27b).
- `RequestCalendar` is the codebase's one ambient (`AsyncLocal`) request value, and only because its consumers are ~40 static export methods with no `HttpContext`; treat it as `CultureInfo.CurrentCulture`, not a precedent (phase-27b).
- Set `options.MapInboundClaims = false` on JWT bearer, or `FindFirstValue(JwtRegisteredClaimNames.Sub)` silently returns null.
- The auth cookie must be `SameSite=None` (not `Lax`) plus `Secure` for `http://localhost:4200` to receive it from `https://localhost:7104`.
- `Response.Cookies.Delete` only clears a cookie when its `Path`/`Secure`/`SameSite` options match the ones used to set it.
- `IOptions<T>` caches at first resolution and never sees a later `dotnet user-secrets set`; restart the Api (long-lived singletons should use `IOptionsMonitor`).
- MediatR 12.4.1's `RequestHandlerDelegate<TResponse>` is parameterless — call `next()`, not `next(cancellationToken)`.
- A FluentValidation rule built from a captured `Func` selector 500s every endpoint it guards (`Could not infer property name`) and no handler test can see it; take `Expression<Func<T, IEnumerable<TElement>>>` and cover it with a validator test (phase-25).

**EF Core, migrations and the InMemory provider**
- `dotnet ef` needs `Microsoft.EntityFrameworkCore.Design` referenced by the `--startup-project` (`Api`), not just `Infrastructure`.
- Validating a migration against a scratch/Docker database does not apply it to the dev database; always follow with a plain `dotnet ef database update`.
- `migrations add` orders operations by model diff, not data safety — read any migration that replaces or retypes a column and reorder by hand (phase-1c bug #1).
- `migrations add` bundles the entire pending diff into one migration; plan one migration per invocation and hand-review it (phase-2 bug #6).
- Rebuilding only `Infrastructure` between `dotnet ef` calls leaves `Api`'s output stale and re-scaffolds an applied diff; let `dotnet ef` rebuild (omit `--no-build`) or rebuild the solution (phase-16a).
- An enum property with `.HasDefaultValue(x)` where `x != default(TEnum)` needs `.ValueGeneratedNever()`, or EF substitutes the SQL default whenever the in-memory value equals `default` (phase-2 bug #2).
- `Database.SqlQuery<T>` accepts only composable SQL (a plain `SELECT`); do atomic read-and-increment as `SELECT ... WITH (UPDLOCK, ROWLOCK)` + a separate `UPDATE`, and alias a scalar result `AS Value` (phase-2 bugs #3–4).
- A handler generic over an interface-constrained type must read properties via `EF.Property<T>(x, nameof(...))`, be registered against the closed 2-arg `IRequestHandler<,>`, and never pass a captured `Func` selector into `Where` (phase-2 bugs #1/#5, phase-9 bug #1).
- Replacing an entire encapsulated child collection in one save mis-tracks on InMemory; snapshot and `RemoveRange`/`AddRange` through the child `DbSet` (phase-4 bug #1).
- A child appended to an already-tracked parent's encapsulated collection is detected as `Modified`, not `Added` — same remedy, have the Domain method report the change and `AddRange` it (phase-24 bug #1).
- `TestAppDbContext` has no `ApplyConfigurationsFromAssembly`, so every encapsulated collection must be restated there with `HasMany...SetPropertyAccessMode(Field)`; its symptom is the identical `DbUpdateConcurrencyException`, so check the test context first.
- SQL Server treats NULLs as equal in a unique index; a unique index over a nullable column needs `.HasFilter("[Col] IS NOT NULL")`, and InMemory enforces neither half.
- `EF.Functions.Like` cannot be translated by InMemory; write `String.Contains`, which SQL Server turns into the same `LIKE`.
- An extraction is not done until the copies it replaced are deleted: `GrantedPermissionReader` said it had replaced two inlined joins and had not, so both missed phase-32b's `LocationId == null` filter and read a branch grant as organization-wide. Before writing copy N+1 of a pattern, grep that copies 1..N were retired (phase-33).
- A shared helper replacing a per-handler `Where` must own **every** condition that `Where` carried, not just the interesting one — there is no global query filter here, so nine handlers rewritten is nine chances to drop `OrganizationId`; take the organization as an argument (phase-35b).
- An append-only fact row (`GlJournalEntry`, `StockMovement`, `StockLedgerEntry`) points back with `(SourceDocumentType, SourceDocumentId)` and nothing else, so it cannot be filtered by any of its document's attributes without a column; stamp at write time and have reversals **inherit** rather than re-derive (phase-35b).
- An expression tree does not short-circuit, so `!flag || list.Contains(x)` hands EF a **null** list to translate — and only on the unrestricted branch, i.e. almost every caller. Compose a second `.Where()` (phase-33).
- …but that generalises only to a captured **bool**: `collection == null || collection.Contains(x)` is funcletized to a constant and folded away, and returns 200 on SQL Server. Compose anyway; don't call an instance of it broken without running it (phase-35a, correcting phase-33).
- A shared matcher cannot live inside a LINQ predicate: a **static call** is untranslatable and so is `Contains(term, StringComparison)` — and InMemory evaluates both in C#, so every handler test passes while all 25 endpoints 500 (phase-34b, phase-25's captured-`Func` through another door).
- Single-argument `Contains` is case-**insensitive** on SQL Server (collation) and case-**sensitive** on InMemory; a handler test must search with the stored casing or it pins a behaviour production lacks (phase-34b).
- Every tenant-scoped table needs an index **leading on `OrganizationId`**; 18 had none, and they were exactly the documents, because master data got one free from its per-tenant uniqueness rule. `TenantIndexConvention` derives all three families and throws at model build for an entity it cannot classify (phase-34c).
- An index added for one access path changes the plan for **every other path over the same table**: `(OrganizationId, CreatedAt)` made the invoice list 10× faster and a non-matching search 1.8× *slower*, because the optimizer swapped one scan for a seek plus a key lookup per row. Re-measure the paths you did not change (phase-34c).
- A materialised id list handed back to SQL becomes an `OPENJSON` parameter as long as the list; a report that loads its period then re-queries children by `ids.Contains` is linear in the period, not in the page. `JournalReportQueryHandler` is the shape that is not (phase-34c).
- On a bulk-`INSERT`-seeded database, statistics quality moves a report that joins to a line table by 2× — more than most changes under test. `UPDATE STATISTICS ... WITH FULLSCAN` on both sides before comparing anything (phase-34c).
- Read a handler's `Where` before assuming it matches its request — `ListPaymentsQueryHandler` shipped with a hardcoded `Direction == Received` (phase-6 bug #2).
- A store-side aggregate (`GroupBy...Count()`) must run after the `SaveChangesAsync` that persists what it counts; tracked-but-unsaved rows are invisible to it (phase-21a).
- A Domain factory/mutator can stay `internal` only while its sole caller is in the Domain assembly (phase-7 bug #1).
- Never name a Domain type after a common BCL word (`Task` → `WorkTask`) (phase-13).
- A filter over a tree of tenant-defined groups must match on group **id**, never group name — names are not unique across a chart of accounts (phase-26a bug #1).
- `decimal` has a signed zero: `-0m` keeps its sign bit and surfaces as `-0` / `-0.00` once cast to `double` for a spreadsheet cell. Accumulate a magnitude only when the value is strictly non-zero — no test catches this, because `-0m == 0m` (phase-26c bug #1).

**GL posting, documents and domain invariants**
- A "reverse of X" posting rule can balance its own entry while leaving a paired control account (AP net of TDS) permanently off; trace the net effect on every account across original + reversal (phase-6 bug #3).
- Reversals mirror the original entry's own posted lines via `GlJournalEntry.PostReversalOf` (a second entry, never a mutation); never re-derive a reversal from the posting rule (phase-16a).
- `ReferrerType`/`ReferrerId` enforce nothing — a conversion needs `MarkConverted`, quantity/rate caps net of prior reversals, and contact/TDS consistency checks in the Create handler (phase-6 bug #4).
- Goods purchases debit `DefaultInventoryAccountId` (post-Phase-19 fix); a live inventory value still comes from `StockLedgerEntry.QuantityRemaining × UnitCost`, not that GL balance (phase-19 bug #1, phase-7 addendum).
- GL-report tests must bracket `DateOnly.FromDateTime(DateTime.UtcNow)`, because `PostedAt` is stamped at Approve time, not from the document date (phase-19 bug #2).
- Anything scheduled or dated for a tenant uses the Nepal wall clock via `Domain/Common/NepalTime` (fixed UTC+05:45, not `TimeZoneInfo`); test an after-local-midnight case, not just an evening-UTC one (phase-20e).
- A FIFO layer stores a unit cost rounded to `ProductionJournal.UnitCostScale`; build a value-transforming GL entry from the values actually created and name the rounding residue (phase-25).
- `GlJournalEntry` stores no copy of its document's number, reference or business date — only `SourceDocumentType`/`SourceDocumentId`/`PostedAt`; any report showing those must join back across the 11 GL-posting types, and must show the same date field it filters on (phase-26a).
- A dated stock report must derive from `StockMovement`, never from `StockLedgerEntry`: `QuantityRemaining` is decremented **in place**, so the FIFO table only ever answers "as of now" and a report for a closed period would silently answer today's question. Opening+In-Out over the append-only movements reconstructs both quantity and value at any date, and equals the FIFO figure today (phase-26c).
- `StockLedgerService.ConsumeAsync` **throws** on an oversell, so no path in this codebase can drive a stock balance negative — the reference product's Negative Item Balance setting (Reject/Warn/Do Nothing) is unbuilt. Don't write a test for a negative-balance branch; pin the throw instead (phase-26c).
- A Debit Note line carries no `ExpenditureClassification` or `IsImport` of its own; both are resolved from the source Purchase Bill's matching line by (PurchaseBillId, ProductId, Rate, VatRate) (phase-19, phase-26c's `PurchaseReturnReader`).
- Convert a currency on a posting rule's **inputs**, never on its finished `GlLineInput` list: every rule derives its balancing leg as a *sum* of the others, so converting afterwards rounds that leg independently and breaks `sum(Debit)==sum(Credit)` intermittently (phase-28).
- Never convert twice: FIFO unit costs, COGS and historical `GlLine`s are already base currency. `ApprovePurchaseBillCommandHandler` is the one place a document rate reaches the stock ledger, and it rounds to 4 dp (`ToBaseUnitCost`), not 2 (phase-28).
- Changing what a posting rule debits changes what every *reversal* of it owes: a Debit Note credits Inventory the return price while `ConsumeAsync` relieves layers at their landed cost, so phase 29's capitalised cost needed its own release leg or Inventory drifted above the ledger one return at a time (phase-29, phase-6 bug #3 again).
- Build a capitalisation leg from the value the ledger actually received (`layer value created − goods amount`), never the figure the user typed, and round each unit cost **once** at the ledger's own scale from the line's total landed value; the gap is the named residue (phase-29, phase-25's rule on a second aggregate).
- When a phase adds a tenant-default GL account, grep `web/` for the field name before calling it done — phase 25's and phase 28's three accounts reached the API and no screen, so they could not be configured at all (phase-29; phase-23 bug #1 in reverse).

- A tenant-level field is reachable only if you can name the command that writes it and the screen that calls it; a read path proves nothing about the write path (phase-31).
- Two confirmable warnings on one document need two override flags and a `warningKind` on the 422, or confirming the first waives the second (phase-31).
- A non-nullable column on a populated table needs a hand-written backfill; the scaffold's `DEFAULT '0001-01-01'` back-dates every row and leaves a stray constraint (phase-31).
- A permission that depends on the requested value as well as the loaded row is the `AttachmentAccess` pattern; the E2E must show 404 on a missing row and 403 on a real one (phase-31's cheque bounce).
- Never weaken a Domain invariant so a test can reach a state only time produces; reach through EF's change tracker (phase-31's expired `TenantSubscription`).
- When NULL in a unique-indexed column is an at-most-one sentinel, the unfiltered index is the enforcement and EF's automatic `IS NOT NULL` filter destroys it; `HasFilter(null)` is load-bearing (phase-32's numbering counter).
- When a tenant setting selects among sets of document types, the schema owes the widest set, or flipping the setting is a lie until later columns ship (phase-32's `LocationScopeMode`).
- Once missing one per-handler re-check is an open door, the check moves into `AuthorizationBehavior` (not a sixth behavior; a nested `ISender.Send` corrupts a scoped context) behind marker interfaces and a build-failing sweep guard (phase-32b).
- A confirm-live pass can falsify an earlier one: a recorded inference about a control nobody operated is not settled (phase-32b's `LocationWiseReportPermission` scopes report rows, not keys).
- Reuse a marker interface by reading it, not merging it, when its member set is narrower than the new grant's (`ILockDateSensitiveDocument`, phase-32b).

**Background jobs**
- A singleton `BackgroundService` cannot inject scoped services; take `IServiceScopeFactory`, read options via `IOptionsMonitor`, and never let a tick's exception escape `ExecuteAsync` (`AlertSchedulerHostedService`).
- No `IsRowVersion()` token on a row a job writes repeatedly if a user can also write it — a cancel wedged a running import; the unique index on the occurrence key is the real correctness mechanism (phase-21a Decision C, bug 1).
- A job that writes must reuse the Create/Update commands under the initiating user's identity via scoped `IJobActingUser` (an `HttpContext` always wins), which re-checks permissions per row for free (phase-21a).
- Do-exactly-once means write and commit the claim row under a unique index before the external side effect; InMemory does not enforce unique indexes, so verify the race against SQL Server (phase-20e Decision C).
- Any feature that writes a blob needs its deletion story decided with it; reuse `IQueuedJobProcessor.SweepAsync` + `JobArtifactRetention.Period`, and delete the blob before stamping the row (phase-21b Decision E).
- A job that produces a file builds it into a buffer, then commits storage key and terminal status in one `SaveChangesAsync`; UIs gate Download on `HasArtifact`, never on `Status == Completed` (phase-21b).
- A job needs an acting identity when it sends a **MediatR request**, not when it writes; an email job that only reads still needs `IJobActingUser` because it renders its PDF through the permission-gated `PrintDocumentQuery` (phase-30).
- Phase-21a's "no concurrency token on a job row" is a rule about rows with **two** writers; a row with one writer (nothing edits a send) can carry a rowversion and get real compare-and-set (phase-30).
- Do-exactly-once plus "a resend is a new row" needs a client-minted **request id** under a unique index — not an occurrence key (no schedule) and not a content hash (it would swallow a legitimate resend); the first intent wins even if the duplicate carried different content (phase-30).

**Files, ClosedXML, uploads and downloads**
- Sync-only writers (ClosedXML `SaveAs`) cannot target the live response stream; write to a `MemoryStream`, then `CopyToAsync` (`ReportSpreadsheetExporter.WriteWorkbookAsync`, phase-16c bug #3).
- A Minimal API endpoint binding `IFormFile` gets antiforgery metadata automatically and 500s unless it calls `.DisableAntiforgery()` (phase-18 bug #1).
- Executing `Results.Stream` against a bare `DefaultHttpContext` needs a `ServiceProvider` with `AddLogging()` (`MigratedRegisterTemplateRoundTripTests`).
- A `MultipartFormDataContent` under `using` in a helper that returns the `Task` unawaited is disposed mid-send (`ObjectDisposedException` from `TestHost`); await inside the helper (phase-22).
- ClosedXML returns empty text for hand-rolled `inlineStr` cells and ignores `<si>` past a stale `uniqueCount`; build import fixtures by filling the app's own generated template (phase-21a).
- `AdjustToContents()` measures every cell it is given — size columns over the header plus a sample of rows, state the row cap, and disclose truncation in the artifact (phase-21b).
- Import-template date columns need an explicit day-first-before-month-first format list, never bare `DateTime.TryParse`; assert the ambiguous case (`ImportRowReader.GetOptionalDate`, phase-21c).

- A trailing optional parameter added to a command reaches nothing until the Api's own request record carries it too — it compiles, every test passes, and the field binds silently to `null` (phase-27b's `Terms`).
- The mirror of that on the read side: a **list** query returning the aggregate exposes a new field for free, while a **detail** query projecting a DTO drops it silently — the write path looks perfect and the form can never show the stored value (phase-32's `GetInvoiceQuery`, caught only by an E2E that re-read what it wrote).
- That read-side gap is the default, not the exception: phase 32's write-path sweep guard stayed green while **14 of 15 detail DTOs and all 5 conversion templates** dropped the same field, so a form stored a branch, never showed it, and overwrote it on the next save. Adding a field to many aggregates owes three assertions — write, read, and every prefill between (phase-35a).

**Angular**
- A component serving both `.../new` and `.../:id` must read the id from `route.paramMap` (an Observable) and re-derive "is new" on every emission (phase-3 bug #1).
- Annotate `HttpClient.get` `params` as `Record<string, string>`; a union including `{}` silently resolves to the `arraybuffer` overload (phase-3 bug #4).
- Don't share a `request$` variable across Create/Update when the result types differ; use an explicit `if/else` (phase-4 bug #3).
- Never bind `[value]` on a signal-fed native `<select>`; bind `[selected]` per `<option>` — this persisted wrong `WarehouseId`s, not just display glitches (phase-5/6/7).
- Footer totals on paginated screens come from a server-computed field over the full filtered set, never a client-side reduce over one page (phase-16c bug #1).
- The app is zoneless: a `computed()` over a plain `FormControl.value` caches forever; track UI-driving values in their own `signal()` written by the control's event handler (phase-17).
- A cached signal created lazily inside a `computed()` throws `NG0600` the moment its source resolves **synchronously** (a test double); real HTTP hides it. `untracked()` the subscribe and the writes (phase-35a's `BillingLocationStore`).
- Bootstrap's JavaScript is not loaded anywhere (`angular.json` has no `scripts`), so `data-bs-toggle` does nothing; drive menus from a signal (phase-22).
- A `.dropdown-menu` inside `.table-responsive` is clipped by the implied `overflow-y`; render it `position: fixed` at coordinates captured on open (phase-22).
- A pipe rendering from a global signal with an unchanging argument must be `pure: false` and memoize internally (`NepaliDatePipe`, phase-23).
- `Domain/Common/BsCalendar` is a verbatim port of the client `bs-date.ts` table; port it, never retype it, and keep both boundaries pinned — a fiscal year runs Shrawan 1 to the last day of Asar and is named by its first BS year (phase-26b).
- A pipe inside parentheses parses anywhere, including a ternary branch, but a bare pipe in a ternary branch does not; pipes stay illegal in event bindings (phase-23).
- A component test asserting an uppercase label fails when the casing comes from CSS `text-uppercase`; assert the source casing (phase-23).
- When a phase starts populating a previously-dead DTO field, grep the templates that consume it — `SalesRegisterQuery`'s export columns were filled and invisible (phase-23 bug #1).
- `<iframe [src]>` needs `DomSanitizer.bypassSecurityTrustResourceUrl` (safe only because the URL is API base + route GUID), while `<img [src]>` with the same string is fine (phase-22).
- `AmountPipe` renders two decimals by default; pass `| amount: 4` for figures legitimately smaller than a cent (phase-25).
- A sweep over `<input>/<select>/<textarea>` cannot see a control a **component** wraps: 121 date fields had a label, an input and nothing joining them. Ask the mirror question — which labels name no control? (phase-34a).
- Bootstrap's brand tones clear WCAG's 4.5:1 against **pure white** and only just, so they fail on this app's `#f8f9fa` body; the four text utilities are re-pointed at its `-600` shades in `styles.scss`, and `contrast-rules.ts` is the measured palette the guard derives from (phase-34a).
- Bootstrap's JS is not loaded, so nothing sets `aria-expanded` for free — every signal-driven popup must set it itself (phase-34a, extending phase-22's gotcha).
- A filter a screen **displays but did not apply** is worse than no filter: anything global a screen both shows and sends must reload that screen when it changes, from the first version (phase-34b).
- A swept-in filter handler must copy what the **sibling** handlers on that page do, not a template: on a paginated page whose reload is `load()` rather than `reload()`, every other filter also calls `page.set(1)`, and without it a narrowing filter applied on page 3 reads as "this branch has no data" (phase-35b).
- An `effect()` cannot tell "the write already being acted on" from "a write needing action" — if the handler that wrote the signal already schedules the response, an effect over it is a race (phase-34b).
- Never put `overflow` on a layout container without asking what is anchored inside it; the rail's `overflow: hidden` clipped a 46rem flyout to 240px, silently (phase-34b, phase-22's gotcha in a second container).
- Deriving beats listing for a **catalogue** (a missing nav entry is an unreachable screen); listing beats deriving for a **curated tray** (deriving loses the curation that is the feature). Guard both the same way — every url must resolve to a real route (phase-34b).

**Multi-way switches on a document-attached mechanism**
- A shared UI *panel* is not evidence of a shared *model*: the reference product shows email templates inside its Custom Templates panel but serves them from a different resource with six extra fields and a disjoint type vocabulary, so `EmailTemplate` is its own aggregate and phase 27b's placeholder `CustomTemplateType.Email` was deleted rather than left dead (phase-30).
- A list sampled from a few screens becomes a wrong list; find the **rule**. Send Email is not on all 15 printable types but on the 6 whose email-template context exists — a rule that also settles the types never probed, asserted in both directions by a guard test (phase-30, correcting phase-27b).
- When a request's real permission key depends on a column of the row the handler is about to load (not on the request itself), `IRequirePermission.PermissionKey` cannot express it — that property is evaluated before the handler runs. Declare a blanket key (Admin+Member, seeded, gates nothing on its own — same shape as `TransactionApprovalView`/`RecentTransactionsQuery`'s pattern) to get through `AuthorizationBehavior`, then re-check the real key inside the handler once the row is loaded, throwing the identical `ForbiddenException` shape so a caller can't tell the layers apart (phase-27a, `AttachmentAccess`).
- Three enums that each name an overlapping-but-distinct vocabulary (a `DocumentType`, and two or three "what can this attach to" parent enums) must never be bridged by ordinal cast — they cannot share an ordinal order once one of them starts with non-document members. Bridge by member *name* (`Enum.TryParse`) and add a guard test picking a member with deliberately divergent ordinals across the enums (phase-27a, `DocumentParentTypes`, restating phase-26a's lesson structurally).

**Testing and manual E2E**
- A vendor's always-pass dummy credential (Turnstile `1x000…AA`) accepts any input; proving the negative path needs the always-fail one (`2x000…AA`) swapped in (phase-20g).
- `UpdateRolePermissionsCommand.Grants` is a dictionary, not a list, and system Admin/Member roles cannot be edited (409) — a negative-permission proof needs a custom role.
- A browser pass in a non-interactive session works by exporting the ASP.NET dev cert, starting the `erp-web-ssl` profile, and transplanting curl's `erp_auth` cookie via `document.cookie` (phase-25 Step 3).
- A registered user has no verification code until `POST /api/auth/request-verification-code`; a Member-role user is the only way to prove a document-scoped 403, since Admin is seeded with every key (phase-27b).
- A test suite that passes with **fewer** tests than the previous run is a failure — check what a rewriting script produced by counting it, not by whether the build is green (phase-27b).
- A guard must assert its input is **non-empty**, not merely defined: Vite returns an empty string for `?raw`/`?inline` on a compiled `.scss`, so `toBeDefined()` passed and every assertion over it was vacuous (phase-34a).
- Prove a guard bites by injecting a regression — but back the file up and restore it **by hash**, never `git checkout --`, which reverts the phase's own work on that file too (phase-34a).
- A uniform sweep is worth more than its subject: asking one question of every paginated list found two queries with **no validator at all** and one whose search term had reached a `LIKE` uncapped since phase 25 (phase-34b).
- `POST /products` takes **`primaryUnitId`**; `POST /accounts` takes **`{name, groupId, kind}`** only (no `code`, and `kind` is an `AccountKind` — `"Normal"` fails to deserialise with a 400 naming no field); a fresh organization has **no warehouse** (phase-34b).
- A bash helper that both **prints and returns** is a trap under `$( )` — it returns the printed line too; fourteen malformed ids became a `PUT` storing nulls that surfaced as a 409 three steps later. Have it set a global (phase-34b, same family as a function whose assignment a subshell discards).
- Map one enum onto another **by name** (`Enum.TryParse`), never by ordinal, and add a test asserting every member has a counterpart — an ordinal cast compiles, works today, and silently reports the wrong value the first time a member is inserted (phase-26a).
- A shared reader that several reports agree through is worth more than each report deriving its own figure: Invoice Age's total balance equals Customer Receivable Summary's closing balance *by construction* because both read `ContactLedgerReader` (phase-26b).
- A curl seed script that pipes approvals to `/dev/null` hides its own failures — the first report just comes back empty. Print every approval's status code. Two live traps: `POST /api/organizations` returns `organizationId`, not `id`, and the GL defaults are **one** `PUT /accounting-defaults` taking all eleven accounts (phase-26c).
- Driving the reference product's Browser pane needs coordinates from `getBoundingClientRect`: its accessibility tree is nearly empty and `find` matches nothing. Its GENERATE control's DOM text is "Generate" — the capitals are CSS `text-transform` (phase-26c).
- A fresh Organization has zero Accounts and zero Account Groups — nothing seeds a chart of accounts — so any E2E needing a Journal Voucher, Cash Transfer, Payment or Expense line must `POST` its own account groups (one per `AccountRootType`, spelled `Asset`/`Liability`/`Equity`/`Income`/`Expense` — singular, unlike the plural `rootType` groupings a list response returns them under) before it can create an account (phase-27a).
- `identity` is a reserved word in T-SQL — reading a verification code needs `[identity].VerificationCodes`, and every document-scoped 403 proof needs a Member user, which needs that code (phase-28).
- `tsc --noEmit -p tsconfig.json` does not typecheck `web/src/app`; it came back clean while `ng build` reported 22 `TS2339` errors. `ng build` is the real check (phase-28).
- Run `ng test` from `web/`, never as `npx --prefix web ng test` from the root: `a11y-sweep-guard.spec.ts` reads `src/styles.scss` via `process.cwd()` and fails for the wrong reason (phase-35a).
- `POST /accounts`'s `kind` is `Other`/`Bank`/`Cash`; `POST /organizations` needs `accountingStartDate` + `workspaceName` and takes the entitlement flags directly; `POST /auth/register` needs `phone`; `grants` is a dictionary but `locationGrants` is a **list** of `{locationId, grants}`; master-data lists are paged, so it is `['items'][0]['id']` (phase-35a).
- A `Reports.*` key **cannot be granted per location** (400: "Only transaction permissions are location-scoped") — a report's location scope comes from the caller's *transaction* grants via `AnyGrantedLocationsAsync`, so the report key is organization-wide and the branch grant goes on e.g. `Sales.Invoice.View` (phase-35b).
- `POST /organizations`'s entitlement flags are `multipleLocations`/`multipleWarehouses`/`trackInventory` — an `...Enabled` suffix binds to false silently and surfaces three steps later as a feature 403; `POST /billing-locations` requires `address`; the invite route is `POST /organizations/{id}/invitations` returning **`membershipId`**; `vatRate` is `ThirteenPercentVat` (a wrong enum member fails as "Failed to read parameter … as JSON", naming no field) (phase-35b).
- `sqlcmd -Q` prints "(N rows affected)" into a captured value; `SET NOCOUNT ON` belongs beside `SET QUOTED_IDENTIFIER ON` at the top of every script (phase-35b).
- A Goods line consumes stock regardless of `TrackInventory`, so on a tenant without that feature a Goods product cannot be invoiced at all (403 on opening stock, 409 on approve); seed a **Service** line when an E2E just needs an approved sales document (phase-30).
- curl cannot read a file for `-F` upload here — every path form gives exit 26 and HTTP `000`, which reads like a server fault; drive the file leg from a short Python `urllib` script (phase-30).
- `POST /api/organizations` needs `industry` and a non-empty `turnstileToken`; accept-invitation is `/api/organizations/memberships/{id}/accept-invitation` with no org segment (with one, a 404 leaves the membership `Invited`); units are `/units-of-measurement` (`shortName`); credit terms are under `/configuration/` (phase-31).
- `POST /accounts` takes `groupId`; `POST /products` takes `type`, not `productType`, and the wrong name silently yields a Goods product that 409s at Approve about the warehouse (phase-32).
- A scripted multi-file edit must assert its anchor count before writing and preserve each file's CRLF/BOM (phase-32).
- `dotnet run --project src/Api` with no `--launch-profile` binds **5155 only**, not the 7104 the Angular dev environment calls; and a stale listener on 5155 makes the https profile fail to start (phase-30).

**Tooling and shell**
- `nvm use` from a shell that cannot create the symlink deletes `C:\nvm4w\nodejs` and reports success; recreate it with `cmd /c 'mklink /J "C:\nvm4w\nodejs" "%LOCALAPPDATA%\nvm\v24.11.0"'`.
- A `cat > file <<'EOF'` heredoc in the Bash tool is silently truncated or mis-parsed well below the ~8 KB figure; use the Write tool, or write a small patch script and run it (phase-26a).
- A script inserting an import after "the last `
import ` line" lands *inside* a multi-line `import { … }` block; anchor on the statement's closing line (phase-35a).
- A lazy `.*?` between two anchors spans the instances in between (it expands past `</label>` to reach a later match), silently merging them; exclude the closing marker — `((?:(?!</label>).)*?)`. The tell is two independent counts disagreeing, so derive the expected number a second way (phase-34a, on top of phase-32's assert-before-writing rule).
- A positional derivation (column index → header text) is *confidently wrong* where the position lies — a `colspan` cell, a cell with its own label. A wrong accessible name is worse than none; audit for the shapes the first pass cannot see (phase-34a).
- In the browser pane the **screenshot is ground truth**: after a viewport resize, `getComputedStyle`/`getBoundingClientRect` can lag the rendering (a drawer measured on-screen while the screenshot showed it tucked away). Reload after emulating a viewport (phase-34b).
- `sed -i` also flips the CRLF of the file you *aimed* it at, after which every `
`-anchored patch script fails its assertion with the anchor looking correct; use Edit for a single substitution, or read the file's own newline (phase-33).
- A `sed -i` over a glob rewrites **every** file it matches, and on Windows that flips CRLF to LF even where the pattern never fires — `git diff` stays empty while `git status` shows a hundred extra modified files. Undoing it needs `rm` *then* `git checkout --`; restrict the file list instead (phase-30).
- When a generator script emits Angular templates through `str.format`, interpolation braces need escaping in the *format string* but not in a substituted value — `{{{{ x }}}}` in a value ships literally and fails as NG5002 (phase-26b).
- A benchmark run against an **empty** tenant looks like a spectacularly fast one — 20 ms p95, every status 200. Only the response size tells them apart, so a harness must assert its target is populated before timing it; `seed-master.sh` rewriting `.seed-ids.env` is the side effect that caused it (phase-34c, the prints-and-returns trap in another costume).
- `sqlcmd -i` runs with `QUOTED_IDENTIFIER OFF`, so any `INSERT` into a table with a filtered index fails; put `SET QUOTED_IDENTIFIER ON` at the top of every script. `-W` and `-y/-Y` are also mutually exclusive (phase-34c).

## Current status

**Phases 0–34c, 35a and 35b are complete.** Phase 35 was split with the user into **35a** (the
document side) and **35b** (the report side); both are done, and with them the Billing Location
dimension reaches every screen that has one.

35b's gate was `GlJournalEntry` having no `LocationId` while seven GL reports filter by one live —
and it turned out the same gap existed on `StockMovement` and `StockLedgerEntry`. All three are
settled by one rule: **an append-only fact row carries the billing location of the document that
created it, stamped at write time** (11 GL posting sites, 15 stock-ledger sites, reversals
inheriting rather than re-deriving, two backfill migrations). On top of that the phase put the filter
on **36 report queries, their handlers and 86 endpoint constructions**, threaded five shared readers,
swept the control onto **43 report screens** through one `app-report-location-filter`, put the
LOCATION column on both Master reports (closing 35a carried item #3), and gave
`TenantSettings.LocationWiseReportPermission` — one consumer for three phases — every report it was
always meant to govern (closing 32b carried item #1). Nine exemptions are recorded with reasons, six
from the live census and three because the underlying row has no location at all.

Two findings outlive the phase. A shared helper replacing a per-handler `Where` must own **every**
condition that `Where` carried: the first `GlEntryLocations` took a pre-filtered queryable and left
nine handlers each responsible for `OrganizationId`, in a codebase with no global query filter. And
a `Reports.*` key **cannot be granted per location at all** — a report's location scope is derived
from the caller's *transaction* grants — which only running the E2E revealed, because the first
negative script asserted a grant shape the API refuses outright.

**Next: phases 36–41 in `docs/roadmap.md`.** Phase 36 opens with a one-line bug that should not wait
behind the rest of it: `featureGuard('MultipleWarehouses')` on `organizations/:id/warehouses` stops a
flag-off tenant creating its *first* warehouse, which Invoice and Purchase Bill both require.
Deferred out of 35b by agreement and carried into 36: **Product-to-location** (what the selector
restricts is unobserved; probing it needs two writes on the live tenant) and the three Moonbeam-only
report filters.

Tests: Domain 443, Application.UnitTests 978, Api.IntegrationTests 18, Angular 291. `dotnet build` /
`dotnet test` / `ng build` / `ng test` all clean. `ng build` still warns the initial bundle exceeds
its 500 kB budget (pre-existing). `tsc --noEmit` does not cover `web/src/app`; `ng build` is the
check (phase-28), and `ng test` must be run from `web/` (phase-35a).

**Update rule for this section:** when a phase completes, add its one-liner to the Phase index above,
append its "read before X" paragraph to `docs/phase-lessons.md`, and replace this block with a
short orientation (what is done, what is next, test counts) — the phase's own story belongs in its
`docs/phase-N-status.md`, never here. Gotchas stay one line here; the narrative goes in
`docs/known-gotchas.md`.
