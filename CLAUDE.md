# ErpApp

A Tigg-style ERP/CRM/Accounting rebuild for Nepali SMEs. Clean Architecture + CQRS (MediatR) on .NET 10 (LTS), Angular 21 (LTS) frontend, SQL Server via EF Core.

**Read `docs/roadmap.md` first** for what phase we're on and what's next. Full context lives in `docs/`:
- `docs/product-requirements.md` — the PRD (what the product does, for whom, why)
- `docs/architecture-spec.md` — bounded contexts, aggregates, cross-cutting engines (GL posting, document numbering, FIFO costing, authorization)
- `docs/erp-module-scan.md` — raw research: a live walkthrough of the reference product (Tigg) this rebuild is modeled on, module by module
- `docs/roadmap.md` — phased build plan, Phase 0 (done) through Phase 8+
- `docs/phase-0-status.md` — history of Phase 0: what was built, bugs hit and fixed, current status

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

Local SQL Server connection string is set via `dotnet user-secrets`, not in `appsettings.json` (see `src/Api/appsettings.Development.json` for the pointer). Never commit a real connection string.

## Known gotchas (see docs/phase-0-status.md for full history)
- MediatR 12.4.1's `RequestHandlerDelegate<TResponse>` is parameterless — call `next()`, not `next(cancellationToken)`, in pipeline behaviors.
- `dotnet ef` needs `Microsoft.EntityFrameworkCore.Design` referenced by whichever project is passed as `--startup-project` (`Api`), not just `Infrastructure`.
- CORS is currently wide open (`AllowAnyOrigin`) in `Program.cs` for local dev — tighten to an explicit origin allow-list once Phase 1 wires up cookie-based JWT auth (can't combine `AllowAnyOrigin` with `AllowCredentials`).

## Current status
Phase 0 (solution scaffolding) is complete — CI green at `https://github.com/lekhu-awasthi/ERP`. Next up: **Phase 1 — Identity & Tenancy** (register → verify email → log in → create Organization via 3-step wizard → land on dashboard shell). See `docs/roadmap.md` Phase 1 section for the full numbered task breakdown before starting.
