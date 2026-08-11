# Phase 2 status — Configuration foundation

**Status: COMPLETE.** The tenant-wide control-plane lookups (`CreditTerm`, `PaymentMode`,
`CustomStatus`, `ReportingTagCategory`/`ReportingTagOption`) share a generic Application-layer
CRUD pattern where genericity actually pays off (List/Delete), backed by real EF Core/SQL Server
persistence under a new `configuration` schema, gated by the Phase 1c authorization pipeline.
`DocumentNumberingRule`/`IDocumentNumberGenerator` (architecture-spec.md §3.1) is built and
integration-tested for duplicate-free concurrent number assignment, ready for Phase 4+'s
`ApproveXCommandHandler`s to call. `CustomFieldDefinition`/`CustomFieldValue` (EAV, §3.6)
definition CRUD is built; value read/write stays deferred to Phase 4+ as scoped. `TenantSettings`
carries its real fields (Suggest Selling Price mode, Product Price Basis, Inventory Tracking mode,
Negative Cash/Stock Balance actions), backfilled onto every pre-existing Phase 1b row. Angular
Configurations screens exist for `CreditTerm` and `PaymentMode` (the roadmap's stated minimum),
confirmed by hand: an Admin can create, edit, and delete both through the real UI against the real
API/DB.

## Roadmap Phase 2 exit criteria — final status

- [x] Generic `LookupList<T>` CRUD pattern (Application layer) -- built once for the two verbs
      where genericity is real (`ListLookupsQuery<TLookup>`/`DeleteLookupCommand<TLookup>`), see
      "Scope decisions" below for why Create/Update stayed concrete per lookup type
- [x] First concrete lookups: `CreditTerm`, `PaymentMode`, `CustomStatus` (with a `DocumentType`
      discriminator, decided now per the task's own prompt), `ReportingTagCategory`/
      `ReportingTagOption` -- all tenant-scoped (`OrganizationId`), full Create/Update/Delete/List
      command surface, all `IRequirePermission`+`IOrganizationScoped` from the start
- [x] `TenantSettings` real fields (`SuggestSellingPriceMode`, `ProductPriceBasis`,
      `InventoryTrackingMode`, `NegativeCashBalanceAction`, `NegativeStockBalanceAction`),
      defaults matching `erp-module-scan.md`'s confirmed live tenant behavior, migration backfills
      every pre-existing Phase 1b row (verified via `sqlcmd`: 7/7 rows backfilled)
- [x] `DocumentNumberingRule` + `IDocumentNumberGenerator`, lazily-created rows, race-safe under
      concurrent callers -- proven by a real-SQL-Server integration test (20 concurrent callers,
      zero duplicates; a second test proves the lazy-create race itself doesn't double-insert)
- [x] `CustomFieldDefinition` + `CustomFieldValue` (EAV) -- definition CRUD built; `CustomFieldValue`
      is entity + EF mapping only, no commands/queries, per the explicit descope (nothing writes/
      reads values before a real document type exists, Phase 4+)
- [x] Angular Configurations shell + `CreditTerm`/`PaymentMode` list-create-edit-delete screens,
      reusing `organization-list-page`/`organization-dashboard-page`'s chrome patterns
- [x] `dotnet build`, `dotnet test` (82 tests: 32 Domain + 47 Application + 3 Api.IntegrationTests,
      Docker-backed), `ng build` all green
- [x] Manual E2E: registered a fresh user, verified email (code read via `sqlcmd` against the real
      dev DB -- SMTP delivery isn't console-stubbed post-Phase-1b), created an Organization,
      confirmed Admin role, navigated Dashboard → Configurations → Credit Terms: created "Net 30",
      edited it to "Net 45", deleted it; same for Payment Modes ("Cash" → "Bank Transfer" → deleted)
      -- every round-trip hit the real API/DB, confirmed via network log + `sqlcmd`

## Scope decisions

1. **Generic pattern split by where genericity actually pays off.** `ListLookupsQuery<TLookup>`/
   `DeleteLookupCommand<TLookup>` are real generic MediatR requests (need nothing beyond
   `Id`/`OrganizationId`, identical across all 5 lookup types). `Create`/`Update` stayed concrete,
   one command/handler/validator trio per lookup type (10 verb-folders, matching the existing
   `Tenancy/Commands/<Verb>/` shape exactly) -- their extra fields genuinely diverge
   (`CreditTerm.DueDays`, `CustomStatus.DocumentType`, `ReportingTagOption.CategoryId`), and
   forcing genericity there would need either a loosely-typed payload bag (breaks FluentValidation)
   or a hook-method base class whose hooks would be most of each handler anyway. This satisfies the
   roadmap's literal "build the generic pattern once, instantiate per lookup" instruction for the
   two verbs where it's real, and avoids false genericity for the two where it isn't.
2. **Domain layer: a marker interface (`ITenantLookupEntity`), not a base class.** Matches this
   codebase's existing "plain sealed class, no `Entity`/`AggregateRoot` base" convention and its
   capability-interface idiom (`IRequirePermission`, `IOrganizationScoped`).
3. **Schema `configuration`** for all 8 new tables (matches architecture-spec.md §5's "one schema
   per bounded context" and §4.10's naming of this context). `TenantSettings`' new columns stay in
   the existing `tenancy.TenantSettings` table (an `ADD COLUMN`, not a new table).
4. **`DocumentType` enum, 17 entries, in `Domain.Common`** -- a shared cross-context vocabulary
   type (first Domain type outside Identity/Tenancy), not owned by any one bounded context, since
   `CustomStatus`, `DocumentNumberingRule`, and `CustomFieldDefinition`/`Value` all reference it.
   Stored via `.HasConversion<string>()` everywhere (precedent: `OrganizationMembership.Status`).
5. **`DocumentNumberingRule` rows are created lazily**, not eagerly seeded per Organization at
   `CreateOrganizationCommand` time -- avoids touching Phase 1b's command and avoids ~17 unused
   rows per tenant for document types that tenant may never use.
6. **Numbering concurrency ended up using explicit-transaction `SELECT ... WITH (UPDLOCK, ROWLOCK)`
   + `UPDATE`, not the originally-planned single atomic `UPDATE ... OUTPUT` statement** -- see
   "Bugs hit and fixed" below for why the single-statement approach didn't work with EF Core's
   `SqlQuery<T>` API. The UPDLOCK row lock is held for the rest of the transaction, so a second
   concurrent caller's identical `SELECT ... WITH (UPDLOCK)` on the same row blocks until the first
   caller commits -- serializing access without a client-side retry loop for the increment itself
   (a retry loop is still needed for the lazy-create race, which a row lock on a not-yet-existing
   row can't prevent).
7. **`CustomFieldValue` is entity + EF config only** -- no commands/queries, per the explicit
   descope (nothing writes/reads values until a real document type exists, Phase 4+).
   `ApplicableDocumentTypes`/`Value` both use the spec's "least clever option" (delimited string /
   `nvarchar(max)` + discriminator), not JSON or `sql_variant`.
8. **Permission keys: one View/Manage pair per lookup type**, not one shared key -- so a future
   Role Reference editor (Phase 8+) can toggle e.g. "Member can edit CreditTerm" independently of
   "Member can edit PaymentMode". **Judgment call**: Admin granted View+Manage on everything;
   Member granted View only, Manage explicitly denied (an `IsGranted=false` row, matching the
   existing `Tenancy.Organization.*` denial-row convention) -- Configuration lookups are tenant-wide
   control-plane settings, not per-user working data, so Member-read/Admin-write fits the existing
   Admin/Member split's intent.
9. **One migration (`AddConfigurationFoundation`), not the two originally planned.** The plan called
   for an isolated `TenantSettings` `ADD COLUMN` migration separate from the new-tables migration,
   for independent review/rollback of the only piece touching existing data. In practice,
   `dotnet ef migrations add` bundles the *entire* model diff since the last migration into
   whichever migration you're adding -- there's no built-in way to scaffold two migrations from one
   model change without temporarily hiding half the model, which would have been more fragile than
   the safety it bought. Reviewed the single scaffolded migration by hand instead (confirmed: the
   `TenantSettings` changes are 5 isolated `AddColumn` calls with no drops/renames; the `RolePermissions`
   seed diff is pure insert; all new unique indexes present as expected) and renamed it from the
   original `AddTenantSettingsFields` to the more accurate `AddConfigurationFoundation` before applying.
10. **Angular: `CreditTerm` and `PaymentMode` screens only**, matching the roadmap's stated minimum
    exit criteria ("at least CreditTerm and PaymentMode"). `CustomStatus`, `ReportingTagCategory`/
    `ReportingTagOption`, and `CustomFieldDefinition` all have working, tested, permission-gated
    Application/Api layers (List/Create/Update/Delete all function via direct API calls) but no
    Angular screen yet -- flagged as the roadmap's own "stretch, if time allows" framing, not
    completed this phase. Building their screens is templated, low-risk follow-up work (copy
    `credit-term-list-page`'s shape) whenever a later phase's document types need them surfaced.
11. **Delete confirmation uses an inline two-step button toggle, not `window.confirm()`.** Native
    `confirm()` dialogs are silently auto-dismissed by the Claude Browser pane's automation (no
    visible prompt, the underlying delete never fires) -- caught during this phase's own manual E2E
    pass when a "successful" delete click left the row in place. Replaced with a `confirmingDeleteId`
    signal that swaps the row's Edit/Delete buttons for a "Delete this X? [Confirm] [Cancel]" pair
    in place -- both more testable and arguably better UX than a native dialog, and there was no
    prior `confirm()`/`window.confirm` precedent elsewhere in the codebase to stay consistent with.

## New cross-cutting pieces (will matter for Phase 3+)

- **`ITenantLookupEntity`** (`Domain.Common`) and the generic `ListLookupsQuery<TLookup>`/
  `DeleteLookupCommand<TLookup>` pair (`Application.Configuration`) -- any later phase adding
  another simple tenant-scoped named list (e.g. `TdsType`, `CostTerm` per architecture-spec.md
  §4.10's fuller list) should implement this interface and get List/Delete for free; explicit DI
  registration lines live in `Application/DependencyInjection.cs`'s `RegisterLookupHandlers<T>`
  call block (MediatR's assembly scan can't discover these -- see "Bugs hit" below).
- **`DocumentType`** (`Domain.Common`) -- the shared enum every later phase's real aggregates
  (Invoice, PurchaseBill, JournalVoucher, ...) should reference for numbering/custom-status/custom-field
  wiring rather than inventing a parallel discriminator.
- **`IDocumentNumberGenerator`** (`Application.Common.Numbering`, impl in
  `Infrastructure.Persistence.DocumentNumberGenerator`) -- ready to call from Phase 4+'s
  `ApproveXCommandHandler`s. Reminder from its own doc comment: call it from `Approve`, never
  `Create` (documents sit at literal `"DRAFT"` until approved, confirmed live).
- **`EF.Property<T>(x, "PropertyName")` pattern for generic-handler LINQ queries** -- when a
  handler is generic over `TLookup : ITenantLookupEntity`, direct property access (`x.OrganizationId`)
  risks EF's translator failing to map the *interface's* `PropertyInfo` back to the concrete
  entity's mapped column (see "Bugs hit" below). Any future generic-over-entity-type handler should
  use `EF.Property<T>(x, nameof(IInterface.Property))` instead.

## Bugs hit and fixed along the way

1. **MediatR's bare `IRequest`/`IRequestHandler<TRequest>` (1-generic-arg convenience interfaces)
   didn't compile against the DI registrations needed for `DeleteLookupCommand<TLookup>`** --
   `services.AddTransient<IRequestHandler<DeleteLookupCommand<TLookup>>, ...>()` failed with
   `CS0311` ("no implicit reference conversion ... to MediatR.IRequest<MediatR.Unit>"), even though
   MediatR.Contracts' own source declares `IRequest : IRequest<Unit>`. Rather than chase the exact
   generic-variance interaction, switched both `DeleteLookupCommand<TLookup>` and
   `DeleteCustomFieldDefinitionCommand` to implement `IRequest<Unit>` explicitly and their handlers
   to `IRequestHandler<T, Unit>` returning `Unit.Value` -- the two-generic-arg form is unambiguous
   and matches exactly what MediatR resolves from the container regardless of version quirks.
2. **EF Core's "database-generated default, no configured sentinel value" warning was a real
   correctness bug, not noise.** `TenantSettings`' new enum columns used `.HasDefaultValue(...)` so
   the migration's `ADD COLUMN` would backfill Phase 1b's existing rows -- but for 3 of the 5 enums
   (`InventoryTrackingMode`, `ProductPriceBasis`, `NegativeStockBalanceAction`), the *chosen*
   default value differed from the enum's CLR `default` (its first-declared, "= 0" member). EF
   Core silently treats "current CLR value equals `default(TEnum)`" as "this property was never
   set," substituting the SQL `DEFAULT` on every insert -- meaning `TenantSettings.CreateDefault()`
   explicitly choosing `InventoryTrackingMode = PhysicalMovement` (enum value 0) would have been
   silently overwritten with `AccountingMovement` (the SQL default) had this gone unnoticed. Fixed
   by chaining `.ValueGeneratedNever()` after every `.HasDefaultValue(...)` call in
   `TenantSettingsConfiguration` -- keeps the SQL-level `DEFAULT` constraint (for the migration's
   one-time backfill) while telling EF to always send the current CLR value verbatim on insert,
   never substituting based on the sentinel convention.
3. **`Database.SqlQuery<TResult>` rejects non-composable SQL** -- the first `IDocumentNumberGenerator`
   implementation tried a single atomic `UPDATE ... OUTPUT DELETED.NextNumber` statement (avoiding
   any explicit lock hint, relying on the UPDATE's automatic exclusive row lock). This failed at
   runtime with `InvalidOperationException: 'FromSql' or 'SqlQuery' was called with non-composable
   SQL`, caught by this phase's own concurrency integration test against a real SQL Server (not
   caught by `dotnet build`, since it's a query-translation-time failure). `SqlQuery<T>` wraps its
   SQL text as a `FROM`-subquery for the LINQ pipeline, which requires a plain `SELECT` --
   `UPDATE...OUTPUT` isn't composable that way even though it returns a resultset. Rewritten to the
   classic explicit-transaction `SELECT ... WITH (UPDLOCK, ROWLOCK)` then `UPDATE` pair
   architecture-spec.md §3.1 names as the alternative -- see scope decision #6 above.
4. **`Database.SqlQuery<TResult>` for a scalar `TResult` requires the result set's column to be
   named `Value`.** After fixing bug #3, the SELECT still failed with `SqlException: Invalid column
   name 'Value'` -- `SqlQuery<int>` doesn't bind positionally to the first column, it looks for a
   column literally named `Value`. Fixed by aliasing: `SELECT NextNumber AS Value FROM ...`.
5. **Generic LINQ query translation through an interface-constrained type parameter is a real risk,
   not just style.** The generic `ListLookupsQueryHandler<TLookup>`/`DeleteLookupCommandHandler<TLookup>`
   initially used `x.OrganizationId == ...`/`x => x.Name` directly. Because `TLookup` is a generic
   parameter constrained by `ITenantLookupEntity`, the compiler emits the member access against the
   *interface's* `PropertyInfo`, which EF Core's expression-tree-to-SQL translator isn't guaranteed
   to resolve back to the concrete entity's mapped column (it matches by `MemberInfo`, and the
   interface's `PropertyInfo` differs from the implementing class's, even for an implicit
   implementation). Switched to `EF.Property<T>(x, nameof(ITenantLookupEntity.OrganizationId))`
   (string-keyed, resolved against the EF model directly) before this could manifest as a runtime
   failure -- confirmed via the passing `ListLookupsQueryHandlerTests`/`DeleteLookupCommandHandlerTests`,
   though these ran against EF InMemory, which is more forgiving here than the real SQL Server
   provider; worth keeping this pattern for any future generic-over-entity-type EF query.
6. **`dotnet ef migrations add` batches the entire pending model diff into one migration** -- ran
   into this trying to follow the plan's "two separate migrations" design (see scope decision #9);
   there's no scaffolding-time way to split one model change into two migrations short of
   temporarily hiding part of the model, which would have been more fragile than the two-migration
   split was meant to protect against. Resolved by keeping one migration, hand-reviewing it in full
   before applying (confirmed additive-only), and renaming it to accurately describe its combined
   scope.
7. **Native `window.confirm()` in a delete button is silently auto-dismissed by the Claude Browser
   pane's automation** -- clicking "Delete" during manual E2E verification appeared to do nothing
   (no visible dialog, no network request fired), which at first looked like a broken click handler.
   `read_network_requests` confirmed no `DELETE` call was ever sent. Replaced with an inline
   two-step confirm-toggle (see scope decision #11) -- both fixes the testability gap and avoids a
   native browser dialog most users find jarring in a polished ERP UI anyway.

## Known issue, not introduced this phase

`ng test --watch=false` has one pre-existing failure (`app.spec.ts`'s "should render title"),
caused by an **uncommitted, pre-existing local edit** to `web/src/app/app.html` (its `<h1>` was
commented out as part of an in-progress, unrelated rebrand pass) that predates this phase's branch.
Left untouched per this phase's scope (see the plan's note on pre-existing uncommitted changes) --
not staged or committed as part of this PR. All 6 other Angular tests, and all 82 backend tests,
pass.

## What's next

**Phase 3 — Contacts & Catalog** (see `roadmap.md`): `Contact` aggregate (Customer/Supplier/Lead),
`ContactGroup` tree, `Product` aggregate, `ProductCategory` tree, `UnitOfMeasurement` -- the first
real master-data screens establishing the reusable record-detail-page chrome pattern every later
module's screens will reuse (list-page chrome is now established twice over, from
Phase 1b/organizations and this phase/configuration lookups).

Also worth a follow-up, not blocking Phase 3: Angular screens for `CustomStatus`,
`ReportingTagCategory`/`Option`, and `CustomFieldDefinition` (APIs already built, tested, and
permission-gated this phase -- see scope decision #10) whenever a later phase's document types
create real demand for surfacing them in the UI.
