# Phase 24 — Variant Products & Attributes (FR-8.3)

## TL;DR

**A variant is a Product.** That one sentence is the phase. Confirmed live against the reference
tenant — "Iphone 16 Pro Max" and its four variants are **five rows in the same Products list**, each
with its own Code, prices, tax and account mappings, and the invoice line picker lists them flat
alongside every other product. So `ProductId` already means "the sellable, stockable thing", and the
FIFO ledger, the twelve `ProductId`-bearing entities, both composite indexes and all 25 report
handlers are **untouched**. The roadmap's phrasing — "the FIFO ledger keys extend from ProductId to
variant identity" — described a change that turned out not to be needed.

**The migration is purely additive**: five nullable columns on `catalog.Products`
(`Sku`, `Barcode`, `ParentProductId`, `HasVariants`, `CombinationKey`), three new tables, ten new
indexes, one self-referencing FK, four permission rows. **Zero operations touch
`StockLedgerEntries` or `StockMovements`, and there is not one `DropColumn`, `DropIndex` or
`DropTable` in `Up`.** Proven on real SQL Server against 1,089 live products and 17 live FIFO cost
layers: SHA-256 fingerprints of both stock tables are byte-identical before and after.

**One new rule, and it is the whole sweep**: a variant **parent** may never reach a document line.
The reference product *does* offer the parent in its picker; we refuse it, because a parent stock
bucket reconciles against nothing. Server-side that is four call sites
(`ProductVariantRules`); client-side it is **one line** — every one of the fifteen product pickers
in the app already went through `CatalogService.listAllProducts`. Both halves have a guard test that
reads the tree off disk and fails the build on a new bypass.

**Two new permission keys** (`Catalog.VariantAttribute.View` / `.Manage`), no feature flag, and
**no key for variants themselves** — creating a variant is creating a product, so it rides
`Catalog.Product.Manage`.

Tests: Domain **230** (+22), Application.UnitTests **540** (+45), Api.IntegrationTests **18**
(unchanged), Angular **119** (+14). `dotnet build` / `dotnet test` / `ng build` / `ng test` /
`tsc --noEmit` all clean.

---

## Step 2 — confirmed live (not inferred)

`erp-module-scan.md`'s Inventory §2–§3 were two-line data-model *sketches*, so a live pass was
mandatory. It changed the phase's central decision, which is exactly the Phase 8f lesson repeating.

| # | Question | What the live product actually does |
|---|---|---|
| 1 | Is the attribute catalog tenant-global or per-product? | **Tenant-global.** A flat list; 16 attributes in the UAT tenant (the scan said 11). Create form is `Name*` + a repeating "Variant Options" list and **nothing else**. |
| 1b | Are attribute names unique? | **No.** The tenant carries both `size` and `Size`, and both `Color` and `color`, as separate attributes. So the index on `(OrganizationId, Name)` is deliberately **not** unique. |
| 2 | How is a variant product created? | It is an **ordinary Product** with an "Attributes Used" pool and a "Variant Details" table. The modal is titled "New Variant **Product**". |
| 2b | **Is there a generation step at all?** | **No.** Variants are added **one at a time**. "Iphone 16 Pro Max" offers 4 colours × 3 sizes — 12 combinations — and carries exactly **4** variants. |
| 2c | What does the add-variant form contain? | `Name*`, auto-filled `Code*` (`P0597`, off the ordinary Product sequence), one **select per attribute** in the pool, `Selling Price*`, `Purchase Price`. No SKU or Barcode field. |
| 3 | **Does a document line show variants separately, or is there a second selector?** | **One flat picker.** Typing "Iphone 16" lists the parent *and* all four variants as siblings, each with its own code (`15`, `15001`, `16001`, `17001`, `18001`). **No second variant selector anywhere.** |
| 3b | Is the parent selectable on a line? | **Yes, in the reference product.** We deliberately diverge — see Decision A. |
| 4 | Is stock per variant or per product? | Variants are Products, so Inventory Position keys on `Code/Goods` and a variant is its own row automatically. (The UAT variants carry no stock, so this is inferred from the identity model rather than observed with numbers — the one thing on this list not directly seen.) |
| 5 | The three screens outstanding from 21b/21c/22 | **Not done.** Out of Phase 24's scope and skipped deliberately to spend the budget on this phase; still outstanding. |

**The decisive observation** was the Products list: searching "Iphone 16 Pro Max" returns **5 rows**
— the parent and its four variants — in the ordinary Products screen. "Variant Products" is that
same list under a filter, not a separate entity.

---

## Decision A — what a variant *is*

**A variant is a Product, with a parent pointer. The stock key does not change.**

The three shapes considered:

1. **A variant is a Product** (chosen). `ProductId` already means "the sellable thing".
2. **A variant is a child, stock keys on `(ProductId, VariantId)`.** The roadmap's assumption.
3. **A variant is a child, `VariantId` alone is the stock key** (nullable in the FIFO hot path).

Option 2 was the plan until the live pass. It costs: a nullable column on **twelve** entities, a
rebuild of both `{OrganizationId, ProductId, WarehouseId, TransactionDate}` composite indexes **on
live FIFO cost layers**, a two-column comparison in 25 query handlers, a grouping decision per
report, and a conversion-cap key change. Option 1 costs **none of that** — and it is what the
reference product actually does, so it is not a simplification away from the target, it *is* the
target.

Option 3 was rejected outright: a nullable key in the hot path of FIFO consumption, for no benefit
over option 2.

### The three roles a `Product` can now play

| Role | `ParentProductId` | `HasVariants` | `CombinationKey` | Transactable? |
|---|---|---|---|---|
| Ordinary product | `NULL` | `false` | `NULL` | **Yes** |
| Variant parent | `NULL` | `true` | `NULL` | **No** |
| Variant child | set | `false` | set | **Yes** |

**What a non-variant product's rows look like: exactly what they looked like before.** Every one of
the 1,089 products in the dev database is row 1 of that table — `ParentProductId NULL`,
`HasVariants 0`, `CombinationKey NULL` — which is the correct state, reached with no backfill.
Every query path for such a product is byte-identical to Phase 23's.

### The deliberate divergence: a parent is not transactable

The live product offers the parent in its line picker. **We refuse it.** Selling "T-Shirt" when the
sellable things are "T-Shirt / L / Blue" and "T-Shirt / XL / Red" creates a fourth stock bucket that
nothing ever receives into — Stock Position would carry a parent balance reconciling against
nothing while every total still added up. That is precisely the failure the roadmap's exit criterion
exists to catch, so copying the reference product here would have been copying a defect.

Enforced in `ProductVariantRules`, and proven both ways in E2E: `409 'T-Shirt' has variants, so it
cannot be used on a document line directly` for the parent, `201` for its variant.

---

## Decision B — the migration

`20260902065826_Phase24VariantProducts`. **Additive only.**

- 5 × `AddColumn` on `catalog.Products` — `Sku`/`Barcode`/`CombinationKey` nullable strings,
  `ParentProductId` nullable `uniqueidentifier`, `HasVariants bit NOT NULL DEFAULT 0`.
- 4 × `CreateTable` (`VariantAttributes`, `VariantAttributeOptions`,
  `ProductVariantAttributeUsages`, `ProductVariantValues`).
- 1 × `InsertData` (four `RolePermission` rows).
- 10 × `CreateIndex`, 1 × `AddForeignKey` (the `Products` self-reference, `Restrict`).

**The safety argument, in three parts:**

1. **The ordering hazard cannot apply.** CLAUDE.md's gotcha is that `dotnet ef` orders by model diff
   rather than data safety, so a drop can precede the add that replaces it. Here there is **not a
   single drop of any kind in `Up`** — verified by grep, not by reading alone. Nothing to reorder.
2. **Nothing touches the stock tables.** Grepping `Up` for `StockLedger|StockMovement|DropColumn|
   DropIndex|DropTable` returns **0**. The two composite indexes on live FIFO layers are not
   rebuilt, because the stock key did not change.
3. **Verified on real SQL Server, seeded before and checked after** — the InMemory provider enforces
   neither unique indexes nor filtered indexes, so this could not be proven in unit tests:

   | | Before | After |
   |---|---|---|
   | `StockLedgerEntries` SHA-256 fingerprint | `5E5153DC…27AF` | `5E5153DC…27AF` |
   | `StockMovements` SHA-256 fingerprint | `C7A1A6D2…1E7D` | `C7A1A6D2…1E7D` |
   | `SUM(QuantityRemaining)` | `398.0000` | `398.0000` |
   | Products | 1,089 | 1,089 (all `HasVariants=0`, `ParentProductId NULL`) |

One detail worth naming: the unique index
`(OrganizationId, ParentProductId, CombinationKey)` carries
`HasFilter("[ParentProductId] IS NOT NULL")`. Without the filter every ordinary product would
collide, because **SQL Server's unique indexes treat NULLs as equal to one another** — the opposite
of the SQL standard. Also checked: `HasVariants` has no `HasDefaultValue` in the model snapshot, so
the enum/bool default-substitution gotcha (phase-2 bug #2) does not apply — EF always writes the
explicit value; the `defaultValue: false` exists only to fill existing rows.

---

## Decision C — attributes, options, and the generation step

**The catalog is tenant-global** (`VariantAttribute` + `VariantAttributeOption`), confirmed live. A
product then selects a **subset** of it as its "Attributes Used" pool
(`ProductVariantAttributeUsage`), and each variant child records its own combination
(`ProductVariantValue`). The pool is deliberately *not* the set of variants — the live tenant
offers 12 combinations and carries 4.

**Removing an option splits into two different questions with two different answers:**

- **Retiring a catalog option is always allowed, and is purely forward-looking.** It stops being
  offered on new variants; every existing variant, its stock and its history stay intact and
  readable. Options are never hard-deleted. Renaming is always allowed too — identity is the row id,
  not the text, so existing variants simply display the corrected label.
- **Dropping an option from a *product's* pool is refused while one of that product's variants is
  built from it** (409 naming the variant). That would leave a child built from something its own
  parent no longer offers. Clearing the pool entirely while variants exist is refused for the same
  reason.

**Generation exists even though the reference product has none.** FR-8.3 says variants are
"generated from reusable, tenant-defined attribute definitions" and the roadmap's exit criterion is
"a two-attribute product generates its variant matrix", so both affordances ship: `+ Add` (one at a
time, matching live) and `Generate All` (the cartesian product). Both funnel through
`Product.CreateVariant`, so they cannot drift apart.

**The cap is 200 combinations per run, and overshooting is refused, never truncated.**
4 attributes × 5 options is 625 rows from one click, and every row is a real `Product` consuming a
document number off the tenant's own sequence. A silent partial matrix is the worst available
outcome — the user cannot tell which combinations are missing — so the error names the number
(`would generate 625 variants, more than the 200 allowed in one run`).

**Re-running generation is safe and is the point.** An existing combination is *skipped*, not
failed, so "add a fifth colour, generate again" fills only the new rows. The idempotency comes from
the `CombinationKey` unique index rather than from the caller being careful — the same
let-the-index-be-the-mechanism idiom as `AlertSendLog` (phase-20e) and `ImportJobRow` (phase-21a).
Verified live: generate → `created: 4, skipped: 0`; generate again → `created: 0, skipped: 4`.

---

## Decision D — how the sweep got done, and how a reader can verify it

**The sweep is complete, and it is far smaller than the brief assumed — because Decision A removed
most of it.** What remained is one rule in two places.

### Server side: four call sites

`ProductVariantRules.EnsureProductsExistAndAreTransactableAsync` folded into the three module
validation helpers (`SalesValidation`, `PurchasingValidation`, `InventoryValidation` —
between them every Quotation, SalesOrder, Invoice, CreditNote, PurchaseOrder, PurchaseBill,
DebitNote, WarehouseTransfer and InventoryAdjustment line, create and update), plus
`CreateOrUpdateOpeningStockLine`, which reads its single product directly rather than through a
helper.

**How to verify the claim:** `ProductVariantSweepGuardTests` reads every `*CommandHandler.cs` in
`src/Application` off disk at test time, finds the ones taking product ids from their own request,
and fails the build on any that does not route through the rule. It currently sees **160 handlers,
19 in scope, 19 passing**, with a 6-entry allow-list that each carry their reason. It has the same
two self-checks as phase-23's `sweep-guard.spec.ts`: that the scan found files at all (so a broken
path cannot make it pass vacuously), and that every exemption still names a real file.

### Client side: one line

Finding 5 of the kickoff said the 14 pickers had "no seam to hide behind". There is one:
**all fifteen** product pickers and report filters call `CatalogService.listAllProducts`. Defaulting
that single method to `variantFilter: 'Transactable'` makes every one of them variant-aware at once
— variant children appear as ordinary flat entries (exactly the live shape), parents disappear.

The report *filter* pages get the same list for a different reason: a parent has no stock and no
transactions, so offering one could only ever produce an empty report.

**How to verify:** `catalog.service.spec.ts` pins the wire format (`variantFilter=Transactable` on
`listAllProducts`, absent on `listProducts`) and then globs every `features/**/*.ts` off disk,
failing the build on any component calling `listProducts` directly or fetching `/products` over
`HttpClient` — with one reasoned exemption (the Products screen itself, which is paginated and has
its own user-facing variant filter).

---

## Decision E — what a variant does to existing documents

**The conversion cap needed no change, and that is a finding rather than an omission.**

Phase 6's bug #4 caps a CreditNote against the source Invoice's remaining quantity per exact
`(ProductId, Rate, VatRate, DiscountPct)` line — a *quadruple*, not the triple the kickoff recalled.
The Phase 24 worry was that two variants sharing a Rate and VatRate collapse into one bucket, so a
return of Large-Blue could be satisfied out of Large-Red's quantity. **They do not, because two
variants are two `ProductId`s** — the existing key already discriminates them.

That is exactly the kind of "the key already covers it" claim that is true until the key changes, so
it is asserted rather than reasoned about. `VariantConversionCapTests` invoices 3 Blue and 5 Red at
**identical** Rate and VatRate and proves: returning 4 Blue fails (`only 3 remains un-credited`)
even though 8 units at that exact key were invoiced; each variant can be returned up to its own
quantity; exhausting Blue leaves Red untouched; and the parent — which shares the Rate and VatRate
too — is refused twice over.

**The three pre-transaction line types** (`QuotationLine`, `SalesOrderLine`, `PurchaseOrderLine`)
needed nothing either. They carry `ProductId`, a variant is a Product, so a Quotation for
"Large Blue" converts to an Invoice for "Large Blue" with no change. The kickoff's 9/3/1 split
dissolves under Decision A.

---

## Decision F — feature gating and permissions

**No feature flag, and the reason is stated rather than left to inference.** `TenantFeature`'s seven
members are captured once at Organization creation and are **immutable thereafter** (Phase 22's
Decision C), so a `Variants` flag could never be granted to an existing tenant. It would also be the
wrong shape: having variants is a property of one Product, not a tenant entitlement, and a tenant
that never creates one already sees nothing. This is not phase-20f's `MultipleWarehouses` situation
— there is nothing a flag-off tenant would be unable to do.

**Exactly one new key pair**, and it is the attribute catalog, not variants:

| Key | Admin | Member | Derivation |
|---|---|---|---|
| `Catalog.VariantAttribute.View` | ✔ | ✔ | A Member building a variant must see which attributes exist, or the form is unusable. |
| `Catalog.VariantAttribute.Manage` | ✔ | ✘ | Taxonomy/control-plane — the exact `ProductCategory` / `UnitOfMeasurement` split: a tenant-wide named list, curated rarely, read constantly. |

**Variants themselves take no key at all.** Creating, editing and deleting a variant *is* creating,
editing and deleting a product, so they ride `Catalog.Product.Manage` unchanged. A second key would
be a weaker gate standing in front of the real one — the same reasoning that left Phase 22's inbox
conversion keyless. Proven in E2E: a custom role holding every key **except** the two above gets
`403 … (Catalog.VariantAttribute.Manage)` on the attribute editor while still reading a product's
four variants with a `200`.

---

## Decision G — reporting

**Every report groups by product, and under Decision A that already means "by variant".** No report
handler changed, and none needed to: a variant is a `Product` row with its own `Code` and `Name`, so
`ProductStockPositionQuery`, `InventoryLedgerQuery`, `StockAgeingQuery`,
`ProductProfitabilityQuery` and both Master reports each show one row per variant automatically —
which is what the live product's Inventory Position does (it keys on `Code/Goods`, and every variant
has its own code).

**A non-variant tenant sees no change whatsoever.** No report gained a column, a grouping toggle, or
a row. That was the alternative worth naming and rejecting: a user-switchable product/variant
grouping would have doubled six report screens' surface area to express something the identity model
already expresses. "A report that silently changes its grouping is a support ticket; one that
doubles its rows without warning is worse" — under Decision A neither happens, because nothing about
the reports changed.

The one visible affordance added is on the **Products list**: a `Variant Product` / `Variant` badge
per row and an `All` / `Variant Products` / `Sellable Only` filter, so a user can tell the three
roles apart. `Variant Products` is the live reference product's own sub-module, expressed as a
filter over the same list because that is what it actually is.

---

## Explicitly out of scope (decided, not forgotten)

- **Multi-UOM × variants.** `ProductSecondaryUnit` already exists with 18 Application-layer
  references, and the interaction (a variant with its own secondary-unit conversion rates and
  prices) is a genuine combinatorial design question nobody has posed. A secondary unit is catalog
  metadata, not a stock or document line, so attaching one to a parent moves nothing and reconciles
  against nothing — it is on the sweep guard's allow-list with that reason.
- **Bulk-importing variants** via Phase 21a's `ProductImporter`. Its column set has no attribute
  columns and no combination concept; adding them is a template-design task of its own.
- **Product-level SKU/Barcode as a distinct concept.** The scan disagrees with itself here (line 211
  carries `sku_id`/`barcodes` at product level, line 216 per variant). Under Decision A the question
  dissolves: `Sku` and `Barcode` sit on `Product`, so every variant has its own, which is exactly
  what FR-8.3 asks for, and ordinary products get them too.
- **The three screens outstanding from 21b/21c/22** (`Configurations > Import / Export`,
  `Organization > Developer Mode`, `Organization > Documents`) remain outstanding.

---

## Testing

### The exit criterion, verified with `sqlcmd` against real SQL Server

Fresh Organization (`Phase24 Variants Ltd`), master data seeded by curl + cookie jar. A parent
`T-Shirt` offering Size{Small,Large} × Colour{Blue,Red}; `Generate All` produced **4 variants**; a
plain `Plain Mug` alongside as the non-variant control.

PurchaseBill 10 Blue-Large @600 + 7 Red-Large @610 + 5 Mug @150 → approved. A second PurchaseBill
10 Blue-Large **@800** → approved. Invoice 4 Blue-Large + 2 Mug → approved.

```
Product              Code  Role      QtyIn    QtyOnHand
T-Shirt              0001  PARENT     .0000       .0000      <- nothing can ever land here
Plain Mug            0002  plain     5.0000      3.0000      <- non-variant control, unchanged
T-Shirt Blue Large   0006  variant  20.0000     16.0000
T-Shirt Blue Small   0005  variant    .0000       .0000
T-Shirt Red Large    0004  variant   7.0000      7.0000      <- ZERO movement
T-Shirt Red Small    0003  variant    .0000       .0000
```

**FIFO layers for Blue-Large** — the criterion as literally worded ("10 received, 4 invoiced, 6
left"), plus the cost proof:

```
2026-02-01   QtyIn 10   Remaining  6   UnitCost 600   <- exactly 6 left on the first layer
2026-02-10   QtyIn 10   Remaining 10   UnitCost 800   <- newer layer untouched
```

`InvoiceLine.CogsUnitCost = 600.0000` — the **oldest** layer's cost, not the newer 800 and not the
sibling's 610. The kardex reconciles per variant (Blue-Large: In 10, In 10, Out 4; Red-Large: In 7
and nothing else; parent: no rows). **Trial Balance balances at 26,690.60 both sides**, and every
figure cross-checks by hand: Inventory 16,320 = 19,020 received − 2,700 COGS; Sales 4,600;
AR 5,198 = 4,600 × 1.13; AP 21,492.60 = 19,020 × 1.13.

### Negative permission proof

Admin is a system role whose grants cannot be edited (409), so a custom role holding every key
**except** the two new ones was created and the membership moved onto it:

```
PUT /variant-attributes/<nonexistent> -> 403  "…(Catalog.VariantAttribute.Manage)."
GET /variant-attributes               -> 403  "…(Catalog.VariantAttribute.View)."
GET /products/<parent>/variants       -> 200  4 variants   <- rides Catalog.Product.*, Decision F
restored to Admin, same nonexistent id -> 404 "Variant attribute not found."
```

The 403-then-404 pair against the **same** id is what proves `AuthorizationBehavior` fired before
the handler ever looked the record up.

### Suites

| Suite | Before | After |
|---|---|---|
| Domain.UnitTests | 208 | **230** |
| Application.UnitTests | 495 | **540** |
| Api.IntegrationTests | 18 | **18** |
| Angular | 105 | **119** |

The non-variant regression is the one that matters, and it is covered three ways: the full existing
suites pass unchanged; `Variants_and_ordinary_products_are_both_accepted` asserts the rule lets
ordinary products through; and the E2E's `Plain Mug` went through a full PurchaseBill → Invoice
cycle inside the same documents as the variants, producing identical stock and GL behaviour.

---

## Bugs hit and fixed

### 1. A new child on a *tracked* parent's encapsulated collection is tracked as `Modified`, not `Added`

`SetProductVariantAttributes` died with
`DbUpdateConcurrencyException: Attempted to update or delete an entity that does not exist in the
store` on its first save. The change tracker showed
`ProductVariantAttributeUsage:Modified` for a row that had just been constructed.

This is the same family as CLAUDE.md's phase-4 bug #1, but reached from the other direction: that
one was a *clear-and-re-add*, this is an **add-only** change. The cause is that the child's key is
already set by its factory, so `DetectChanges`, finding it on a parent that is itself `Modified`,
propagates that state rather than inferring `Added`.

Fix: `Product.SetVariantAttributeUsages` now returns a `VariantUsageChanges(Added, Removed)` record
and the handler calls `db.ProductVariantAttributeUsages.AddRange/RemoveRange` explicitly — CLAUDE.md's
documented remedy, applied to the add path. Note the diff-don't-replace shape was already correct
and was **not** the problem; a Domain test (`SetVariantAttributeUsages_diffs_rather_than_clearing_and_re_adding`)
pins it by asserting surviving rows are the **same instances**.

### 2. `TestAppDbContext` needs every encapsulated collection restated

The InMemory test context deliberately has no `ApplyConfigurationsFromAssembly`, so all three new
encapsulated collections (`VariantAttribute.Options`, `Product.VariantAttributeUsages`,
`Product.VariantValues`) fell back to convention and mis-mapped. The symptom was the *same*
`DbUpdateConcurrencyException` as bug #1, which is what made bug #1 take two passes to isolate —
worth knowing that these two distinct causes present identically.

### 3. `UpdateRolePermissions` takes a dictionary, not an array

E2E-only: the payload is `IReadOnlyDictionary<string, bool>`, and sending an array of
`{permissionKey, isGranted}` objects yields a bare
`400 Failed to read parameter … as JSON` that names nothing useful. Also: **Admin is a system role
and its grants cannot be edited** (409) — a negative permission proof must go through a custom role.

---

## Files of note

- `src/Domain/Catalog/Product.cs` — the three roles, `CreateVariant`, `BuildCombinationKey`,
  `SetVariantAttributeUsages` + `VariantUsageChanges`.
- `src/Application/Catalog/Variants/ProductVariantRules.cs` — the one rule; its doc comment names
  all four call sites.
- `tests/Application.UnitTests/Catalog/ProductVariantSweepGuardTests.cs` — the mechanical proof of
  server-side completeness.
- `web/src/app/core/catalog/catalog.service.spec.ts` — the mechanical proof of client-side
  completeness.
- `web/src/app/features/catalog/product-variant-panel/` — Attributes Used + Variant Details.
- `web/src/app/features/catalog/variant-attribute-list-page/` — the tenant-global catalog.
