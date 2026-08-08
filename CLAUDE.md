# ErpApp

A Tigg-style ERP/CRM/Accounting rebuild for Nepali SMEs. Clean Architecture + CQRS (MediatR) on .NET 10 (LTS), Angular 21 (LTS) frontend, SQL Server via EF Core.

**Read `docs/roadmap.md` first** for what phase we're on and what's next. Full context lives in `docs/`:
- `docs/product-requirements.md` — the PRD (what the product does, for whom, why)
- `docs/architecture-spec.md` — bounded contexts, aggregates, cross-cutting engines (GL posting, document numbering, FIFO costing, authorization)
- `docs/erp-module-scan.md` — raw research: a live walkthrough of the reference product (Tigg) this rebuild is modeled on, module by module
- `docs/roadmap.md` — phased build plan, Phase 0 (done) through Phase 8+
- `docs/phase-0-status.md` — history of Phase 0: what was built, bugs hit and fixed, current status
- `docs/phase-1a-status.md` — history of Phase 1a: what was built, bugs hit and fixed (read this before touching auth/config wiring — several non-obvious gotchas), current status

## Stack & conventions
- Backend: .NET 10 (LTS), Clean Architecture (`src/Domain` → `src/Application` → `src/Infrastructure`/`src/Api`), CQRS via MediatR, FluentValidation, EF Core + SQL Server.
- Frontend: Angular 21 (LTS), in `web/`.
- Solution file is `ErpApp.slnx` (the new .NET 10 format, not `.sln`).
- Dependency rule: `Api → Application → Domain`; `Infrastructure → Application/Domain`. Nothing depends on `Infrastructure` or `Api` except `Api/Program.cs` (the composition root).
- Every command/query goes through the MediatR pipeline: `LoggingBehavior` then `ValidationBehavior` (see `src/Application/Common/Behaviors/`).
- Multi-tenancy: single database, shared schema, `OrganizationId` discriminator + EF Core global query filter (not yet implemented — lands in Phase 1).
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

## Current status
**Phase 1a (User & auth, Identity context) is complete** — CI green on PR [#1](https://github.com/lekhu-awasthi/ERP/pull/1) (`feature/phase-1a-identity-auth`), full register → verify → log in → guarded dashboard → logout flow confirmed working by hand with real SMTP email delivery. See `docs/phase-1a-status.md` for the full history (several non-obvious bugs hit and fixed — worth reading before touching `Program.cs`/DI wiring). Next up: **Phase 1b — Organization & membership (Tenancy context)**. See `docs/roadmap.md`'s Phase 1b section for the full numbered task breakdown before starting.
