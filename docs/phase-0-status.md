# Phase 0 status — Solution scaffolding

**Status: COMPLETE.** CI green on both jobs (`.NET build & test`, `Angular build & test`) at `https://github.com/lekhu-awasthi/ERP`, `main` branch, as of 2026-08-08.

Phase 0 was built through a Claude Cowork cloud sandbox session that had no NuGet access (only npm/pip/etc were allowlisted), so every `.csproj` and code file was scaffolded there and pushed file-by-file to the actual dev machine (`C:\Users\lekhu\Downloads\erp-app`) for the user to `dotnet restore`/`build`/`test`/`git push` for real. That produced a handful of real bugs (below) that only surfaced once real packages were restored — all fixed and confirmed working.

## Roadmap Phase 0 exit criteria — final status

- [x] .NET solution: `Domain`/`Application`/`Infrastructure`/`Api`, correct project references, `.editorconfig` + analyzers
- [x] MediatR + FluentValidation pipeline wired (`LoggingBehavior`, `ValidationBehavior`) — proven by `PingQueryTests`
- [x] EF Core + SQL Server: `AppDbContext`, `InitialCreate` migration created and applied to a local SQL Server instance
- [x] Api: DI wired, Swagger, `GET /health` — confirmed 200 locally
- [x] Angular workspace: builds, tests pass, shell calls `/health` and renders "API status: healthy" — confirmed live against the running API
- [x] Test projects: `Domain.UnitTests` + `Application.UnitTests` passing; `Api.IntegrationTests` requires Docker Desktop (Testcontainers)
- [x] CI workflow (`.github/workflows/ci.yml`) green on GitHub Actions, both jobs
- [x] Repo pushed to GitHub: `https://github.com/lekhu-awasthi/ERP`, `main` branch

## Bugs hit and fixed along the way

1. **MediatR delegate signature**: `next(cancellationToken)` → `next()` in `LoggingBehavior.cs`/`ValidationBehavior.cs` (MediatR 12.4.1, the version that actually restored, uses the parameterless `RequestHandlerDelegate<TResponse>` — a newer signature with a cancellation-token parameter doesn't exist in this version).
2. **EF Core Design package**: `dotnet ef` needs `Microsoft.EntityFrameworkCore.Design` referenced by the `--startup-project` (`Api`), not just `Infrastructure` (which had it `PrivateAssets="all"`, blocking it from flowing through). Added a direct reference in `src/Api/ErpApp.Api.csproj`.
3. **XML comment bug**: a double-hyphen (`--`) inside an XML comment in `ErpApp.Api.csproj` broke project-file parsing entirely ("An XML comment cannot contain '--'"). Fixed the comment wording.
4. **CORS**: added a permissive dev-only `AllowAnyOrigin`/`AllowAnyMethod`/`AllowAnyHeader` policy in `Program.cs`, so the Angular dev server (different port) can call the API. Flagged with a TODO to tighten to an explicit origin allow-list once cookie-based JWT auth lands in Phase 1 (can't combine `AllowAnyOrigin` with `AllowCredentials`).
5. **PingQueryTests DI bug**: test built a bare `ServiceCollection` with only `AddApplication()`, never `AddLogging()`, so `LoggingBehavior`'s `ILogger<T>` dependency couldn't resolve. Fixed by adding `services.AddLogging()` plus a `Microsoft.Extensions.Logging` PackageReference the test project was missing.
6. **CI: SQL Server service container failed to start** — the workflow referenced `${{ secrets.CI_SQL_SA_PASSWORD }}`, a GitHub Actions secret that was never created, so it resolved to an empty string (SQL Server refuses a blank/weak SA password). Fixed by hardcoding a throwaway CI-only password directly in the workflow instead (the container only lives for one CI run — no real secret needed).
7. **CI: `npm ci` failed** — `package-lock.json` had drifted out of sync with `package.json` (missing `@emnapi/core`/`@emnapi/runtime` optional-dependency entries, likely from a platform difference between when the lockfile was first generated and later `npm install`s). Fixed by regenerating a clean lockfile (`rm -rf node_modules package-lock.json && npm install`), verified with a clean `npm ci` + `ng build` + `ng test` before pushing.
8. **Api.IntegrationTests / Docker**: not a bug — `Testcontainers.MsSql` needs Docker Desktop running locally to execute. Not currently required for CI to pass (CI's own `.NET build & test` job runs on `ubuntu-latest`, which has Docker pre-installed); only matters for running that specific test project on a local dev machine without Docker.

## TestSprite

User asked about testing via TestSprite (third-party AI testing MCP, `npx @testsprite/testsprite-mcp@latest`). Not a Claude connector-registry item — it runs locally next to the actual dev servers, via the user's own local Claude Code CLI (`claude mcp add TestSprite --env API_KEY=... -- npx @testsprite/testsprite-mcp@latest`), not through the Cowork cloud session. User completed the `claude mcp add` step; whether a full TestSprite test pass has actually been run is unconfirmed — low priority relative to Phase 1 given how little UI surface exists yet to test (just `/health`).

## Tooling / workflow note for future sessions

This phase revealed that Cowork's cloud sandbox is a genuinely worse fit than Claude Code (the CLI) for this kind of iterative build-fix-rebuild work — no direct filesystem/git access, files had to round-trip through `SendUserFile` + a device bridge, and some paths (`.github/workflows/*.yml`) are blocked from remote writes entirely as a safety measure. **Recommendation: use Claude Code, running locally in this repo, for Phase 1 onward.** This `docs/` folder plus the repo-root `CLAUDE.md` exist specifically to give a fresh Claude Code session the same context this Cowork session had, without needing to re-explain the project.

## What's next

**Phase 1 — Identity & Tenancy** (see `roadmap.md` for the full task breakdown): User entity + registration/email-verification/login (Identity context), then Organization + membership + the 3-step creation wizard (Tenancy context), then a minimal hardcoded Role/Permission stub just enough to unblock later phases. This is the first real vertical slice — auth, a real aggregate, EF Core persistence, and a full Angular flow — and sets the pattern every later phase repeats.
