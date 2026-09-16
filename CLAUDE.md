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
- Phase 8a–8f: financial/statutory reports (TB, BS, P&L, VAT, TDS, Annex 13, Annex 5). 8b before a report-test suite; 8c before seeding a Goods Product; 8f is the confirm-live-first precedent — `docs/phase-8a-status.md`…`8f`
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
- Phase 25: manufacturing (BOM → Production Order → Production Journal, perpetual posting). Before a value-transforming posting rule or a shared FluentValidation helper — `docs/phase-25-status.md`
- Phase 26a: the five missing Accounting reports + FR-9.1's Compare column. Before a period-over-period comparison, or a report joining `GlLine` back to its document — `docs/phase-26a-status.md`
- Phase 26b: Receivable/Payable + Sales/Purchase analytics (13 reports), server-side BS calendar. Before ageing anything, or a fiscal-year-keyed report — `docs/phase-26b-status.md`
- Phase 26c: Reports catalogue completed (inventory, return registers, Net Trading Assets, Exceptional, User Log). Before a stock report, or one that must agree with a register — `docs/phase-26c-status.md`
- Phase 27a: Custom Fields/Status/Reporting Tags/Tasks-Documents-Activity swept to every type; polymorphic `Comment`. Before adding a `DocumentType` member or a second mechanism sweep — `docs/phase-27a-status.md`
- Phase 27b: print for all 15 types, BS dates in server output, wizard Turnstile, feature route guard. Before adding a print type or a server-rendered date — `docs/phase-27b-status.md`
- Phase 28: multi-currency (Currency list, rate on 12 types, fold on posting-rule inputs, realised forex on allocation). Before converting anything into the GL or gating a feature flag — `docs/phase-28-status.md`
- Phase 29: landed cost (Additional Cost on the Purchase Bill, capitalised into FIFO layers, Landed Cost Clearing account). Before capitalising into a stock layer or adding a tenant-default GL account — `docs/phase-29-status.md`
- Phase 30: Communications (Send Email on 6 types + Contact, Email Logs, Email Templates, `AlertMedium.Sms`). Before wiring Send Email, or a job that reads through a permission-gated request — `docs/phase-30-status.md`
- Phase 31: credit control, Configurations > General, three dead settings enforced, stored `DueDate`, cheque bounce. Before enforcing a tenant setting, or a NOT NULL column on a populated table — `docs/phase-31-status.md`
- Phase 32: Billing Locations (HeadOffice seeded, cap at one, `LocationId` on 17 types, location-wise numbering). Before letting a setting choose which types store a field — `docs/phase-32-status.md`
- Phase 32b: per-location permission scope (nullable `RolePermission.LocationId`, one branch in `AuthorizationBehavior`). Before a permission depending on an unread row — `docs/phase-32b-status.md`
- Phase 33: platform chrome (global search, History, Quick Links, the `UserPreference` per-user store). Before a per-user setting or a cross-type search — `docs/phase-33-status.md`
- Phase 34a: WCAG 2.1 AA sweep + `a11y-sweep-guard.spec.ts`. Before adding a template, choosing a colour, or scripting an edit between two anchors — `docs/phase-34a-status.md`
- Phase 34b: the shell on `NavigationCatalog` (zero page-template edits), Reports index, list chrome. Before a paginated list query, or a displayed-but-unowned filter — `docs/phase-34b-status.md`
- Phase 34c: scale — the 50k-invoice dataset in `tools/scale/`, p95 budgets per screen class, `TenantIndexConvention`, the shell `@defer`ed. Before adding an index or quoting a performance number — `docs/phase-34c-status.md`
- Phase 35a: ledger drill-down + the location picker/filter on all 15 document forms and lists. Before adding a field to many aggregates at once — `docs/phase-35a-status.md`
- Phase 35b: the location dimension in reports (`LocationId` stamped on the fact tables, the filter on 36 queries). Before filtering an append-only fact table or proving a location permission — `docs/phase-35b-status.md`
- Phase 36: allocation/forex/ageing consistency, `OutstandingDocumentReader`, server-side due dates, product-to-location on the picker. Before assuming a document has one GL entry, or folding a settlement to base — `docs/phase-36-status.md`
- Phase 37: inventory policy — shortfall layers, the cost catch-up, returns at consumed FIFO cost, the clearing unwind. Before letting stock go negative or deciding what a return credits — `docs/phase-37-status.md`
- Phase 38: import/export breadth — 5 upload types, tree ordering, a pre-commit dry run, export by category + date range. Before adding an importer or an array parameter to a POST — `docs/phase-38-status.md`
- Phase 39: the sanitised rich-text control, the Organization logo, standalone Deals/Tasks routes, the search results page. Before storing anything a user typed that is rendered later — `docs/phase-39-status.md`
- Phase 40: the human WCAG pass (keyboard census, one focus ring, always-present live regions) + list-chrome leftovers. Before claiming an a11y criterion or writing a status message — `docs/phase-40-status.md`
- Phase 41: subscription and plan model — the seeded `SubscriptionPlan` catalogue, `SubscriptionQuotaBehavior`, the shell banner. Before calling a tenant-level limit "enforcement" — `docs/phase-41-status.md`
- Phase 42: performance follow-through (`ToKeyPagedResultAsync` on 16 lists, Detail GL paged by row, the bundle budget). Before paging a list, handing SQL a list of ids, or quoting a bundle size — `docs/phase-42-status.md`
- Phase 43: aggregate completions (`UpdateOrganizationCommand`, Deal/WorkTask as record parents, `DebitNote.WarehouseId`). Before two column renames in one migration, or changing what a document does to stock — `docs/phase-43-status.md`
- Phase 44: report semantics read live first (four registers folded to base, Reporting Tags as OR-within/AND-across, System Audit's location, the backfill). Before assuming a report applies the filter it accepts — `docs/phase-44-status.md`
- Phase 45: multi-UOM × variants (a variant owns its unit matrix; a parent is refused one), the `ProductAttributePool` importer. Before deciding an interaction nobody has posed, or nesting a control in a row anchor — `docs/phase-45-status.md`
- Phase 46: metered add-on axes — the AI-scan ceiling (20/day, from the audit trail), the allowance year, locations as a record not a ceiling. Before adding a metered axis — `docs/phase-46-status.md`
- Phase 47: accessibility completion (Sort by on 16 document lists, the nested control removed, per-field errors on 13 forms, the drop list). The NVDA hour is undone. Before sweeping "the N screens that qualify" — `docs/phase-47-status.md`
- Phase 48: shared display components + record pages (every date *output* through `NepaliDatePipe`, instant-aware; `app-deal-form`/`app-task-form` for create and edit). Before rendering a timestamp, or trusting a carried item's own count — `docs/phase-48-status.md`
- Phase 49: the expiry decision (we keep an expired tenant and mark it, where the reference product deletes it from the list), `IsActive`, Nepal-anchored term ends, SMS on the Subscription screen. Before copying a behaviour read live, or diverging from one — `docs/phase-49-status.md`
- Phase 50: measured indexes (`Cheque`'s date index + key-paging), the convention's declaration mechanism, `tests/Infrastructure.UnitTests`. Before adding an index, or writing a cross-assembly invariant test — `docs/phase-50-status.md`
- Phase 51: batch and serial tracking — both are keys on the FIFO layer, neither carries a quantity. Before adding a dimension to the stock ledger, or sweeping a change through optional parameters — `docs/phase-51-status.md`

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
- Two column renames on one table in one diff are paired by **ordinal position**, not by meaning, so `dotnet ef` silently swaps them; verify with a predicate true only of the right pairing (phase-43 bug #1).
- Rebuilding only `Infrastructure` between `dotnet ef` calls leaves `Api`'s output stale and re-scaffolds an applied diff; let `dotnet ef` rebuild (omit `--no-build`) or rebuild the solution (phase-16a).
- An enum property with `.HasDefaultValue(x)` where `x != default(TEnum)` needs `.ValueGeneratedNever()`, or EF substitutes the SQL default whenever the in-memory value equals `default` (phase-2 bug #2).
- `Database.SqlQuery<T>` accepts only composable SQL (a plain `SELECT`); do atomic read-and-increment as `SELECT ... WITH (UPDLOCK, ROWLOCK)` + a separate `UPDATE`, and alias a scalar result `AS Value` (phase-2 bugs #3–4).
- A handler generic over an interface-constrained type must read properties via `EF.Property<T>(x, nameof(...))`, be registered against the closed 2-arg `IRequestHandler<,>`, and never pass a captured `Func` selector into `Where` (phase-2 bugs #1/#5, phase-9 bug #1).
- Replacing an entire encapsulated child collection in one save mis-tracks on InMemory; snapshot and `RemoveRange`/`AddRange` through the child `DbSet` (phase-4 bug #1).
- A child appended to an already-tracked parent's encapsulated collection is detected as `Modified`, not `Added` — same remedy, have the Domain method report the change and `AddRange` it (phase-24 bug #1).
- `TestAppDbContext` has no `ApplyConfigurationsFromAssembly`, so every encapsulated collection must be restated there with `HasMany...SetPropertyAccessMode(Field)`; its symptom is the identical `DbUpdateConcurrencyException`, so check the test context first.
- SQL Server treats NULLs as equal in a unique index; a unique index over a nullable column needs `.HasFilter("[Col] IS NOT NULL")`, and InMemory enforces neither half.
- `EF.Functions.Like` cannot be translated by InMemory; write `String.Contains`, which SQL Server turns into the same `LIKE`.
- Retire copies 1..N before writing copy N+1 of a pattern: `GrantedPermissionReader` left two inlined joins behind and both missed the `LocationId == null` filter (phase-33).
- A shared helper replacing a per-handler `Where` must own **every** condition that `Where` carried, not just the interesting one — there is no global query filter here, so nine handlers rewritten is nine chances to drop `OrganizationId`; take the organization as an argument (phase-35b).
- An append-only fact row (`GlJournalEntry`, `StockMovement`, `StockLedgerEntry`) carries only its source ids; filtering on a document attribute needs a stamped column, and reversals inherit it (phase-35b).
- An expression tree does not short-circuit, so `!flag || list.Contains(x)` hands EF a **null** list to translate — and only on the unrestricted branch, i.e. almost every caller. Compose a second `.Where()` (phase-33).
- …but that generalises only to a captured **bool**: `collection == null || collection.Contains(x)` is funcletized to a constant and folded away, and returns 200 on SQL Server. Compose anyway; don't call an instance of it broken without running it (phase-35a, correcting phase-33).
- A shared matcher cannot live inside a LINQ predicate: a **static call** is untranslatable and so is `Contains(term, StringComparison)` — and InMemory evaluates both in C#, so every handler test passes while all 25 endpoints 500 (phase-34b, phase-25's captured-`Func` through another door).
- Same door, third time: a store-side `Sum` over an already-projected record is untranslatable, InMemory evaluates it, and only an E2E sees the 500. Project after `Skip`/`Take` (phase-42).
- Single-argument `Contains` is case-**insensitive** on SQL Server (collation) and case-**sensitive** on InMemory; a handler test must search with the stored casing or it pins a behaviour production lacks (phase-34b).
- Every tenant-scoped table needs an index leading on `OrganizationId`; `TenantIndexConvention` derives them and throws at model build for an entity it cannot classify (phase-34c).
- …but it recognises a business date by **name** (`Date`, `PostedAt`), so `Cheque.ChequeDate` is invisible to it and the Cheque Register has always ordered by an unindexed column (phase-47).
- Asking the mirror question found **three** such dates, and the two stock tables were right only by luck of a hand-written composite; not classifying now fails the model build (phase-50).
- An index added for one path changes the plan for every other path on the table (list 10× faster, no-match search 1.8× slower); re-measure the paths you did not touch (phase-34c).
- …including a change *you* reasoned into: tenant predicates on a join's other three tables fixed the search path and took a sibling tab from 2,143 logical reads to 83,308 (phase-50).
- A list that searches a **joined** column (only `ListChequesQuery` does) cannot be indexed out of it — the OR spans two tables, so the row is formed before the term is evaluated; a covering index bought 0.03% (phase-50).
- A materialised id list handed back to SQL becomes an `OPENJSON` parameter as long as the list; a report that loads its period then re-queries children by `ids.Contains` is linear in the period, not in the page. `JournalReportQueryHandler` is the shape that is not (phase-34c).
- …and the converse: a list long enough to be worth avoiding is too long to send. Narrowing by 35,001 ids cost 182,545 reads against ~1,500 for the scan; join the *query*, or drop it (phase-42).
- A page's rows are fetched before they are eliminated: an offset of 49,950 costs 153,705 logical reads for whole entities and 571 for ids. Count, page the keys, fetch those rows (`ToKeyPagedResultAsync`, phase-42).
- A count of zero is a complete answer — never issue the page query after it; that one branch, not any knowledge of searching, is what fixes a search term matching nothing (phase-42).
- A ledger report's cost is the history *before* its period: the opening balance aggregates all of it, so the Detail General Ledger is now slower over one month than over three years (phase-42).
- A list may offer an ordering only where an index leads on `(OrganizationId, <column>)`; an unknown sort value is a 400 naming the field, never a silent default (phase-40).
- On a bulk-`INSERT`-seeded database, statistics quality moves a report that joins to a line table by 2× — more than most changes under test. `UPDATE STATISTICS ... WITH FULLSCAN` on both sides before comparing anything (phase-34c).
- Read a handler's `Where` before assuming it matches its request — `ListPaymentsQueryHandler` shipped with a hardcoded `Direction == Received` (phase-6 bug #2).
- EF refuses a set operation *after a client projection*, so `Concat`-ing two `select new SomeRecord(...)` queries throws at run time on SQL Server as well as InMemory; concatenate while both halves are still anonymous and build the record from the materialised page (phase-38).
- A store-side aggregate (`GroupBy...Count()`) must run after the `SaveChangesAsync` that persists what it counts; tracked-but-unsaved rows are invisible to it (phase-21a).
- A Domain factory/mutator can stay `internal` only while its sole caller is in the Domain assembly (phase-7 bug #1).
- Never name a Domain type after a common BCL word (`Task` → `WorkTask`) (phase-13).
- `stopPropagation()` on a control inside an `<a routerLink>` *causes* navigation — it suppresses the listener that would have called `preventDefault()`. Click needs both; mousedown needs neither (phase-45).
- Guard the add and the edit, never the **delete**: a product given secondary units and promoted to a variant parent afterwards would otherwise hold rows that are invisible and unremovable (phase-45).
- A sweep-guard allow-list reason can be the argument for the opposite conclusion: "a secondary unit on a variant parent reconciles against nothing" is why a parent must be *refused* one (phase-45).
- A **record** parent (Contact, Deal, WorkTask) is a `DocumentType` member that is *not* transactional; that one property is what routes it to its own keys with no special-casing (phase-43).
- A filter over a tree of tenant-defined groups must match on group **id**, never group name — names are not unique across a chart of accounts (phase-26a bug #1).
- `decimal` has a signed zero: `-0m` keeps its sign bit and surfaces as `-0` / `-0.00` once cast to `double` for a spreadsheet cell. Accumulate a magnitude only when the value is strictly non-zero — no test catches this, because `-0m == 0m` (phase-26c bug #1).

**GL posting, documents and domain invariants**
- A dimension that is a `GROUP BY` over the one quantity cannot drift from it; a dimension with its own quantity column is phase 37's two-of-three-views failure waiting to happen. `ProductBatch` stores no quantity (phase-51).
- A serial is a FIFO layer of **quantity one**, so specific identification and FIFO are the same walk with a different selector — and the report's *Status* filter is `QuantityRemaining`, not a modelled lifecycle (phase-51).
- A serialised issue is refused whatever the Negative Item Balance setting says: naming a unit that was never received is a typo, not an oversell (phase-51).
- A shortfall carries **whatever key the request named** — the batch when one was named and came up short, none when the walk was unnarrowed; and a receipt fills same-batch debts first, then un-batched ones, or an un-batched debt on a tracked product is unrepayable forever (phase-51).
- A "reverse of X" posting rule can balance its own entry while leaving a paired control account (AP net of TDS) permanently off; trace the net effect on every account across original + reversal (phase-6 bug #3).
- Reversals mirror the original entry's own posted lines via `GlJournalEntry.PostReversalOf` (a second entry, never a mutation); never re-derive a reversal from the posting rule (phase-16a).
- "One GL entry per Approved document" is a habit, not an invariant; reverse the outstanding net of every entry via `SourceDocumentGlEntries`, never `SingleAsync` (phase-36).
- A settlement folds to base at the rate of **what it settles** (each allocation at its target's rate, the remainder at its own), or a fully settled invoice keeps a residual balance equal to the realised forex (phase-36).
- `ReferrerType`/`ReferrerId` enforce nothing — a conversion needs `MarkConverted`, quantity/rate caps net of prior reversals, and contact/TDS consistency checks in the Create handler (phase-6 bug #4).
- Goods purchases debit `DefaultInventoryAccountId` (post-Phase-19 fix); a live inventory value still comes from `StockLedgerEntry.QuantityRemaining × UnitCost`, not that GL balance (phase-19 bug #1, phase-7 addendum).
- GL-report tests must bracket `DateOnly.FromDateTime(DateTime.UtcNow)`, because `PostedAt` is stamped at Approve time, not from the document date (phase-19 bug #2).
- Anything scheduled or dated for a tenant uses the Nepal wall clock via `Domain/Common/NepalTime` (fixed UTC+05:45, not `TimeZoneInfo`); test an after-local-midnight case, not just an evening-UTC one (phase-20e).
- A FIFO layer stores a unit cost rounded to `ProductionJournal.UnitCostScale`; build a value-transforming GL entry from the values actually created and name the rounding residue (phase-25).
- `GlJournalEntry` stores no copy of its document's number, reference or business date — only `SourceDocumentType`/`SourceDocumentId`/`PostedAt`; any report showing those must join back across the 11 GL-posting types, and must show the same date field it filters on (phase-26a).
- A dated stock report derives from `StockMovement`, never `StockLedgerEntry`, whose `QuantityRemaining` is decremented in place and only answers "as of now" (phase-26c).
- Same rule outside stock: a per-day count comes from the append-only `Audit` rows, never `UploadedDocument.ExtractionAttemptedAt`, which a re-scan overwrites — ten paid re-runs would count as one (phase-46).
- An oversell leaves a shortfall layer (a negative `StockLedgerEntry` at the last known cost) when the Negative Item Balance verdict allows it; `ConsumeAsync` takes the verdict, never the setting (phase-37).
- A Debit Note line carries no `ExpenditureClassification` or `IsImport` of its own; both are resolved from the source Purchase Bill's matching line by (PurchaseBillId, ProductId, Rate, VatRate) (phase-19, phase-26c's `PurchaseReturnReader`).
- Convert a currency on a posting rule's **inputs**, never on its finished `GlLineInput` list: every rule derives its balancing leg as a *sum* of the others, so converting afterwards rounds that leg independently and breaks `sum(Debit)==sum(Credit)` intermittently (phase-28).
- Same in a report: fold a statutory register's **lines**, not its buckets, or Total stops equalling TaxExempt+Taxable+VAT; fold in the shared reader, or two registers disagree about one note (phase-43).
- Never convert twice: FIFO unit costs, COGS and historical `GlLine`s are already base currency. `ApprovePurchaseBillCommandHandler` is the one place a document rate reaches the stock ledger, and it rounds to 4 dp (`ToBaseUnitCost`), not 2 (phase-28).
- Changing what a posting rule debits changes what every reversal owes; phase 29's capitalised cost needed its own release leg on Debit Note (phase-29, phase-6 bug #3 again).
- Build a capitalisation leg from the value the ledger actually received, round each unit cost once at the ledger's scale, and name the residue (phase-29).
- When a phase adds a tenant-default GL account, grep `web/` for the field name before calling it done — phase 25's and phase 28's three accounts reached the API and no screen, so they could not be configured at all (phase-29; phase-23 bug #1 in reverse).
- A stock **value** correction must reach three views or two drift silently: the FIFO layers, the Inventory account, and the append-only movement history (`StockMovement.ValueAdjustment`, a row with zero quantity). Any two can be patched into agreement — assert all three (phase-37).
- A return relieves at the cost the layers give up (FIFO's choice), debits the supplier the return price, and books the difference as the balancing plug (phase-37).
- A catch-up leg raisable from many call sites is its own GL entry against the same source document, never an amount threaded through posting rules; `PostReversalOf` is gone (phase-37).
- Changing what a document does to the **stock** ledger changes what its Void owes: giving Approve a new warehouse source left Void restocking from the old one, so stock went out and never came back (phase-43).
- A standalone Debit Note credited the Inventory *account* while the FIFO ledger never moved; a standalone Credit Note posts no Inventory leg at all, so only one of the two was ever a divergence (phase-43).

- Rich text is sanitised on write, in the Domain setter, by re-emission from a parsed tree, never by filtering; `Sanitize` must stay idempotent (phase-39).
- A rich-text grammar is its **renderer's capability list**: decide what `RichTextPdfRenderer` can draw, then build the toolbar, or the field looks one way on screen and another in the PDF the customer receives (phase-39).
- A tenant-level field is reachable only if you can name the command that writes it and the screen that calls it; a read path proves nothing about the write path (phase-31).
- A field an aggregate refuses to expose should be **absent** from the request record, not merely unwritten — present-and-ignored reads to a client as accepted (phase-43's `WorkspaceName`).
- Before calling a tenant-level limit "enforcement", ask who can write it: `Tenancy.Subscription.Manage` sits on the tenant's own Admin, so a quota is a record and a guard, not a control (phase-41).
- A field dead on two free-trial tenants is one sample, not two; read what the vendor publishes (its price list sells the "dead" fields) before asking for another tenant (phase-41).
- …and the same about a **behaviour**: two trials are one sample of trial behaviour and zero of paid, so "the reference product deletes an expired tenant" never settles what ours does (phase-49).
- A divergence you *choose* owes a surface, not a doc paragraph: 17 organizations sat read-only with nothing saying so until the picker said it (phase-49).
- …and a fact recorded in the scan but not carried into the decision is not yet evidence: "AI scans / day: 20" sat in the price-list appendix for a phase while the roadmap called that axis an invention (phase-46).
- Zero-means-unmetered is right for an allowance somebody **bought** and exactly wrong for a **cost control**: on the scan axis it is a free trial with uncapped spend on a paid API, so the trial seeds the published ceiling (phase-46).
- A scaffolded migration default can be *plausible* and still mean the opposite of what is wanted — `DailyAiScanQuota DEFAULT 0` is "unmetered", so the feature would have applied to nobody with the API bill as the only evidence (phase-46).
- Locations are sold per unit and capped nowhere: the reference product runs three on an Enabled flag with an unbounded list and no charge shown, so a purchased count is a record, never a ceiling (phase-46, phase-43's precedent).
- Two confirmable warnings on one document need two override flags and a `warningKind` on the 422, or confirming the first waives the second (phase-31).
- A non-nullable column on a populated table needs a hand-written backfill unless the default is already true of the existing rows (`ValueAdjustment` needed none) (phase-31, phase-37).
- A permission that depends on the requested value as well as the loaded row is the `AttachmentAccess` pattern; the E2E must show 404 on a missing row and 403 on a real one (phase-31's cheque bounce).
- Never weaken a Domain invariant so a test can reach a state only time produces; reach through EF's change tracker (phase-31's expired `TenantSubscription`).
- When NULL in a unique-indexed column is an at-most-one sentinel, the unfiltered index is the enforcement and EF's automatic `IS NOT NULL` filter destroys it; `HasFilter(null)` is load-bearing (phase-32's numbering counter).
- When a tenant setting selects among sets of document types, the schema owes the widest set, or flipping the setting is a lie until later columns ship (phase-32's `LocationScopeMode`).
- Once missing one per-handler re-check is an open door, the check moves into `AuthorizationBehavior` (not a sixth behavior; a nested `ISender.Send` corrupts a scoped context) behind marker interfaces and a build-failing sweep guard (phase-32b).
- A confirm-live pass can falsify an earlier one: a recorded inference about a control nobody operated is not settled (phase-32b's `LocationWiseReportPermission` scopes report rows, not keys).
- Product-to-location filters the **picker** and nothing else: the reference product saves *and* approves a document naming an out-of-location product (phase-43, confirmed live; the enforcement idea is retired).
- Reuse a marker interface by reading it, not merging it, when its member set is narrower than the new grant's (`ILockDateSensitiveDocument`, phase-32b).

**Imports and exports**
- A bulk importer resolves a row into the command it *would* send (`PlanAsync` -> `ImportRowPlan`) and only then sends it, so the dry run and the real run share every line of the resolution; a plan that cannot be built yet is `Provisional` and throws if executed (phase-38).
- A pre-commit review needs no findings table: have the validate pass claim **only the rows it rejects** in the existing row ledger, and the apply pass skips them through the same mechanism that makes a crashed import resumable (phase-38).
- Intra-file parent ordering belongs to **self**-referencing types only — a column pointing at a *different* aggregate is not an edge inside the file. A cycle or a duplicate key fails the whole file with its rows named; an unknown parent is one row's error (phase-38).
- A dry run writes nothing, so a hierarchical importer's ordinary name lookup cannot see a parent a later row creates; pass the in-file key set down, or the review reports correct files as broken (phase-38's `ImportRowContext.PendingKeys`).
- An importer whose row *adds* to a set its command replaces wholesale re-reads and sends the union per row; the dry run plans each row from the same start, which is right — it checks rows, not totals (phase-45).
- A template generated **from the document in front of you** (the landed-cost grid: one column per tenant cost term, one row per bill line) is neither an `ImportTemplateDefinition` nor an `ImportJob` — nothing is written and there is nothing to resume (phase-38).

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
- Read an uploaded image's format and size from its bytes (`Domain/Common/ImageHeader`), never its content type; QuestPDF throws for an undecodable image after composition (phase-39).
- Executing `Results.Stream` against a bare `DefaultHttpContext` needs a `ServiceProvider` with `AddLogging()` (`MigratedRegisterTemplateRoundTripTests`).
- A `MultipartFormDataContent` under `using` in a helper that returns the `Task` unawaited is disposed mid-send (`ObjectDisposedException` from `TestHost`); await inside the helper (phase-22).
- ClosedXML returns empty text for hand-rolled `inlineStr` cells and ignores `<si>` past a stale `uniqueCount`; build import fixtures by filling the app's own generated template (phase-21a).
- `AdjustToContents()` measures every cell it is given — size columns over the header plus a sample of rows, state the row cap, and disclose truncation in the artifact (phase-21b).
- Import-template date columns need an explicit day-first-before-month-first format list, never bare `DateTime.TryParse`; assert the ambiguous case (`ImportRowReader.GetOptionalDate`, phase-21c).

- A trailing optional parameter added to a command reaches nothing until the Api's own request record carries it too — it compiles, every test passes, and the field binds silently to `null` (phase-27b's `Terms`).
- A Minimal API binds an **array** parameter from the *body* on a POST, so a repeated query string arrives `null` and a "choose what to export" feature silently exports everything; `[FromQuery]` is load-bearing, and the simple types beside it bind without help, which is what hides it (phase-38).
- A list query returning the aggregate exposes a new field for free, but a detail DTO drops it silently and the form can never show it (phase-32's `GetInvoiceQuery`).
- Adding a field to many aggregates owes three assertions, write, read and every prefill between: 14 of 15 detail DTOs and all 5 conversion templates dropped `LocationId` (phase-35a).

**Report filters and live-read semantics**
- A sweep guard over *query records* cannot see whether a **handler** applies the filter it accepts; both statutory registers narrowed only their return half for three phases. Pin it behaviourally, per report (phase-44).
- A sweep whose set is a screen **shape** is silently blind to every other shape: the Cheque Register escaped 34c's indexes, 42's key-paging and 47's sort menu, one screen missed three times (phase-50).
- Reporting Tags are **OR within a category, AND across categories** — measured on a six-category tenant; phases 19/36's "any-of across everything" was inherited, never observed (phase-44).
- A control recorded as "unbuilt because two options could not be told apart" may just be a **parent and its modifier**: Display Warehouse in Column is `disabled` until Group by Warehouse is ticked (phase-44).
- A reason to exclude something can be right while the conclusion is wrong: 26c predicted Warehouse Transfer's two-rows-with-blank-money shape exactly, and the reference product ships it (phase-44).
- A statutory register folds its **lines**, in the **shared reader**, **in memory after `ToListAsync`** — the last because `ToBase` is a static call SQL Server cannot translate (phase-43/44).
- `SetLocation` is draft-only by design, so backfilling approved history needs its own narrower mutator (`BackfillLocation`: fills a null, refuses to move one) (phase-44).
- A stamped audit column records where a document **was when the action happened**; do not backfill it when you backfill the documents (phase-44).

**Angular**
- A component serving both `.../new` and `.../:id` must read the id from `route.paramMap` (an Observable) and re-derive "is new" on every emission (phase-3 bug #1).
- Annotate `HttpClient.get` `params` as `Record<string, string>`; a union including `{}` silently resolves to the `arraybuffer` overload (phase-3 bug #4).
- Don't share a `request$` variable across Create/Update when the result types differ; use an explicit `if/else` (phase-4 bug #3).
- Never bind `[value]` on a signal-fed native `<select>`; bind `[selected]` per `<option>` — this persisted wrong `WarehouseId`s, not just display glitches (phase-5/6/7).
- Footer totals on paginated screens come from a server-computed field over the full filtered set, never a client-side reduce over one page (phase-16c bug #1).
- The app is zoneless: a `computed()` over a plain `FormControl.value` caches forever; track UI-driving values in their own `signal()` written by the control's event handler (phase-17).
- A cached signal created lazily inside a `computed()` throws `NG0600` the moment its source resolves **synchronously** (a test double); real HTTP hides it. `untracked()` the subscribe and the writes (phase-35a's `BillingLocationStore`).
- Bootstrap's JavaScript is not loaded anywhere (`angular.json` has no `scripts`), so `data-bs-toggle` does nothing; drive menus from a signal (phase-22).
- Angular's `DatePipe` ignores `DatePreferenceService` entirely, so `| date:` renders Gregorian whatever the BS toggle says; phase 23's guard banned date *inputs* and nobody asked the mirror question about date **output** (phase-48).
- `NepaliDatePipe` takes an instant's **Nepal** day, never `slice(0, 10)` of its UTC form — between 18:15 and 24:00 UTC that names yesterday, which the Transaction List had been doing since phase 26a (phase-48).
- One calendar gets one pipe: a time is a **mode** on `NepaliDatePipe` (`'datetime'`, `'datetime-seconds'`) rendered from the same shifted instant, never a second pipe (phase-48).
- A day written `T23:59:59Z` and read back by `slice(0, 10)` agrees with itself and with nothing else; anchor both halves to `+05:45`, and move them together or the date ratchets forward per save (phase-49).
- A field error must name a **control**: `aria-invalid` is not valid on a `table`, so a line-table message points at the **Add Line button** — the thing that fixes it, and where focus should land (phase-48).
- The app routes by **path**, not by hash — the reference product's own URLs are hash-based, so a browser pass against `#/organizations/…` bounces to Sign In with the cookie working perfectly; check `location.href` before suspecting the cookie (phase-39).
- A `.dropdown-menu` inside `.table-responsive` is clipped by the implied `overflow-y`; render it `position: fixed` at coordinates captured on open (phase-22).
- A pipe rendering from a global signal with an unchanging argument must be `pure: false` and memoize internally (`NepaliDatePipe`, phase-23).
- `Domain/Common/BsCalendar` is a verbatim port of the client `bs-date.ts` table; port it, never retype it, and keep both boundaries pinned — a fiscal year runs Shrawan 1 to the last day of Asar and is named by its first BS year (phase-26b).
- A pipe inside parentheses parses anywhere, including a ternary branch, but a bare pipe in a ternary branch does not; pipes stay illegal in event bindings (phase-23).
- A component test asserting an uppercase label fails when the casing comes from CSS `text-uppercase`; assert the source casing (phase-23).
- When a phase starts populating a previously-dead DTO field, grep the templates that consume it — `SalesRegisterQuery`'s export columns were filled and invisible (phase-23 bug #1).
- `<iframe [src]>` needs `DomSanitizer.bypassSecurityTrustResourceUrl` (safe only because the URL is API base + route GUID), while `<img [src]>` with the same string is fine (phase-22).
- `AmountPipe` renders two decimals by default; pass `| amount: 4` for figures legitimately smaller than a cent (phase-25).
- A sweep over `<input>/<select>/<textarea>` cannot see a control a **component** wraps: 121 date fields had a label, an input and nothing joining them. Ask the mirror question — which labels name no control? (phase-34a).
- Every date a user types goes through `app-bs-date-input`, never a native `<input type="date">`; `sweep-guard.spec.ts` enforces it and will catch a filter added without thinking about BS dates (phase-23, still biting in phase-38).
- Bootstrap's brand tones clear WCAG's 4.5:1 against **pure white** and only just, so they fail on this app's `#f8f9fa` body; the four text utilities are re-pointed at its `-600` shades in `styles.scss`, and `contrast-rules.ts` is the measured palette the guard derives from (phase-34a).
- Bootstrap's JS is not loaded, so nothing sets `aria-expanded` for free — every signal-driven popup must set it itself (phase-34a, extending phase-22's gotcha).
- A filter a screen **displays but did not apply** is worse than no filter: anything global a screen both shows and sends must reload that screen when it changes, from the first version (phase-34b).
- State two surfaces show needs a shared store when one of them changes; the subscription banner kept "366 days remaining" after a paid plan was saved (phase-41).
- A swept-in filter handler copies its sibling handlers on that page, including `page.set(1)`, or a narrowing filter on page 3 reads as "no data" (phase-35b).
- An `effect()` cannot tell "the write already being acted on" from "a write needing action" — if the handler that wrote the signal already schedules the response, an effect over it is a race (phase-34b).
- Never put `overflow` on a layout container without asking what is anchored inside it; the rail's `overflow: hidden` clipped a 46rem flyout to 240px, silently (phase-34b, phase-22's gotcha in a second container).
- Deriving beats listing for a **catalogue** (a missing nav entry is an unreachable screen); listing beats deriving for a **curated tray** (deriving loses the curation that is the feature). Guard both the same way — every url must resolve to a real route (phase-34b).
- A live region announces on content change, so a region created already holding its text says nothing; render `app-status-banner` unconditionally (phase-40).
- `role="alert"` on unconditional page furniture is the reverse error: it fires on page load and interrupts whatever is being read (phase-40).
- Bootstrap's `:focus-visible` ring measures 1.21–2.53:1 against the 3:1 rule; one opaque `#0a58ca` outline at `outline-offset: 2px` is right for every variant (phase-40).
- A scan for controls cannot see a click handler with no control: the organization picker's rows were `<div (click)>`, so a keyboard user could sign in and reach none of the other 140 screens. The check for this class is a **focusable census**, not a scan (phase-40).
- A control that hides itself owns its own label, or its caption outlives it — `ListChrome` named a location filter that renders nothing on a single-location tenant, on 22 screens, invisibly to any source-level guard (phase-40).
- An ARIA grouping with no `aria-label` is worse than no role: it announces "group" and adds a boundary carrying nothing. `role="group"` over native radios should be `radiogroup` — the browser is already doing the roving selection (phase-40).
- A backtick inside a comment inside an inline `template:` literal terminates the template; the compiler blames the `@Component` decorator (phase-40).
- A `computed()` records only the signals it actually **read**, so a `&&` short-circuiting past the signal on the first evaluation leaves it dependency-free and frozen; every test that calls the setter before reading passes (phase-47).
- A control nested in an `<a>` is invalid HTML no handler repairs: row a `<div>`, link a `stretched-link`, controls siblings at `position-relative z-2` (the overlay is z-index 1), and the ring moves to the row via `:has()` (phase-47).

**Multi-way switches on a document-attached mechanism**
- A shared UI panel is not evidence of a shared model: email templates are their own resource, so `EmailTemplate` is its own aggregate and `CustomTemplateType.Email` was deleted (phase-30).
- A list sampled from a few screens becomes a wrong list; find the **rule**. Send Email is not on all 15 printable types but on the 6 whose email-template context exists — a rule that also settles the types never probed, asserted in both directions by a guard test (phase-30, correcting phase-27b).
- When the real permission key depends on the row about to be loaded, declare a blanket seeded key for `AuthorizationBehavior`, then re-check the real key in the handler with the identical `ForbiddenException` (phase-27a, `AttachmentAccess`).
- Never bridge overlapping enums (`DocumentType` and the parent-type enums) by ordinal cast; bridge by name with `Enum.TryParse` and pin it with a divergent-ordinal guard test (phase-27a).
- …and two lists describing one thing in two vocabularies cannot be joined by display name at all: the plan tick-list speaks the price list and a tenant's features speak the signup wizard, so the join matches nothing and renders as agreement. Compare where both sides are typed (phase-46).
- A guard predicate naming a **dependency** is not naming the behaviour: two handlers inject `IDocumentExtractor` only to read `IsConfigured`/`ModelId`, so the exclusions must be named with reasons and asserted to still exist (phase-46).

**Testing and manual E2E**
- An E2E that ends by moving its own user onto a restricted role leaves the browser pass looking at a broken app — lookups 403 and unrelated fields render as em dashes; restore the role, or prove the 403 last against a throwaway user (phase-51).
- A sweep driven by the compiler stops exactly where the compiler stops: changing `ConsumeAsync`'s **return type** enumerated all five consume sites, while adding **optional parameters** to `IncrementAsync` enumerated none — and the one increment site that needed them shipped un-swept past every green test (phase-51).
- A vendor's always-pass dummy credential (Turnstile `1x000…AA`) accepts any input; proving the negative path needs the always-fail one (`2x000…AA`) swapped in (phase-20g).
- A 403 proves the key only beside a 200 from the same user on the same organization in the same run; log in before `accept-invitation` or the membership stays `Invited` (phase-41).
- Two implementations of one rule in two languages are pinned to a shared table both suites read (`rich-text-cases.json`, the `bs-date` table), never to each other (phase-39, phase-26b).
- …and the half with a database runs against **SQL Server**, not InMemory: `search-cases.json`'s subject is case-insensitivity, which is the collation's and not the expression's (phase-47).
- A source-scanning guard cannot tell a comment from markup — five templates failed the nesting check on their own explanatory comments. Strip comments once, and assert both that they are gone and that nothing else is (phase-47).
- Derive the N in "sweep the N screens that qualify": the roadmap's 18 document lists were 16 screens over 15 queries, and the single exemption was an index gap nobody had noticed (phase-47).
- A guard whose predicate names a **type** silently stops covering anything solved before that type existed: `SearchSweepGuardTests` recognised only `PagedResult<T>`, so the two list queries that predate it were invisible — and were exactly the two the phase had to fix (phase-39).
- …and a predicate naming a **file extension** does the same: `a11y-sweep-guard`'s glob missed five inline-`template:` components for six phases. Widening it found nothing wrong, which is the honest result and not the same as never having looked (phase-40).
- A guard that accepts two spellings on one side must accept both on the other: it recognised `[for]` and `[attr.for]` on a label but only `[id]` on a control, so a control naming itself `[attr.id]` read as unnamed (phase-40).
- "Is this screen a list or a report" resisted for two phases because the premise was false — this codebase has three list shapes, and 34b's two undecidable screens were one report and one configuration lookup (phase-40).
- A Domain invariant reached through an endpoint is a **500**, which tells a caller nothing; add the same rule to the validator for a 400 that names the field and keep the Domain check as the backstop. Only the E2E sees this — every handler test constructs a valid command (phase-39).
- `curl -F name=<value` reads the value as a **file path** when it starts with `<`, reporting exit 26 and HTTP 000 — phase-30's symptom, a different cause. Rich text always starts with `<p>`; use `--form-string` (phase-39).
- **Endpoint and `sqlcmd` shapes an E2E needs — request field names, enum members, route quirks, seeding order — are a lookup table in `docs/e2e-recipes.md`.** Read it before writing a seed script; a wrong field name is usually a 400 naming no field.
- `UpdateRolePermissionsCommand.Grants` is a dictionary, not a list, and system Admin/Member roles cannot be edited (409) — a negative-permission proof needs a custom role.
- A browser pass in a non-interactive session works by exporting the ASP.NET dev cert, starting the `erp-web-ssl` profile, and transplanting curl's `erp_auth` cookie via `document.cookie` (phase-25 Step 3).
- A test suite that passes with **fewer** tests than the previous run is a failure — check what a rewriting script produced by counting it, not by whether the build is green (phase-27b).
- A guard must assert its input is **non-empty**, not merely defined: Vite returns an empty string for `?raw`/`?inline` on a compiled `.scss`, so `toBeDefined()` passed and every assertion over it was vacuous (phase-34a).
- Prove a guard bites by injecting a regression — but back the file up and restore it **by hash**, never `git checkout --`, which reverts the phase's own work on that file too (phase-34a).
- …and `touch` it afterwards: a restored backup carries an *older* mtime, MSBuild skips the rebuild, and the suite keeps failing on an injected regression the source no longer contains (phase-50).
- A guard asserting a rule's concrete *consequences* is not a guard on the rule: the ListSort test checked two indexes exist and passed the injected third ordering. Drive it from the thing that can change (phase-50).
- A cross-assembly invariant decidable from types and model metadata belongs in a unit-test project referencing both, never `Api.IntegrationTests` — a guard inside a Docker-gated failure is not read (phase-50).
- Wall time on a working machine cannot settle one screen's plan (two identical passes moved a report 4×); read logical reads and CPU from `sys.dm_exec_query_stats` (`tools/scale/probe-cheque-io.sh`, phase-50).
- A uniform sweep is worth more than its subject: asking one question of every paginated list found two queries with **no validator at all** and one whose search term had reached a `LIKE` uncapped since phase 25 (phase-34b).
- A bash helper that both **prints and returns** is a trap under `$( )` — it returns the printed line too; fourteen malformed ids became a `PUT` storing nulls that surfaced as a 409 three steps later. Have it set a global (phase-34b, same family as a function whose assignment a subshell discards).
- Map one enum onto another **by name** (`Enum.TryParse`), never by ordinal, and add a test asserting every member has a counterpart — an ordinal cast compiles, works today, and silently reports the wrong value the first time a member is inserted (phase-26a).
- A shared reader that several reports agree through is worth more than each report deriving its own figure: Invoice Age's total balance equals Customer Receivable Summary's closing balance *by construction* because both read `ContactLedgerReader` (phase-26b).
- Two reports agree only through one shared reader plus a test reading both on the same data (`OutstandingDocumentReader`); patching divergences leaves coincidence (phase-36).
- Driving the reference product's Browser pane needs coordinates from `getBoundingClientRect`: its accessibility tree is nearly empty and `find` matches nothing. Its GENERATE control's DOM text is "Generate" — the capitals are CSS `text-transform` (phase-26c).
- `tsc --noEmit -p tsconfig.json` does not typecheck `web/src/app`; it came back clean while `ng build` reported 22 `TS2339` errors. `ng build` is the real check (phase-28).
- Run `ng test` from `web/`, never as `npx --prefix web ng test` from the root: `a11y-sweep-guard.spec.ts` reads `src/styles.scss` via `process.cwd()` and fails for the wrong reason (phase-35a).
- A `Reports.*` key cannot be granted per location; a report's location scope comes from the caller's transaction grants via `AnyGrantedLocationsAsync` (phase-35b).
- The Angular suite times out nondeterministically under machine load — always the *first* test in a file, always at 5000 ms, in files the change never touched. Re-run before believing it (phase-36); `Api.IntegrationTests` does the same under Testcontainers (phase-37).
- A Goods line consumes stock regardless of `TrackInventory`, so on a tenant without that feature a Goods product cannot be invoiced at all (403 on opening stock, 409 on approve); seed a **Service** line when an E2E just needs an approved sales document (phase-30).
- curl cannot read a file for `-F` upload here — every path form gives exit 26 and HTTP `000`, which reads like a server fault; drive the file leg from a short Python `urllib` script (phase-30).
- A scripted multi-file edit must assert its anchor count before writing and preserve each file's CRLF/BOM (phase-32).
- `dotnet run --project src/Api` with no `--launch-profile` binds **5155 only**, not the 7104 the Angular dev environment calls; and a stale listener on 5155 makes the https profile fail to start (phase-30).

**Tooling and shell**
- `nvm use` from a shell that cannot create the symlink deletes `C:\nvm4w\nodejs` and reports success; recreate it with `cmd /c 'mklink /J "C:\nvm4w\nodejs" "%LOCALAPPDATA%\nvm\v24.11.0"'`.
- A `cat > file <<'EOF'` heredoc in the Bash tool is silently truncated or mis-parsed well below the ~8 KB figure; use the Write tool, or write a small patch script and run it (phase-26a). It also **eats backslash escapes** even when the delimiter is quoted, so a `
` or `	` inside an embedded script arrives as a literal newline or tab — a syntax error if you are lucky and a corrupted path if you are not (phase-39).
- A script inserting an import after "the last `
import ` line" lands *inside* a multi-line `import { … }` block; anchor on the statement's closing line (phase-35a).
- A scripted insert *before* a method lands between it and its doc comment; the unit to anchor on is the comment plus the declaration (phase-47, phase-35a's rule in mirror).
- One sweep can span two newline conventions — three of 42 files were LF in a CRLF repo; detect per file, and let the asserted anchor count abort rather than rewrite three files invisibly (phase-47).
- A lazy `.*?` between two anchors spans the instances between them; exclude the closing marker (`((?:(?!</label>).)*?)`) and derive the expected count a second way (phase-34a).
- A positional derivation (column index → header text) is *confidently wrong* where the position lies — a `colspan` cell, a cell with its own label. A wrong accessible name is worse than none; audit for the shapes the first pass cannot see (phase-34a).
- In the browser pane the **screenshot is ground truth**: after a viewport resize, `getComputedStyle`/`getBoundingClientRect` can lag the rendering (a drawer measured on-screen while the screenshot showed it tucked away). Reload after emulating a viewport (phase-34b).
- …and the same lag makes `getComputedStyle` inside a `focusin` handler report `outline: none` on every control, which reads as an app-wide 2.4.7 failure that does not exist. Measure after a real key event, and look at the picture (phase-40).
- A green `ng build` and a browser showing the pre-fix markup means the **dev server is serving a bundle older than the source** after a failed rebuild; the unchanged `_ngcontent-ng-cNNNNNN` attribute is the tell. Restart the preview (phase-40).
- `sed -i` also flips the CRLF of the file you *aimed* it at, after which every `
`-anchored patch script fails its assertion with the anchor looking correct; use Edit for a single substitution, or read the file's own newline (phase-33).
- A `sed -i` over a glob rewrites every matched file and flips CRLF to LF on Windows even where the pattern never fires; restrict the file list (phase-30).
- When a generator script emits Angular templates through `str.format`, interpolation braces need escaping in the *format string* but not in a substituted value — `{{{{ x }}}}` in a value ships literally and fails as NG5002 (phase-26b).
- A benchmark against an empty tenant looks fast (20 ms p95, all 200); a harness must assert its target is populated before timing it (phase-34c).

## Current status

**Phases 0–51 are complete.** The v1 sequence (0–25), parity (26–34c), consolidation (35–41),
completion (42–47) and the continuation phases (48–51) are all done; each phase's story is in its
`docs/phase-N-status.md`, and every finished planning entry is archived in `docs/roadmap-history.md`.

**Phase 51 added the first new *feature* since 47, and its modelling question had one answer.** A
batch and a serial are both **keys on the FIFO layer**, and neither carries a quantity of its own:
`ProductBatch` is an identity row with no `Quantity` column, so a batch's on-hand is a `GROUP BY`
over the layers carrying its id; a serial is a layer of **quantity one**, which makes specific
identification the ordinary FIFO walk with one more predicate, and makes the reports' *Status*
filter (`In Stock` / `Issued`) nothing but `QuantityRemaining`. Proven in SQL on a fresh
organization: `FifoLayers = InventoryAccount = MovementHistory = 2780.0000`, and
`Total 17 = SumOfBatches 16 + UnBatched 1`. The control is on the Invoice and Purchase Bill line
grids, which is where the 2026-09-16 read put it; everywhere else either derives the allocation
(Credit Note, Debit Note, Warehouse Transfer) or **refuses** a tracked product with a named 409
(Opening Stock, Inventory Adjustment, Production Journal — `StockTrackingRules.RefusedPaths`, each
with its reason and re-entry condition). **The two new reports' column sets were never read** — the
vendor gates them behind keys its demo Admin lacks, and this phase invoked the phase-8f rule
explicitly rather than silently.

**Next: phase 52 in `docs/roadmap.md`** — a unit on the document line, which is what would make
phase 45's secondary units mean something. Its first decision is what an approved document, a return
and a conversion do when the product's conversion rate is later edited (a stored factor versus a live
lookup, the same choice phase 37's cost catch-up made). Phase 51 left that dimension cheaper to add:
the four line types already carry an allocation, and `LineStockAllocator` is the one place a line's
worth of stock is moved. Three items sit outside the sequence with their own start conditions: **an
hour with NVDA** (needs a person with headphones), **full-text search** (a semantics change, not a
performance fix), and **the two traceability reports' real columns** (needs an account holding
`Reports.ProductBatch.View`'s vendor equivalent). The deferred and dropped lists are in the roadmap
and are unchanged.

Tests at last count: Domain **688**, Application.UnitTests **1212**, Infrastructure.UnitTests **12**,
Api.IntegrationTests 30, Angular **576**. `dotnet build` / `dotnet test` / `ng build` / `ng test` all
clean, and `ng build` does not warn — phase 42's measured 680 kB initial-bundle budget is pinned by
`build-budget.spec.ts`, and the bundle sits at **643.68 kB**. `Api.IntegrationTests` needs Docker
Desktop running: without it the Testcontainers-backed tests fail in their constructors with
`DockerEndpointAuthConfig` before any assertion, which reads like regressions and is not; it also
fails nondeterministically under machine load and passes on re-run (phase 36/37). `tsc --noEmit` does
not cover `web/src/app`; `ng build` is the check (phase-28), and `ng test` must be run from `web/`
(phase-35a) on **Node 24** (`nvm use 24.11.0` — v16 dies with `availableParallelism is not a
function`). When the subject is one screen's plan, the number to trust is `tools/scale/`, not the
wall clock (phase-50); phase 51 took **no** performance measurement and claims none, but it did add
an index to `StockLedgerEntry`, so a later phase touching stock performance should re-measure the
FIFO walk.

**Update rule for this section:** when a phase completes, add its one-liner to the Phase index above,
append its "read before X" paragraph to `docs/phase-lessons.md`, and replace this block with a
short orientation (what is done, what is next, test counts) — the phase's own story belongs in its
`docs/phase-N-status.md`, never here. Gotchas stay one line here (under ~220 characters); the
narrative goes in `docs/known-gotchas.md`, and endpoint shapes go in `docs/e2e-recipes.md`.
