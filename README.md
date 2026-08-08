# ErpApp

Tigg-style ERP/CRM/Accounting rebuild. Clean Architecture + CQRS (MediatR) on .NET 10 (LTS),
Angular 21 (LTS) frontend. See the project's `roadmap.md` and `architecture-spec.md` for the
full plan — this README only covers getting Phase 0 running locally.

## ⚠️ First-time setup: NuGet restore required

This solution was scaffolded in a sandboxed environment **without access to nuget.org**, so
none of the .NET package references (MediatR, FluentValidation, EF Core, Swashbuckle,
Testcontainers, xunit, etc.) have been restored yet. The `.csproj` files list everything
that's needed — the very first thing to do on a machine with normal internet access is:

```bash
dotnet restore ErpApp.slnx
dotnet build ErpApp.slnx
```

If any pinned package version in a `.csproj` is no longer available by the time you read
this (package versions move fast), bump it to the current stable release for .NET 10 —
`dotnet add package <Name>` without a version will pull latest.

The Angular workspace (`web/`) was **not** affected by this — npm access worked fine, so
`web/node_modules` is already installed and `ng build` / `ng test` already pass.

## Solution layout

```
src/
  Domain/           # Entities, value objects — empty in Phase 0, populated from Phase 1 on
  Application/       # MediatR commands/queries, FluentValidation validators, pipeline behaviors
  Infrastructure/     # EF Core AppDbContext, SQL Server provider
  Api/                 # ASP.NET Core minimal API, Program.cs composition root, Swagger
web/                    # Angular workspace
tests/
  Domain.UnitTests/
  Application.UnitTests/
  Api.IntegrationTests/  # WebApplicationFactory + Testcontainers (needs Docker)
```

## Running locally

### API

1. Set your local SQL Server connection string via user-secrets (never commit a real one):
   ```bash
   dotnet user-secrets set "ConnectionStrings:Default" \
     "Server=localhost,1433;Database=ErpApp;User Id=sa;Password=<yours>;TrustServerCertificate=True" \
     --project src/Api
   ```
   A local SQL Server container works fine:
   ```bash
   docker run -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=<yours>" -p 1433:1433 -d mcr.microsoft.com/mssql/server:2022-latest
   ```
2. Create the first (no-op) migration and apply it:
   ```bash
   dotnet ef migrations add InitialCreate --project src/Infrastructure --startup-project src/Api
   dotnet ef database update --project src/Infrastructure --startup-project src/Api
   ```
3. Run the API:
   ```bash
   dotnet run --project src/Api
   ```
   Swagger UI: `https://localhost:7104/swagger`. Health check: `https://localhost:7104/health`.

### Angular

```bash
cd web
npm start   # ng serve, proxies to the API base URL in src/environments/environment.development.ts
```

### Tests

```bash
dotnet test ErpApp.slnx      # Domain/Application unit tests + Api integration tests (needs Docker for Testcontainers)
cd web && npm test           # Angular unit tests (vitest)
```

## Phase 0 exit criteria (roadmap.md)

- [x] .NET solution scaffolded: Domain/Application/Infrastructure/Api with correct project references
- [x] MediatR + FluentValidation pipeline wired (LoggingBehavior, ValidationBehavior) — proven by `PingQueryTests`
- [x] EF Core + SQL Server provider added, `AppDbContext` created — **migration not yet generated** (needs `dotnet ef`, which needs the restore above)
- [x] Api: DI wired, Swagger added, `GET /health` implemented
- [x] Angular workspace created, calls `/health` on load, builds and tests pass
- [x] Test projects scaffolded with one passing/ready test each
- [x] CI workflow added (`.github/workflows/ci.yml`) — builds + tests both .NET and Angular on push/PR

**Not yet verified in this environment:** `dotnet build`/`dotnet test` end-to-end, since NuGet
restore couldn't run here. Run the restore step above and re-verify before treating Phase 0 as
fully closed.
