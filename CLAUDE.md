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
- **Unconfirmed screen shape → confirm live before coding.** If `erp-module-scan.md` never opened the screen in its hands-on pass, read the live Tigg UAT tenant through the Browser pane first (the user logs in themselves — never enter credentials, never commit them). The Phase 8f Annex 5 lesson: the speculative design and the real screen had nothing in common.
- **Permission keys are derived per feature, not defaulted.** Flat per-transaction registers and anything exposing PAN/contact identity → Admin-only; bounded rollups and routine daily-use working data → Admin+Member. Record the reasoning in the status doc. New `PermissionKeys.cs` constants are auto-discovered by `PermissionKeyCatalog` (reflection), but the permission-seed migration must go through `RolePermissionConfiguration.HasData` first or the scaffold is silently empty.
- **Manual E2E bar:** seed master data via direct API calls (curl + cookie jar), reserve browser clicks for the phase's own new UI; prove at least one negative path (a 403 naming the exact key — against a nonexistent id, so 403-not-404 proves the behavior fired before the handler); verify persisted data via `sqlcmd` when a UI value could lie (see the select-race gotcha). A reusable Admin test login (email/password) persists across phases in local `dotnet user-secrets` under the `Testing:*` keys (never committed) — reuse that identity, but still create a **fresh Organization per phase** so seeded data doesn't accumulate across phases' baselines; run `dotnet user-secrets list --project src/Api` to see the key names (not values).
- **Context discipline:** one phase = one session (start from `docs/roadmap.md` and the relevant `docs/phase-N-status.md`; don't continue a finished phase's thread). Start each `phase-N-status.md` with a short TL;DR block so future sessions can read just the header unless the task needs a specific section; consult docs via targeted search (Grep/section reads), not full-file reads.

## Known gotchas (one line each — the full story for every entry is in `docs/known-gotchas.md`, under the same headings)

**Configuration, hosting, pipeline and auth**
- Never read configuration as a top-level statement in `Program.cs` before `.Build()` if the value is captured into a later-running closure; bind lazily (`AddOptions<T>().Bind(...)`) — user-secrets mask the bug locally (phase-1a #8).
- Every new `AddOptions<T>()...ValidateOnStart()` needs its test-only keys added to all four host-booting `Api.IntegrationTests` suites' `AddInMemoryCollection` in the same commit, or CI alone goes red (currently `Jwt`, `Email`, `Turnstile`).
- Don't reproduce that CI failure with `ASPNETCORE_ENVIRONMENT=Production` — it also flips `ThrowOnBadRequest`, breaking `ExceptionHandlingTests` spuriously; delete the key from one suite's in-memory collection instead.
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
- Read a handler's `Where` before assuming it matches its request — `ListPaymentsQueryHandler` shipped with a hardcoded `Direction == Received` (phase-6 bug #2).
- A store-side aggregate (`GroupBy...Count()`) must run after the `SaveChangesAsync` that persists what it counts; tracked-but-unsaved rows are invisible to it (phase-21a).
- A Domain factory/mutator can stay `internal` only while its sole caller is in the Domain assembly (phase-7 bug #1).
- Never name a Domain type after a common BCL word (`Task` → `WorkTask`) (phase-13).

**GL posting, documents and domain invariants**
- A "reverse of X" posting rule can balance its own entry while leaving a paired control account (AP net of TDS) permanently off; trace the net effect on every account across original + reversal (phase-6 bug #3).
- Reversals mirror the original entry's own posted lines via `GlJournalEntry.PostReversalOf` (a second entry, never a mutation); never re-derive a reversal from the posting rule (phase-16a).
- `ReferrerType`/`ReferrerId` enforce nothing — a conversion needs `MarkConverted`, quantity/rate caps net of prior reversals, and contact/TDS consistency checks in the Create handler (phase-6 bug #4).
- Goods purchases debit `DefaultInventoryAccountId` (post-Phase-19 fix); a live inventory value still comes from `StockLedgerEntry.QuantityRemaining × UnitCost`, not that GL balance (phase-19 bug #1, phase-7 addendum).
- GL-report tests must bracket `DateOnly.FromDateTime(DateTime.UtcNow)`, because `PostedAt` is stamped at Approve time, not from the document date (phase-19 bug #2).
- Anything scheduled or dated for a tenant uses the Nepal wall clock via `Domain/Common/NepalTime` (fixed UTC+05:45, not `TimeZoneInfo`); test an after-local-midnight case, not just an evening-UTC one (phase-20e).
- A FIFO layer stores a unit cost rounded to `ProductionJournal.UnitCostScale`; build a value-transforming GL entry from the values actually created and name the rounding residue (phase-25).

**Background jobs**
- A singleton `BackgroundService` cannot inject scoped services; take `IServiceScopeFactory`, read options via `IOptionsMonitor`, and never let a tick's exception escape `ExecuteAsync` (`AlertSchedulerHostedService`).
- No `IsRowVersion()` token on a row a job writes repeatedly if a user can also write it — a cancel wedged a running import; the unique index on the occurrence key is the real correctness mechanism (phase-21a Decision C, bug 1).
- A job that writes must reuse the Create/Update commands under the initiating user's identity via scoped `IJobActingUser` (an `HttpContext` always wins), which re-checks permissions per row for free (phase-21a).
- Do-exactly-once means write and commit the claim row under a unique index before the external side effect; InMemory does not enforce unique indexes, so verify the race against SQL Server (phase-20e Decision C).
- Any feature that writes a blob needs its deletion story decided with it; reuse `IQueuedJobProcessor.SweepAsync` + `JobArtifactRetention.Period`, and delete the blob before stamping the row (phase-21b Decision E).
- A job that produces a file builds it into a buffer, then commits storage key and terminal status in one `SaveChangesAsync`; UIs gate Download on `HasArtifact`, never on `Status == Completed` (phase-21b).

**Files, ClosedXML, uploads and downloads**
- Sync-only writers (ClosedXML `SaveAs`) cannot target the live response stream; write to a `MemoryStream`, then `CopyToAsync` (`ReportSpreadsheetExporter.WriteWorkbookAsync`, phase-16c bug #3).
- A Minimal API endpoint binding `IFormFile` gets antiforgery metadata automatically and 500s unless it calls `.DisableAntiforgery()` (phase-18 bug #1).
- Executing `Results.Stream` against a bare `DefaultHttpContext` needs a `ServiceProvider` with `AddLogging()` (`MigratedRegisterTemplateRoundTripTests`).
- A `MultipartFormDataContent` under `using` in a helper that returns the `Task` unawaited is disposed mid-send (`ObjectDisposedException` from `TestHost`); await inside the helper (phase-22).
- ClosedXML returns empty text for hand-rolled `inlineStr` cells and ignores `<si>` past a stale `uniqueCount`; build import fixtures by filling the app's own generated template (phase-21a).
- `AdjustToContents()` measures every cell it is given — size columns over the header plus a sample of rows, state the row cap, and disclose truncation in the artifact (phase-21b).
- Import-template date columns need an explicit day-first-before-month-first format list, never bare `DateTime.TryParse`; assert the ambiguous case (`ImportRowReader.GetOptionalDate`, phase-21c).

**Angular**
- A component serving both `.../new` and `.../:id` must read the id from `route.paramMap` (an Observable) and re-derive "is new" on every emission (phase-3 bug #1).
- Annotate `HttpClient.get` `params` as `Record<string, string>`; a union including `{}` silently resolves to the `arraybuffer` overload (phase-3 bug #4).
- Don't share a `request$` variable across Create/Update when the result types differ; use an explicit `if/else` (phase-4 bug #3).
- Never bind `[value]` on a signal-fed native `<select>`; bind `[selected]` per `<option>` — this persisted wrong `WarehouseId`s, not just display glitches (phase-5/6/7).
- Footer totals on paginated screens come from a server-computed field over the full filtered set, never a client-side reduce over one page (phase-16c bug #1).
- The app is zoneless: a `computed()` over a plain `FormControl.value` caches forever; track UI-driving values in their own `signal()` written by the control's event handler (phase-17).
- Bootstrap's JavaScript is not loaded anywhere (`angular.json` has no `scripts`), so `data-bs-toggle` does nothing; drive menus from a signal (phase-22).
- A `.dropdown-menu` inside `.table-responsive` is clipped by the implied `overflow-y`; render it `position: fixed` at coordinates captured on open (phase-22).
- A pipe rendering from a global signal with an unchanging argument must be `pure: false` and memoize internally (`NepaliDatePipe`, phase-23).
- A pipe inside parentheses parses anywhere, including a ternary branch, but a bare pipe in a ternary branch does not; pipes stay illegal in event bindings (phase-23).
- A component test asserting an uppercase label fails when the casing comes from CSS `text-uppercase`; assert the source casing (phase-23).
- When a phase starts populating a previously-dead DTO field, grep the templates that consume it — `SalesRegisterQuery`'s export columns were filled and invisible (phase-23 bug #1).
- `<iframe [src]>` needs `DomSanitizer.bypassSecurityTrustResourceUrl` (safe only because the URL is API base + route GUID), while `<img [src]>` with the same string is fine (phase-22).
- `AmountPipe` renders two decimals by default; pass `| amount: 4` for figures legitimately smaller than a cent (phase-25).

**Testing and manual E2E**
- A vendor's always-pass dummy credential (Turnstile `1x000…AA`) accepts any input; proving the negative path needs the always-fail one (`2x000…AA`) swapped in (phase-20g).
- `UpdateRolePermissionsCommand.Grants` is a dictionary, not a list, and system Admin/Member roles cannot be edited (409) — a negative-permission proof needs a custom role.
- A browser pass in a non-interactive session works by exporting the ASP.NET dev cert, starting the `erp-web-ssl` profile, and transplanting curl's `erp_auth` cookie via `document.cookie` (phase-25 Step 3).

**Tooling and shell**
- `nvm use` from a shell that cannot create the symlink deletes `C:\nvm4w\nodejs` and reports success; recreate it with `cmd /c 'mklink /J "C:\nvm4w\nodejs" "%LOCALAPPDATA%\nvm\v24.11.0"'`.
- A `cat > file <<'EOF'` heredoc in the Bash tool is silently truncated past roughly 8 KB; use the Write tool or split it.

## Current status

**Every planned phase (0–25) is complete.** Phase 25 (Manufacturing) was the last row of
`docs/roadmap.md`'s index table; its full write-up, including the perpetual-vs-periodic posting
decision and the conservation-law proof, is in `docs/phase-25-status.md`. No confirm-live or
browser-pass debt is outstanding (phase-25's Step 3 records how to run a browser pass in a
non-interactive session).

**What remains** is not unfinished roadmap work:
- the deferred post-v1 list in `docs/roadmap.md` (multi-currency, Billing Locations + POS, IRD e-filing);
- carried items: server-rendered PDFs and `.xlsx` still print dates in AD (phase-23 Decision A); the
  three manufacturing reports have no `.xlsx` export; Phase 25's named follow-ups (Custom Status on
  Production Order, multi-level BOM explosion, Reporting Tags / Custom Fields / print on production documents).

Tests at last count: Domain 249, Application.UnitTests 571, Api.IntegrationTests 18, Angular 128;
`dotnet build` / `dotnet test` / `ng build` / `ng test` / `tsc --noEmit` all clean.

**Update rule for this section:** when a phase completes, add its one-liner to the Phase index above,
append its "read before X" paragraph to `docs/phase-lessons.md`, and replace this block with a
short orientation (what is done, what is next, test counts) — the phase's own story belongs in its
`docs/phase-N-status.md`, never here.
