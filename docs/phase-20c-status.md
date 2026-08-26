# Phase 20c status — Cost Terms

**TL;DR:** Added the `CostTerm` lookup (`{ Id, OrganizationId, Name, Category, IsActive, CreatedAt }`
with a `CostTermCategory` of `AdditionalCost`/`ProductionCost`) as prerequisite reference data for
Phase 25's Manufacturing — nothing consumes it yet, by design. Pure `LookupList<T>` pattern work: it
reuses the generic `ListLookupsQuery<CostTerm>`/`DeleteLookupCommand<CostTerm>` untouched and adds
only the concrete `CreateCostTermCommand`/`UpdateCostTermCommand` pair the pattern requires, plus a
`cost-terms` endpoint group and one Angular Configurations screen rendering the reference product's
two sections over the single shape. Permission keys follow every other Configuration lookup's
Member-View/Admin-Manage split. No live-confirmation pass was needed (see decision #1). Tests:
Domain.UnitTests 128 (+2), Application.UnitTests 278 (+9), Angular 7 specs (unchanged),
`dotnet build`/`ng build`/`tsc --noEmit` clean. Manual E2E via curl + `sqlcmd` against a fresh
Organization: full Create/Edit/Delete round-trip in both categories, per-category uniqueness proven
both ways, and **four** separate 403s naming the exact key — plus a full live-browser pass through
the real screen (create/edit/delete round-trips, the select prefilling correctly for a
non-default category, and a 409 surfaced in the UI).

## Scope decisions

1. **No confirm-live pass against the Tigg UAT tenant — deliberately, not skipped.** The
   confirm-live discipline (Phase 8f's Annex 5 lesson) exists for screens whose *shape* is
   unconfirmed. `erp-module-scan.md` Configurations §7 already recorded this one from a hands-on
   pass, including the exact data model — "Two sections: Additional Cost Terms (landed-cost items —
   Freight, Insurance, Customs Duty) and Production Cost Terms (Expense Term values for
   BOM/Production Journal). Data model: CostTerm { id, name, category(AdditionalCost/ProductionCost) }."
   There is no unconfirmed shape left to reverse-engineer, and no consuming document form exists yet
   whose layout could diverge. This is the one remaining Phase 20 sub-phase where that's true; 20b
   and 20d both still need the live pass.
2. **`Category` is a real discriminator on one entity, not two entities or two endpoints.** The two
   sections select into genuinely different consuming contexts (landed cost on a purchase versus
   expense terms rolled into a BOM/Production Journal cost), but they share every field. One table
   with a `CostTermCategory` column keeps `ITenantLookupEntity` satisfied, so the generic List/Delete
   pair works with zero changes — splitting into two entities would have doubled the surface for no
   behavioral gain. This mirrors `CustomStatus`'s `DocumentType` discriminator exactly.
3. **Uniqueness is per `(OrganizationId, Category, Name)`, not per organization.** "Freight" is a
   plausible name in *both* sections, and blocking the second one would be a bug the reference
   product's own two-section layout implies shouldn't exist. Enforced in both handlers and by a
   unique index. Proven both directions in E2E (#2 and #3 below).
4. **`IsActive` is edit-only in the UI, matching every sibling lookup screen.** `CostTerm.Create`
   always starts active; the checkbox only appears once you're editing an existing row. Copied from
   `credit-term-list-page` rather than re-derived.
5. **Nothing consumes `CostTerm` yet, and that is the whole point.** Roadmap Phase 20 item 3 scopes
   this explicitly as "prerequisite reference data for Phase 25's Manufacturing." Same precedent
   `CreditTerm` set in Phase 2 (built a phase or more ahead of its consumer). The entity's own doc
   comment says so, so a future reader doesn't mistake the absence of readers for an oversight.
6. **The two sections are derived client-side from one list call, not two endpoints.** The list
   endpoint returns both categories in a single bounded page (Phase 16c's `listAll` convention), so
   the Angular page filters into a `sections` computed. One reload path after every save/delete, and
   the server keeps exactly one route group.

## Permission-key derivation

`Configuration.CostTerm.View` / `Configuration.CostTerm.Manage`, seeded **Admin: View=true,
Manage=true; Member: View=true, Manage=false** — the identical split every other Configuration
lookup uses (`CreditTerm`/`PaymentMode`/`TdsType`).

Reasoning, not defaulted: a cost term is tenant-wide control-plane reference data, not per-user
working data, so curating the list is an Admin act. Members need **View** specifically because
Phase 25's BOM and Production Journal forms will have to populate a cost-term picker for ordinary
users — a Member-denied View would break that consumer before it's written. `.Manage` covers
Create/Update/Delete as one grant, per `PermissionKeys.cs`'s standing note that the
Create/Edit/Delete/Approve split is reserved for transactional documents.

Both constants were added to `PermissionKeys.cs` (auto-discovered by `PermissionKeyCatalog`'s
reflection) **and** to `RolePermissionConfiguration.HasData` before scaffolding the migration, per
the Phase 9 lesson — GUIDs `...0125`–`...0128`, continuing the existing sequence.

## Files touched

- Domain: `CostTerm.cs`, `CostTermCategory.cs` (new).
- Application: `CreateCostTerm`/`UpdateCostTerm` command+handler+validator trios (new);
  `PermissionKeys.cs`, `LookupPermissionKeys.cs` (both switch arms), `IAppDbContext.cs`,
  `DependencyInjection.cs` (`RegisterLookupHandlers<CostTerm>`).
- Infrastructure: `CostTermConfiguration.cs` (new), `AppDbContext.cs`,
  `RolePermissionConfiguration.cs`, migration `20260826155739_Phase20cCostTerms`.
- Api: `ConfigurationEndpoints.cs` — `MapCostTermEndpoints` (GET/POST/PUT/DELETE `/cost-terms`).
- Web: `cost-term-list-page/` (new), `configuration.models.ts`, `configuration.service.ts`,
  `app.routes.ts`, `configuration-shell.html` (tile).
- Tests: `Domain.UnitTests/Configuration/CostTermTests.cs`,
  `Application.UnitTests/Configuration/CreateCostTermCommandHandlerTests.cs` and
  `UpdateCostTermCommandHandlerTests.cs` (all new); `TestSupport/TestAppDbContext.cs` (DbSet).

## Migration review

`20260826155739_Phase20cCostTerms` is purely additive — `CreateTable` + `InsertData` (4
role-permission rows) + `CreateIndex`. No column drop/replace, so the Phase 1c scaffold-ordering
hazard doesn't apply. Read before applying anyway, per the standing rule. Applied to the local dev
database with a plain `dotnet ef database update` (no `--connection` override), per the standing
gotcha.

## Manual E2E (fresh Organization, curl + sqlcmd)

Fresh Organization `Phase20c Test Org` (`b2a1bff3-…`), created with `manufacturing: true` so the
tenant matches this lookup's eventual consumer. Reusable Admin test login (`Testing:*`
user-secrets) plus a second registered-and-DB-activated user holding the seeded **Member** role for
the negative proofs — no test credentials committed. Api run with `--launch-profile https` per the
Phase 11 gotcha; `[identity].Users` bracket-quoted per the same doc.

1. **Create, both categories** — `Freight`, `Insurance`, `Customs Duty` (AdditionalCost) and
   `Machine Hours` (ProductionCost) → `201` each.
2. **Same name in the *other* category accepted** — a second `Freight` under ProductionCost → `201`.
   This is decision #3's positive direction.
3. **Duplicate in the *same* category rejected** — `Freight`/AdditionalCost again → `409`
   ("A cost term named 'Freight' already exists for AdditionalCost.").
4. **Malformed category rejected** — `"category":"NotACategory"` → `400` at JSON binding, not a
   silent coerce to the enum's first member.
5. **List** → `200`, all 5 rows, ordered by Name, `category` serialized as a string.
6. **Update** — renamed `Insurance` → `Marine Insurance` with `isActive:false` → `200`; moved
   `Customs Duty` from AdditionalCost to ProductionCost → `200` (a category move is legal when the
   name is free in the destination). Renaming onto a taken name in the same category → `409`;
   unknown id → `404` ("Cost term not found.").
7. **Delete** — `Machine Hours` → `204`; deleting again → `404`. `sqlcmd` confirmed 4 rows remain.
8. **`sqlcmd` verification** of `configuration.CostTerms`: both `Freight` rows coexist under
   different categories, `Marine Insurance` persisted with `IsActive = 0`, `Customs Duty` persisted
   under `ProductionCost`, `Category` stored as the string `AdditionalCost`/`ProductionCost`.
9. **Seed verification** — `sqlcmd` against `tenancy.RolePermissions` confirms exactly the four
   intended rows (Admin View/Manage granted, Member View granted, Member Manage denied).
10. **Negative permission proofs, four of them**, as the Member-role user:
    - `POST /cost-terms` → **`403`** naming `Configuration.CostTerm.Manage`.
    - `PUT /cost-terms/{a nonexistent guid}` → **`403`** (not `404`) — proves `AuthorizationBehavior`
      fired *before* the handler could look the id up.
    - `DELETE /cost-terms/{a nonexistent guid}` → **`403`** (not `404`), the same proof for the
      generic `DeleteLookupCommand<CostTerm>` path.
    - `GET /cost-terms` on an organization the user is **not** a member of → **`403`** naming
      `Configuration.CostTerm.View` — the `IOrganizationScoped` membership check (Phase 12).
    - And the positive control: `GET /cost-terms` on their *own* org → `200`. The View/Manage split
      is genuinely granular, not a blanket membership gate.
11. Unauthenticated `GET` (no cookie) → `401`.
12. `dotnet build` clean; Domain.UnitTests 128 and Application.UnitTests 278 green; `ng build` and
    `tsc --noEmit` clean; `ng test --watch=false` 7 specs green. `Api.IntegrationTests` not run —
    Docker Desktop was not running (CLAUDE.md's standing carve-out for that suite).

## Live browser (real UI, real clicks)

Against the same `Phase20c Test Org`, on top of the curl-seeded rows. The user signed in themselves;
every step below was driven through the real screen, not the API.

1. **Configurations index** — the new "Cost Terms" tile renders between Credit Terms and Payment
   Modes; clicking it routes to `/configuration/cost-terms`.
2. **Initial render** — both sections present with the curl-seeded rows correctly partitioned:
   `Freight` + `Marine Insurance` (badged **Inactive**, matching `IsActive = 0`) under Additional
   Cost; `Customs Duty` + `Freight` under Production Cost. The two `Freight` rows coexisting in
   different sections is decision #3 visible on screen.
3. **Create through the form** — typed `Labour Hours`, set the Category select to Production Cost,
   clicked Add. Row appeared under **Production Cost Terms**, and `sqlcmd` confirmed
   `Category = ProductionCost`. This is the select gotcha's real test: a `[value]`-style race would
   have shown/persisted `AdditionalCost` silently. The form then reset to "New Cost Term" with the
   select back at its default.
4. **Edit prefill, including the discriminating select case** — clicking Edit on `Marine Insurance`
   switched the heading to "Edit Cost Term", revealed the Active checkbox **unchecked** (matching
   `IsActive = 0`), and prefilled name + category. Then, the case that actually discriminates:
   clicking Edit on `Customs Duty` (a **ProductionCost** row, i.e. *not* the select's default)
   prefilled `selectedIndex: 1` / `value: "ProductionCost"` — no fallback to index 0.
   `formControlName` + static `<option>` children sidesteps the `[value]`-vs-`@for` race entirely
   (phase-5/6/7 gotcha), which is why this screen doesn't need the `[selected]`-per-option workaround.
5. **Edit round-trip** — renamed `Customs Duty` → `Customs & Excise Duty`, moved it Production →
   Additional, unchecked Active, clicked Update. The row moved sections on screen and `sqlcmd`
   confirmed all three changes persisted.
6. **Conflict surfaced in the UI** — adding `Freight` under Additional Cost again rendered the red
   alert carrying the server's exact message ("A cost term named 'Freight' already exists for
   AdditionalCost."), not a silent no-op.
7. **Delete** — the two-step inline confirm ("Delete this cost term?" → Confirm/Cancel) appeared on
   that row only; confirming removed `Labour Hours`, and `sqlcmd` confirmed 4 rows remain.
8. **Console** — the only error logged across the whole session is the browser's own resource-level
   log line for the deliberate `409`. No JS exceptions, no Angular errors.

One cosmetic observation, deliberately **not** changed: a stale error alert survives a subsequent
successful *delete* (only `save()` clears `errorMessage`). `credit-term-list-page` and every sibling
lookup screen behave identically, so fixing it here alone would make this one page diverge from six
others. Worth a single sweep across all lookup screens if it ever bothers anyone — not worth a
one-page inconsistency.

## Known limitations / follow-ups

- **Nothing reads `CostTerm` yet.** Intentional (decision #5). Phase 25 is the consumer.
- **No `IsActive` filtering on the list endpoint.** Same as every other lookup — inactive rows come
  back and the UI badges them. Whenever the first consumer builds a cost-term picker it will need to
  filter to active rows itself, exactly as the other lookups' consumers do.
- **`LookupPermissionKeys`'s doc comment still says "five cases"** while the switch now has sixteen.
  Pre-existing drift, not introduced here; left alone rather than widening this diff.
- **`docs/phase-20a-status.md`'s TL;DR is stale on one point:** it says both Phase 19 leftovers were
  "re-flagged, not fixed", but the Reporting Tags admin screen shipped in #34 and the Purchase/COGS
  double-count was fixed in #35 with a regression test in #36 — all on `main` before 20a merged.
  Noted here rather than edited into another sub-phase's status doc.
- **Node version friction, environment-only:** `nvm4w`'s active version on this machine is v16.20.2,
  below the Angular CLI's v20.19 minimum, so `ng build`/`ng serve`/`ng test` fail out of the box.
  Worked around by prepending `C:\Users\lekhu\AppData\Local\nvm\v24.11.0` to `PATH` for those
  commands rather than switching the global `nvm` symlink. Worth an `nvm use 24.11.0` if this
  recurs.

## What's next

Phase 20b (Custom Status wiring) or 20d (Printing/Custom Templates) — both need a confirm-live pass
against the Tigg UAT tenant before any code, unlike this one. See `docs/roadmap.md`'s Phase 20
section.
