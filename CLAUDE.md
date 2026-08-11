# ErpApp

A Tigg-style ERP/CRM/Accounting rebuild for Nepali SMEs. Clean Architecture + CQRS (MediatR) on .NET 10 (LTS), Angular 21 (LTS) frontend, SQL Server via EF Core.

**Read `docs/roadmap.md` first** for what phase we're on and what's next. Full context lives in `docs/`:
- `docs/product-requirements.md` — the PRD (what the product does, for whom, why)
- `docs/architecture-spec.md` — bounded contexts, aggregates, cross-cutting engines (GL posting, document numbering, FIFO costing, authorization)
- `docs/erp-module-scan.md` — raw research: a live walkthrough of the reference product (Tigg) this rebuild is modeled on, module by module
- `docs/roadmap.md` — phased build plan, Phase 0 (done) through Phase 8+
- `docs/phase-0-status.md` — history of Phase 0: what was built, bugs hit and fixed, current status
- `docs/phase-1a-status.md` — history of Phase 1a: what was built, bugs hit and fixed (read this before touching auth/config wiring — several non-obvious gotchas), current status
- `docs/phase-1b-status.md` — history of Phase 1b: what was built, scope decisions, bugs hit and fixed, current status
- `docs/phase-1c-status.md` — history of Phase 1c: what was built, scope decisions (Role/RolePermission shape, permission marker interfaces), bugs hit and fixed, current status
- `docs/phase-2-status.md` — history of Phase 2: what was built (generic lookup CRUD, DocumentNumberingRule/IDocumentNumberGenerator, EAV CustomFieldDefinition/Value, TenantSettings real fields), scope decisions, bugs hit and fixed (read this before writing any raw-SQL EF Core query — several non-obvious `Database.SqlQuery<T>`/generic-LINQ-translation gotchas), current status

## Stack & conventions
- Backend: .NET 10 (LTS), Clean Architecture (`src/Domain` → `src/Application` → `src/Infrastructure`/`src/Api`), CQRS via MediatR, FluentValidation, EF Core + SQL Server.
- Frontend: Angular 21 (LTS), in `web/`.
- Solution file is `ErpApp.slnx` (the new .NET 10 format, not `.sln`).
- Dependency rule: `Api → Application → Domain`; `Infrastructure → Application/Domain`. Nothing depends on `Infrastructure` or `Api` except `Api/Program.cs` (the composition root).
- Every command/query goes through the MediatR pipeline: `LoggingBehavior` then `ValidationBehavior` then `AuthorizationBehavior` (see `src/Application/Common/Behaviors/`). A command/query is only permission-gated if it implements `IRequirePermission` (`Application.Common.Security`) — most queries and Phase 0/1a/1b commands don't yet.
- Multi-tenancy: single database, shared schema, `OrganizationId` discriminator. No EF Core global query filter exists yet — every handler manually filters by `OrganizationId` in LINQ (e.g. `MyOrganizationsQueryHandler`, every Phase 2 lookup handler); introducing a shared `ITenantEntity`/`ApplyGlobalFilters<T>()` mechanism is a deliberately deferred, separate infra decision (would touch every existing Tenancy table too), not assumed to exist.
- Every transactional aggregate (Invoice, PurchaseBill, JournalVoucher, etc.) follows Draft → Approve lifecycle; document numbers are assigned **at Approve, not at Create** (confirmed live in the reference product — see `docs/erp-module-scan.md`'s Document Numbering section).

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

## Known gotchas (see docs/phase-0-status.md and docs/phase-1a-status.md for full history)
- MediatR 12.4.1's `RequestHandlerDelegate<TResponse>` is parameterless — call `next()`, not `next(cancellationToken)`, in pipeline behaviors.
- `dotnet ef` needs `Microsoft.EntityFrameworkCore.Design` referenced by whichever project is passed as `--startup-project` (`Api`), not just `Infrastructure`.
- **Never read configuration (`builder.Configuration.Get<T>()`, `.GetConnectionString(...)`, etc.) as a top-level statement in `Program.cs` before `.Build()` if the value gets captured into a closure that runs later** (an options-configure delegate, a DbContext options builder, `AddJwtBearer(options => ...)`, etc.) — that snapshot is taken too early to see config sources added afterward (notably `WebApplicationFactory`'s test-only overrides in `Api.IntegrationTests`), and the bug is easy to miss locally because developer-machine `user-secrets` are already loaded by the time the eager read happens, masking it. Bit us twice in Phase 1a (Jwt:SigningKey and ConnectionStrings:Default) — see `docs/phase-1a-status.md` item 8. Prefer `services.AddOptions<T>().Bind(configuration.GetSection(...))` (lazy) or resolve `IOptions<T>`/`IConfiguration` from `IServiceProvider` inside a lazily-invoked delegate.
- JWT bearer's default inbound claim mapping remaps `"sub"`/`"email"` to legacy XML-namespace claim types — set `options.MapInboundClaims = false;` or `ClaimsPrincipal.FindFirstValue(JwtRegisteredClaimNames.Sub)` silently returns null.
- Cookie `SameSite` must be `None` (not `Lax`), not just `Secure`, for the Angular dev server (`http://localhost:4200`) to receive it from the Api (`https://localhost:7104`) — differing scheme alone makes Chrome treat same-host requests as cross-site.
- `Response.Cookies.Delete(name, options)` only actually clears a cookie if `options` (`Path`/`Secure`/`SameSite`) matches what was used when the cookie was set — mismatched options are silently ignored by the browser.
- Validating a new migration by running `dotnet ef database update` against a scratch/Docker database (to sanity-check the generated SQL) does **not** apply it to the actual local dev database the Api's `ConnectionStrings:Default` user-secret points at — always follow up with a plain `dotnet ef database update` (no `--connection` override) before manually click-testing, or every endpoint that touches the new tables 500s with `Invalid object name`.
- `dotnet ef migrations add` orders operations by model diff, not by data safety — a migration that both drops a column and adds its replacement can scaffold the `DropColumn` *before* the `AddColumn`, silently losing the data needed to backfill the new one. Always read a scaffolded migration before applying it when a column's being replaced/retyped; reorder by hand (create/seed anything new first, backfill via raw SQL while the old column still exists, drop it last) — see `docs/phase-1c-status.md`'s bug #1 for a worked example (`OrganizationMembership.Role` string → `RoleId` FK).
- `dotnet ef migrations add` bundles the *entire* pending model diff into whichever migration you're adding — there's no way to scaffold two migrations from one model change without temporarily hiding part of the model from the DbContext. Plan for one migration per `migrations add` invocation, not per logical change; hand-review the single result instead of trying to force a split (see `docs/phase-2-status.md`'s bug #6).
- An EF Core enum property configured with `.HasDefaultValue(someValue)` where `someValue` differs from the enum's CLR `default` (its first-declared, "= 0" member) needs `.ValueGeneratedNever()` chained after it, or EF silently substitutes the SQL default on every insert whenever the in-memory value happens to equal `default(TEnum)` — even if a factory method explicitly chose that value. See `docs/phase-2-status.md`'s bug #2.
- `Database.SqlQuery<TResult>` (EF Core 8+) only accepts *composable* SQL (effectively a plain `SELECT`) — an `UPDATE ... OUTPUT` statement throws `InvalidOperationException` at query-translation time (not caught by `dotnet build`, only by actually running the query against a real provider). For an atomic read-and-increment, use an explicit transaction with `SELECT ... WITH (UPDLOCK, ROWLOCK)` followed by a separate `ExecuteSqlInterpolatedAsync` `UPDATE`, not a single `OUTPUT`-returning statement. Also: for a scalar `TResult`, the result set's column must be aliased `AS Value` — `SqlQuery<T>` binds by that column name, not positionally. See `docs/phase-2-status.md`'s bugs #3–4 (`DocumentNumberGenerator`).
- A MediatR handler generic over a type parameter constrained by an interface (e.g. `Handler<TLookup> where TLookup : ISomeInterface`) that accesses a property via that constraint (`x.SomeProperty`) risks EF Core's LINQ translator failing to map the *interface's* `PropertyInfo` back to the concrete entity's mapped column. Use `EF.Property<T>(x, nameof(ISomeInterface.SomeProperty))` instead. Also: such a handler's DI registration must target the closed generic `IRequestHandler<TRequest, TResponse>` (2 type args) explicitly — MediatR's assembly scan can't discover it, and a request implementing MediatR's bare `IRequest`/`IRequestHandler<T>` (1-arg convenience interfaces) may not satisfy the 2-arg registration's generic constraint depending on version; implement `IRequest<Unit>`/`IRequestHandler<T, Unit>` explicitly instead. See `docs/phase-2-status.md`'s bugs #1 and #5.

## Current status
**Phase 2 (Configuration foundation) is complete** — generic `ListLookupsQuery<TLookup>`/`DeleteLookupCommand<TLookup>` CRUD plus concrete Create/Update for `CreditTerm`/`PaymentMode`/`CustomStatus`/`ReportingTagCategory`/`ReportingTagOption` (new `configuration` schema), `TenantSettings`' real fields, `DocumentNumberingRule`/`IDocumentNumberGenerator` (race-safe under concurrent callers, integration-tested against real SQL Server), and `CustomFieldDefinition`/`CustomFieldValue` (EAV, definition CRUD only). Angular Configurations screens exist for CreditTerm and PaymentMode. Confirmed by hand: a fresh Admin can create/edit/delete both through the real UI against the real API/DB. See `docs/phase-2-status.md` for the full history (scope decisions, the `Database.SqlQuery<T>` composability gotchas, bugs hit and fixed). Next up: **Phase 3 — Contacts & Catalog**. See `docs/roadmap.md`'s Phase 3 section for the full task breakdown before starting.

Phase 1c (Minimal role/permission stub, Identity/Tenancy context) is complete — `Role`/`RolePermission` tables (two system-level roles, Admin/Member) and a real `AuthorizationBehavior` MediatR pipeline behavior replace the ad hoc admin checks Phase 1b inlined in `InviteUserCommandHandler`/`AcceptRequestCommandHandler`. Confirmed by hand: a fresh Admin can still create an Organization and invite a Member; a Member calling the invite endpoint directly gets a real HTTP 403. See `docs/phase-1c-status.md` for the full history (scope decisions on system-level vs. per-org roles, the `MembershipRole`-as-selector design, bugs hit and fixed).

Phase 1b (Organization & membership, Tenancy context) is complete — full login → create Organization via 3-step wizard → land on its dashboard → invite a second user → accept from that user's account flow confirmed working by hand, backed by real SQL Server persistence and real SMTP invite email. See `docs/phase-1b-status.md` for that history.

Phase 1a (User & auth, Identity context) is complete — CI green on PR [#1](https://github.com/lekhu-awasthi/ERP/pull/1) (`feature/phase-1a-identity-auth`). See `docs/phase-1a-status.md` for that history.
