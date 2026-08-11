# Phase 3 status — Contacts & Catalog

**Status: COMPLETE.** `Contact` (Customer/Supplier/Lead) and `Product` (Goods/Service) master-data
aggregates exist, backed by real EF Core/SQL Server persistence under two new schemas (`contacts`,
`catalog`), gated by the Phase 1c authorization pipeline. `ContactGroup`/`ProductCategory`
(self-referencing trees) and `UnitOfMeasurement` (flat lookup) reuse Phase 2's generic
`ListLookupsQuery<TLookup>`/`DeleteLookupCommand<TLookup>` pair. `ProductSecondaryUnit` (multi-UOM
child collection) is built and wired to a real `AddSecondaryUnit` command. Angular establishes the
record-detail-page chrome pattern (left mini-profile panel + vertical tab list + right content
pane) for the first time, alongside the now-familiar list-page chrome, for both Contacts and
Products. Confirmed by hand: a fresh Admin can create a ContactGroup, a Contact of each Type, a
UnitOfMeasurement, a ProductCategory, and a Product of each Type (including adding a secondary
unit) through the real UI against the real API/DB.

## Roadmap Phase 3 exit criteria — final status

- [x] `Contact` aggregate (Customer/Supplier/Lead), `CreateContact`/`UpdateContact`/
      `DeactivateContact` commands, `ContactGroup` tree with standard CRUD (reusing the generic
      List/Delete pair, concrete Create/Update)
- [x] `Product` aggregate (Goods/Service), `ProductCategory` tree, `UnitOfMeasurement`,
      `ProductSecondaryUnit` child collection with `AddSecondaryUnit`
- [x] Angular: Contacts list/detail, Products list/detail -- list-page chrome reused
      (`credit-term-list-page`-style for the two tree/flat lookup management screens,
      `organization-list-page`-style loading/error/empty pattern for the row-navigates-to-detail
      screens); record-detail-page chrome (left mini-profile + vertical tab list + right content
      pane) built for the first time and reused identically for both Contact and Product
- [x] `dotnet build`, `dotnet test` (115 tests: 45 Domain + 67 Application + 3
      Api.IntegrationTests, Docker-backed, all green), `ng build`, `ng test --watch=false` (7
      tests, all green) all pass
- [x] Manual E2E against real API/DB: registered a fresh user, verified email (code read via
      `sqlcmd` against the dev DB, same as Phase 2 -- SMTP delivery isn't console-stubbed),
      created an Organization, created a `ContactGroup` ("Wholesale"), created a Customer contact
      assigned to it (auto-numbered `0001`), confirmed the list screen and record-detail-page both
      render it correctly including the Edit form; created a `UnitOfMeasurement` ("Piece"/"pc")
      and a `ProductCategory` ("Electronics"), created a Goods Product assigned to both
      (auto-numbered `0001`), added a second `UnitOfMeasurement` ("Box"/"box") and a secondary
      unit on the Product (conversion rate 12, own pricing) -- every round-trip hit the real
      API/DB, confirmed via network log

*(Variant Products/Attributes remain explicitly out of scope, per the roadmap's own parenthetical
note -- not started.)*

## Scope decisions

1. **Deferred FK fields dropped, not stubbed.** `Product.SalesAccountId`/`SalesReturnAccountId`/
   `PurchaseAccountId`/`PurchaseReturnAccountId` (Account doesn't exist until Phase 4) and
   `PrintProfileId` (PrintingTemplate isn't built at all) are not on Phase 3's `Product`. Adding
   nullable FK-shaped columns pointing at nothing yet is exactly the kind of speculative column
   this codebase's engineering principles argue against; Phase 4+/later can add them as a clean
   additive migration once the target aggregates exist.
2. **Tax modeled as a fixed 3-value enum (`VatRate`) on `Product`, not a Configuration lookup** --
   the scan doc confirms Tigg exposes exactly 3 fixed options (No Vat / 0 Vat / 13% Vat), not an
   admin-managed list. Never given a `.HasDefaultValue` (always set explicitly by `Product.Create`),
   so the enum-default EF gotcha (see phase-2-status.md's bug #2) never applies -- same treatment
   given to `Contact.Type`/`Product.Type`/`Product.ValuationMethod`.
3. **`Type` is immutable after creation** on both `Contact` and `Product` (no `UpdateType`) --
   switching a Customer into a Supplier or a Goods item into a Service is a modeling smell the
   reference product doesn't expose either. Enforced in the Angular form too (the Type radio group
   is `disabled` once a record exists), not just left to the API to reject.
4. **RowVersion/optimistic concurrency deferred** for `Contact`/`Product`. Architecture-spec §5's
   rule targets true transactional documents; the only two aggregate roots that have it so far
   (`Organization`, `DocumentNumberingRule`) both have narrow single-editor usage patterns. Adding
   it now means round-tripping RowVersion through every Angular edit form for no proven need yet --
   additive to bring back later if concurrent-edit conflicts turn out to matter.
5. **Member permission scope, decided per-entity** (unlike Phase 2, where every lookup got the
   same Member-View-only treatment):
   - `Contact`/`Product` are working data Members create/edit daily -> **Member gets View+Manage**.
   - `ContactGroup`/`ProductCategory`/`UnitOfMeasurement` are taxonomy/control-plane, same shape as
     Phase 2's lookups -> **Member gets View only, Manage denied** (explicit `IsGranted=false` row,
     matching Phase 2's precedent exactly).
6. **No recursive-CTE `ITreeQuery<T>` yet.** Phase 3's only tree consumer is a flat picker dropdown
   -- Angular renders indentation client-side by walking `ParentId`/`Name` (see the new
   `core/common/tree.ts`'s `buildTreeRows`, shared by both list-pages' parent-picker and both
   detail-pages' group/category picker). The generic `ListLookupsQuery<TLookup>` already returns
   the full flat list; a subtree-rollup query is deferred until something (Trial Balance-style
   rollups) actually needs it.
7. **`ContactGroup`/`ProductCategory`/`UnitOfMeasurement` reuse the generic
   `ListLookupsQuery<TLookup>`/`DeleteLookupCommand<TLookup>`** (they satisfy `ITenantLookupEntity`
   fine -- the extra `ParentId`/`ShortName` fields don't conflict with the interface) with their
   own concrete `Create`/`Update` commands, exactly like Phase 2's ReportingTagCategory/Option pair.
8. **Codes are auto-assigned at Create, not Approve** -- `Contact`/`Product` aren't `Draft->Approve`
   aggregates (no such lifecycle is named for them anywhere in the spec/roadmap), so the "numbers
   assigned at Approve" rule doesn't apply. `DocumentType.Contact`/`DocumentType.Product` already
   existed in the shared enum specifically for this ("numbering-pool-only codes" per its own doc
   comment), and `IDocumentNumberGenerator` was already built and integration-tested in Phase 2 --
   `CreateContactCommandHandler`/`CreateProductCommandHandler` just call `GetNextNumberAsync`.
9. **Money precision**: `decimal(18,4)` for `SellingPrice`/`PurchasePrice`/`OpeningBalance`;
   `decimal(18,6)` for `ProductSecondaryUnit.ConversionRate` -- straight from architecture-spec §5.
10. **Uniqueness**: `(OrganizationId, Name)` unique per tree/lookup (simpler than a
    `ParentId`-qualified unique index, matches `CreditTerm`'s shape); `(OrganizationId, Code)`
    unique on `Contact` and `Product`.
11. **FK delete behavior: `Restrict` everywhere**, matching `ReportingTagOption`'s precedent exactly
    -- no special app-layer handling of the resulting DB error if someone tries to delete a
    referenced `ContactGroup`/`ProductCategory`/`UnitOfMeasurement`, same scope cut Phase 2 made.
    Exception: `Product` -> `ProductSecondaryUnit` is `Cascade` (deleting a Product should delete
    its own secondary-unit rows -- this is a child of the aggregate, not a reference to another
    one).
12. **`ProductSecondaryUnit` is an encapsulated child collection (private backing field)** -- the
    first of its kind in this codebase. `Product.SecondaryUnits` is exposed as
    `IReadOnlyList<ProductSecondaryUnit>`, mutated only via `Product.AddSecondaryUnit(...)`; EF Core
    maps it via `.HasMany(x => x.SecondaryUnits).WithOne()...SetPropertyAccessMode(PropertyAccessMode.Field)`.
    `TestAppDbContext` needed the same mapping restated manually (no `ApplyConfigurationsFromAssembly`
    call there, by design -- see phase-2-status.md's `TestAppDbContext` note).

## New cross-cutting pieces (will matter for Phase 4+)

- **`core/common/tree.ts`'s `buildTreeRows`** (Angular) -- a generic client-side flattener for any
  `ParentId`-linked list into depth-ordered rows, used by both list-pages' own parent picker and
  both detail-pages' group/category picker. Any later phase adding another self-referencing tree
  (e.g. `AccountGroup` in Phase 4) can reuse this directly instead of re-deriving the recursion.
- **Record-detail-page chrome** (`contact-detail-page`/`product-detail-page`'s shared shape: left
  mini-profile panel + vertical tab list + right content pane, one component handling both `new`
  and `:id` routes) -- the template every later module's detail screens should clone. Note the
  route-reuse gotcha below before doing so.
- **`DocumentType.Contact`/`DocumentType.Product` numbering pool now has real callers** --
  confirms the "numbering-pool-only codes" pattern from architecture-spec.md §3.1 works for
  Create-time (not just Approve-time) number assignment.

## Bugs hit and fixed along the way

1. **Angular's default route-reuse strategy keeps the same component instance alive across
   `contacts/new` -> `contacts/:contactId` navigation** (both match the same route path,
   `organizations/:id/contacts/:contactId`), so a `contactId`/`isNew` captured once from
   `route.snapshot.paramMap` in the constructor went stale immediately after Create redirected to
   the new record's own URL -- the page kept showing "New Contact" with the just-submitted form
   even though the URL and the underlying data were correct (confirmed via network log: the
   `GET .../contacts/{id}` for the new record never even fired). Fixed by subscribing to
   `route.paramMap` (an `Observable`, not a one-time snapshot) in the constructor and re-running
   the whole "is this new or existing, load-or-reset" branch on every emission; `isNew` became a
   `signal<boolean>` instead of a plain readonly field so the template stays reactive too. Same fix
   applied to `product-detail-page` (identical shape, same route-config overlap). Worth remembering
   for any future single-component "handles both create and edit via the same route path" pattern.
2. **`ContactsService`/`CatalogService`'s `baseUrl()` dropped the `/api` path segment** --
   copy-adapted from `ConfigurationService`'s `${environment.apiBaseUrl}/api/organizations/...`
   but written as `${environment.apiBaseUrl}/organizations/...`, missing the literal `/api`
   `ConfigurationService` bakes into its own template string rather than getting from
   `environment.apiBaseUrl` (which is just the API's origin, `https://localhost:7104` in dev, no
   path). Every Contacts/Catalog list call 404'd until caught during manual E2E verification
   (`GET https://localhost:7104/organizations/.../contacts` instead of
   `.../api/organizations/.../contacts`). Fixed by adding the missing `/api` segment in both
   services' `baseUrl()`.
3. **Dev machine's active Node version (via `nvm-windows`) was 16.20.2**, too old for Angular 21's
   build tooling (`os.availableParallelism`, added in Node 18.4, is called unconditionally by
   `@angular/build`) -- `ng build`/`ng serve` failed immediately with
   `TypeError: (0 , node_os_1.availableParallelism) is not a function`. Not a code bug, but
   documented here since it will bite the next person on this machine too: `nvm use <version>`
   requires an elevation prompt this non-interactive environment can't satisfy (it hangs
   indefinitely, and killing the hung process mid-symlink-swap left `C:\nvm4w\nodejs` pointing at
   nothing, breaking `node`/`npm` entirely for a few minutes). Fixed by recreating the symlink
   directly as a **directory junction** (`mklink /J`, which -- unlike a true symbolic link or
   `nvm use`'s own mechanism -- does not require admin rights) pointing at the already-locally-cached
   Node 20.20.2 under `%NVM_HOME%` (`C:\Users\<user>\AppData\Local\nvm\v20.20.2`). If this recurs,
   check `nvm list` for an already-installed newer version before reaching for `nvm use`.
4. **Angular's `HttpClient.get<T>(url, { params })` overload resolution fails silently into the
   wrong overload when `params`'s inferred type is a union including `{}`** --
   `const params = type ? { type } : {};` produces `{ type: ContactType } | {}`, which doesn't
   satisfy any of `HttpClient.get`'s `params?: HttpParams | Record<string, ...>` overloads cleanly;
   TypeScript's overload resolution gave up and picked the `responseType: 'arraybuffer'` overload
   as a fallback, so the compile error read as `Type 'Observable<ArrayBuffer>' is not assignable to
   type 'Observable<Contact[]>'` -- a confusing symptom pointing away from the real cause (caught by
   `ng build`, not by `ng test`, since no test exercised this method's generic type on real data).
   Fixed by annotating `params: Record<string, string>` explicitly on both `ContactsService`'s and
   `CatalogService`'s `list*` methods with an optional type filter.

## What's next

**Phase 4 — Accounting core** (see `roadmap.md`): `AccountGroup` (tree) + `Account` (leaf),
`JournalVoucher` as the first real `ApprovableTransaction` (Draft -> Approve, real use of
`IDocumentNumberGenerator` at Approve time this time, not Create), `IGlPostingRule<T>` +
`GlJournalEntry.Post()`, `CashTransfer`. This is also the natural point to circle back and add the
FK columns Phase 3 deliberately deferred (`Product.SalesAccountId`/etc.) as a clean additive
migration once `Account` exists.
