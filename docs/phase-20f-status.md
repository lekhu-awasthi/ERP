# Phase 20f — Tenant feature-flag enforcement (FR-2.6)

## TL;DR

`TenantSubscription`'s seven Accounting Feature flags — written once at Organization creation since
Phase 1b and read **nowhere** in the twelve phases since — are now a real gate. A fourth MediatR
pipeline behavior, `FeatureGateBehavior`, keyed by a new `IRequireFeature` marker interface, sits
between `AuthorizationBehavior` and `LockDateBehavior` and rejects a request whose tenant never
opted into the feature it needs, with a new `FeatureNotEnabledException` → **HTTP 403** naming the
feature in the wizard's own words.

**The Step 1 investigation found only 2 of the 7 flags have a real surface in this codebase to
gate** — `TrackInventory` and `MultipleWarehouses`. The other five have nothing built to gate, and
that includes *both* examples FR-2.6 itself gives (Multi-Currency exchange rates, Manufacturing
BOM/Production screens). Scope was reduced to what is real rather than padded to match the FR's
illustrations. See Decision #1.

`TrackInventory` gates the Inventory bounded context (WarehouseTransfer, InventoryAdjustment,
Opening Stock, Stock Position, Inventory Ledger — 16 requests). `MultipleWarehouses` additionally
gates WarehouseTransfer (the only two-feature request here) and caps warehouse creation at one —
a *conditional* gate that cannot ride the pipeline, so it lives in `CreateWarehouseCommandHandler`.
See Decision #4, the single most load-bearing design call in this phase.

Also shipped: `GetTenantSubscriptionQuery` plus a read-only Angular **Subscription & Features** page
mirroring the reference product's own two screens, and feature-conditional rendering of the three
Inventory nav entries on the Organization dashboard.

Tests: Domain.UnitTests 159 (+16), Application.UnitTests 319 (+13), Angular 7 specs (unchanged).
`dotnet build` / `ng build` / `tsc --noEmit` clean. Manual E2E against **three** freshly created
Organizations covering all three meaningful flag combinations.

---

## Step 1 — the scope investigation (code-grounded)

`grep -rn "TenantSubscription" src --include=*.cs`, excluding migrations and EF configuration,
returns exactly one functional consumer: `CreateOrganizationCommandHandler.cs:49`, the write. The
premise held — this was ambient, unread state.

For each flag, whether a real, already-built consuming surface exists here:

| Flag | Real surface? | Evidence |
|---|---|---|
| **TrackInventory** | **Yes — the largest** | The whole Inventory context: WarehouseTransfer and InventoryAdjustment (Create/Update/Approve/Void), Opening Stock lines, Stock Position, Inventory Ledger (kardex) |
| **MultipleWarehouses** | **Yes** | `Warehouse` lookup, `CreateWarehouseCommand`/`UpdateWarehouseCommand`, the generic List/Delete pair, `warehouse-list-page`, and every document type's Warehouse picker |
| MultipleLocations | No | Zero `BillingLocation` anywhere. (A `find -iname "*location*"` hit on `PaymentAllocation.cs` is a false positive — the word "allocation" contains "location"; `grep BillingLocation` returns nothing.) |
| MultiCurrency | No | Zero `Currency` domain class. FR-2.5's "manage the tenant's active Currency list" screen does not exist. |
| Manufacturing | No | No BOM / Production Order / Production Journal — those are Phase 25. The only adjacent artifact is Phase 20c's `CostTerm.ProductionCost` category, deliberately unconsumed reference data. |
| PosRetail / PosRestaurant | No | Out of the entire rebuild's scope per `erp-module-scan.md` line 5's recorded scope decision, not merely unbuilt. |

**Both of FR-2.6's own worked examples are unbuildable in this codebase today.** The FR reads
"a tenant without Multi-Currency enabled should not be prompted for exchange rates; a tenant
without Manufacturing enabled should not see BOM/Production screens" — there are no exchange-rate
prompts and no BOM/Production screens to hide. Neither was invented to make the sweep feel
complete. The `TenantFeature` enum covers all seven so Phase 25's Manufacturing gate is a one-line
`RequiredFeatures` declaration with no new infrastructure.

## Step 2 — confirm-live findings (Moonbeam UAT tenant)

Three findings, one of which materially reshaped the plan.

**1. The entitlement flags are read-only to the tenant — confirmed directly, not inferred.**
`Configurations > Tigg Subscriptions` renders them as plain read-only rows with no edit control:
`Location Enabled: No` / `Warehouse Enabled: Yes` / `IRD Verified: No` / `IRD Sync Enabled: No` —
exactly the data model `erp-module-scan.md:383` recorded. On `Configurations > Organization >
Features`, the *disabled* feature (Billing Location) carries a static grey **"Disabled"** pill and
a banner reading "Billing Location feature is currently inactive on your account. To activate this
feature, please reach out to Tigg Support." `read_page --filter interactive` found exactly **one**
`switch` element on that entire page, and it belongs to Multiple Currency — not to Billing Location
or Multiple Warehouse.

→ **Immutable-at-creation is correct here and is not a divergence from the reference product.** In
Tigg these are vendor-controlled; this codebase has no vendor-support channel, so there is nothing
to build an Update path *for*. No `TenantSubscription` mutator was added. (Multi-Currency's toggle
is genuinely self-service there, but Multi-Currency is not a subscription entitlement in Tigg at
all — and we have no Currency entity, so it is moot twice over.)

**2. Enforcement shape is shown-but-disabled, not hidden.** Billing Location's panel still renders
with heading, description, status pill and explanatory banner; only its Add button and list are
suppressed. The new Subscription & Features page follows this rather than omitting disabled rows.

**3. `Track Inventory` cannot gate "the Inventory module".** Tigg's Inventory nav is: Products,
Variant Products, Variant Attributes, Product Category, Units Of Measurement, Warehouse Transfer,
Inventory Adjustment, Bills Of Materials, Production Order, Production Journal. **The product
catalog lives under Inventory.** Hiding that nav wholesale would hide Products, which every tenant
needs. This codebase already splits the two correctly — Catalog vs Inventory bounded contexts — so
the gate lands on Inventory only and Catalog is untouched. Note also that Warehouse management is
*not* in that nav at all; it lives under Organization > Features, which is where we gate it.

There is no post-creation Track Inventory control anywhere in the reference product: not on the
Features tab, and not under `Apps > General` (checked: Suggest Selling Price, Product Price Basis,
Negative Cash/Item Balance, Credit Limit, VAT account mapping — nothing inventory-toggling).

---

## Scope decisions

### 1. Ship the two flags that have real surfaces; do not invent the other five

2 of 7 was the outcome, and the phase was sized to it rather than expanded into
Manufacturing/POS/Currency to make the sweep look complete. This is the same
"propose a reduced scope explicitly" discipline `phase-20d-status.md` used, running the opposite
direction: there the assumed scope was *bigger* than reality, here the FR's own examples are.

### 2. `TenantFeature` is an enum, not a string constant catalog

`PermissionKeys` are strings because each key is *persisted* as a `RolePermission` row — the key
itself is data. A feature flag is a fixed column on `TenantSubscription`; nothing persists its
name. So there is no reason to give up compile-time checking. `TenantSubscription.IsEnabled(TenantFeature)`
is the single place the enum maps back onto the columns, so the behavior and the read-only query
cannot disagree about which column a feature means.

`IrdSyncEnabled` deliberately has **no** enum member: it can never be enabled at creation (no IRD
e-filing integration is designed), so nothing could gate on it. It still surfaces on the read-only
screen.

### 3. `IRequireFeature` exposes a *collection*, not a single feature

`IReadOnlyCollection<TenantFeature> RequiredFeatures`, because WarehouseTransfer genuinely needs
two: the inventory tracking that gives a stock movement meaning, and more than one warehouse to
move it between. Every other gated request today declares exactly one. A single-feature interface
would have forced either a second marker interface or an artificial choice about which of the two
WarehouseTransfer "really" needs.

### 4. `MultipleWarehouses` is a **cap at one**, not an on/off block — and it lives in the handler

**The most consequential decision in this phase.** `CreateOrganizationCommandHandler` seeds
`TenantSettings`, `TenantSubscription` and the Admin membership — but **no default Warehouse**. And
Invoice and PurchaseBill both *require* a `WarehouseId`. So gating `CreateWarehouseCommand`
outright when the flag is off would leave such a tenant permanently unable to raise an invoice.

The rule is therefore: **the second warehouse is what the entitlement buys.** A flag-off tenant
creates exactly one and is then capped. This also matches the reference product, which calls the
feature "Multiple Warehouse" and carries it as `warehouseEnabled`.

Two consequences worth recording:
- It is a *conditional* gate ("reject only if one already exists"), which a marker-interface
  pipeline behavior cannot express, so it sits in
  `CreateWarehouseCommandHandler.EnforceWarehouseEntitlementAsync` rather than riding
  `FeatureGateBehavior`. This is the one deliberate exception to the one-behavior rule.
- Stating it as a cap rather than a block means Organizations created before this phase — which
  have zero warehouses — can still create their first. **No backfill migration is needed.**

The alternative (seed a default warehouse at Organization creation, then block all creation when
the flag is off) was rejected: it changes `CreateOrganizationCommandHandler` for every tenant to
buy nothing the cap does not already deliver.

`UpdateWarehouseCommand` is **not** gated — a flag-off tenant must still be able to rename or
deactivate its single warehouse.

### 5. `FeatureNotEnabledException` maps to HTTP 403

Chosen over 409 and 422 (user decision, taken explicitly rather than defaulted). 403 sits beside
`ForbiddenException` with the same honest semantics: the request is understood and well-formed and
authorization is refused — it just turns on the Organization's entitlements rather than the acting
user's role. The two are told apart by message: a feature 403 always names the feature. 409 was the
runner-up (a tenant-configuration state conflict, and cleanly separable client-side without reading
message text); 422 was rejected because in this codebase it already means the warn-and-override
path (`StockAvailabilityWarningException`), which this is not.

### 6. Pipeline position: after `AuthorizationBehavior`, before `LockDateBehavior`

A caller with no permission on the document type gets a permission 403 first and learns nothing
about the tenant's entitlements; a request blocked by an entitlement never reaches the LockDate
lookup. **Proven live**, not just reasoned — see the E2E ordering check below.

### 7. Per-request DB read, no cache

`FeatureGateBehavior` reads the `TenantSubscription` row per invocation. `AuthorizationBehavior`
already does a per-request permission query and `LockDateBehavior` another; a third single-row
lookup by an indexed `OrganizationId` is consistent with that and cheaper than reasoning about
invalidation. Considered and rejected: `IMemoryCache`. The flags are immutable after creation, so a
cache would in fact be safe — but it would be the first caching layer in this codebase, and adding
one for a single-row lookup that no profiling has flagged is complexity without evidence. Revisit
if a real hot path appears.

### 8. `Tenancy.Subscription.View` is granted to **Admin and Member**

A View key with no Manage counterpart, because there is nothing to manage (confirm-live finding #1).
Admin+Member departs from the Admin-only bar Phase 20d set for control-plane keys, for a concrete
reason rather than by analogy: the Angular shell reads this query to decide which feature-gated nav
entries to render, so *every* role needs it or a Member's nav silently shows Inventory links that
then 403. It also exposes nothing sensitive — plan name, trial dates, seven booleans; no PAN, no
contact identity, no per-transaction data — putting it in the "bounded, routine" half of this
codebase's permission-derivation rule.

### 9. Stock *reports* stay ungated; the FIFO/GL engine is never gated

User decision. Stock Ageing and Product Profitability live in the Reports context and read the FIFO
ledger, which keeps running regardless — Invoice and PurchaseBill approval still decrement stock and
post COGS for Goods lines whatever the flag says. Gating those reports would hide real data the
tenant's own GL reflects. And gating the engine itself was never on the table: it would break GL.
**The gate covers Inventory screens and document types, never the posting engine.** Verified live —
see the E2E section.

---

## What shipped

**Domain**
- `TenantFeature` enum (7 members, one per `AccountingFeatureSelections` field, same order).
- `TenantSubscription.IsEnabled(TenantFeature)` — the single enum-to-column map.

**Application**
- `Common/Security/IRequireFeature.cs` — `IReadOnlyCollection<TenantFeature> RequiredFeatures`.
- `Common/Exceptions/FeatureNotEnabledException.cs`.
- `Common/Behaviors/FeatureGateBehavior.cs`, registered between Authorization and LockDate.
- 16 requests gained `IRequireFeature`: 10 declaring `TrackInventory` (InventoryAdjustment
  Create/Update/Approve/Void plus Get/List, OpeningStockLine Create-or-Update plus List,
  StockPosition, InventoryLedger) and 6 declaring both `TrackInventory` and `MultipleWarehouses`
  (WarehouseTransfer Create/Update/Approve/Void plus Get/List).
- `CreateWarehouseCommandHandler.EnforceWarehouseEntitlementAsync` — the cap (Decision #4).
- `Tenancy/Queries/GetTenantSubscription/` — query plus handler; the per-feature display name and
  description come verbatim from the wizard's Step 2 cards, so the read-only screen names each
  entitlement the way the user saw it when choosing.
- `PermissionKeys.SubscriptionView`.

**Infrastructure / Api**
- `RolePermissionConfiguration`: two seed rows (Admin and Member, both granted), ids `...0131`/`...0132`.
- Migration `20260831150827_Phase20fSubscriptionViewPermission` — two `InsertData` rows, nothing
  else in the diff. Applied to the local dev database with a plain `dotnet ef database update`.
- `ExceptionHandling.cs`: `FeatureNotEnabledException` maps to 403.
- `GET /api/organizations/{id}/subscription`. **No PUT counterpart, by design.**

**Angular**
- `TenantFeatureKey` / `TenantFeatureState` / `TenantSubscription` models; `getSubscription()`.
  No update request shape exists, matching the immutability decision.
- `subscription-features-page` plus route `organizations/:id/features`.
- Organization dashboard: loads the subscription, `hasFeature()` gates the three Inventory nav
  entries (Warehouse Transfers requires both flags, exactly as its command declares). **Fails
  closed** — if the subscription cannot be loaded, `hasFeature()` returns false and gated links stay
  hidden rather than leading to a 403.

**Tests** — Domain 143 to 159, Application 306 to 319.
- `TenantSubscriptionTests`: per-feature theories for all-off / all-on, a distinct-flags test that
  catches a switch arm reading the wrong column, and the out-of-range throw.
- `FeatureGateBehaviorTests` (7): allow, reject-naming-the-feature, fail-closed on a missing
  subscription row, org isolation, skip for an ungated request, **every declared feature required
  not just the first**, and the loud `InvalidOperationException` for an `IRequireFeature` request
  that is not `IOrganizationScoped`.
- `TenantFeatureEnforcementTests` (6): the four warehouse-cap cases plus the subscription query,
  all seeded through the real `CreateOrganizationCommandHandler`.
- `TestSupport/TenantFeatureSeed.cs` — seeds through `TenantSubscription.CreateTrial`, never by
  hand-constructing the entity, so a test cannot depend on a flag combination the real wizard could
  not produce.

Three pre-existing tests needed a seeded subscription because they create a second warehouse
(`TransactionApprovalQueryHandlerTests`, `SalesMasterReportQueryHandlerTests`,
`PurchaseMasterReportQueryHandlerTests`). That was the cap working, not a regression.

---

## Manual E2E

Three Organizations created through the real `CreateOrganizationCommand` (never by hand-editing
`TenantSubscription` rows), covering every meaningful combination:

| Org | TrackInventory | MultipleWarehouses |
|---|---|---|
| A `P20f allon` | on | on |
| B `P20f alloff` | off | off |
| C `P20f trackonly` | **on** | **off** |

**Feature gate** — each 403's message names the feature:

| Surface | A | C | B |
|---|---|---|---|
| `inventory/stock-position` | 200 | — | 403 *Track Inventory* |
| `inventory-adjustments` | 200 | — | 403 *Track Inventory* |
| `inventory/ledger` | 200 | — | 403 *Track Inventory* |
| `opening-balances/products` | 200 | — | 403 *Track Inventory* |
| `warehouse-transfers` | 200 | **403 *Multiple Warehouses*** | 403 *Track Inventory* |

Org C is the one that matters: `TrackInventory` is satisfied and `MultipleWarehouses` is not, so a
behavior that stopped at the first satisfied feature would have let it through. It names the
*second* feature.

**Warehouse cap** — first warehouse succeeds (201) on all three orgs, including both flag-off ones;
second succeeds only on A. `sqlcmd` confirms final counts 2 / 1 / 1.

**Ordering** — a nonexistent org id on a *feature-gated* route returns the **permission** 403
(`Inventory.InventoryLedger.View`), never a feature 403, proving `AuthorizationBehavior` runs first
(403-not-404 also proves the check fired before the handler). The same request unauthenticated
returns 401. Org-membership negative on the new endpoint: 403 naming `Tenancy.Subscription.View`.

**Nothing collateral broke.** On Org B — Track Inventory **off** — a real Invoice was created and
approved end-to-end through its single warehouse, posting a balanced entry (AR 2260 Dr / Sales 2000
Cr / VAT Payable 260 Cr, verified in `accounting.GlLines` via `sqlcmd`). This is the live proof of
Decision #9: the gate covers Inventory screens and document types, never the posting engine.

**Persisted state** (`sqlcmd`) — all three subscriptions carry their distinct flag combinations
through the real command path; both `Tenancy.Subscription.View` seed rows present and granted.

**Browser** (dev server plus API, all three orgs):
- Org A dashboard: Warehouse Transfers, Inventory Adjustments, Stock Position all present.
- Org C dashboard: Inventory Adjustments and Stock Position present, **Warehouse Transfers absent**
  — the two-feature case renders correctly. Switching orgs re-derived the flags correctly (the
  `paramMap` subscription handles the Phase 3 route-reuse class of bug).
- Org B dashboard: all three absent; the ungated Warehouses link correctly still present.
- Features page, Org B: "Features enabled 0 of 7", every card showing a grey Disabled pill plus the
  explanatory note — the reference product's shown-but-disabled shape.
- Features page, Org A: "2 of 7", Track Inventory and Multiple Warehouses with green Enabled pills
  and no note, the rest disabled.

---

## Known limitations / follow-ups

- **Five flags are gated by nothing**, because nothing exists to gate. `MultipleLocations` needs a
  `BillingLocation` aggregate; `MultiCurrency` needs FR-2.5's Currency list; `Manufacturing` waits
  on Phase 25; POS Retail/Restaurant are out of the rebuild's scope entirely. Each becomes a
  one-line `RequiredFeatures` declaration when its surface lands — the mechanism is done.
- **Only the Organization dashboard's nav is feature-conditional.** Deep links to
  `/organizations/:id/inventory/...` on a flag-off tenant still render the page shell, which then
  shows the API's 403 through its normal error path. A route guard reading the subscription would
  close that; not built, since the API is the authority and the message is already clear. Same
  known, accepted shape as permission gating today.
- **No `TenantSubscription` update path**, deliberate (confirm-live finding #1). If this product
  ever grows a plan-upgrade or vendor-support flow, that is where a mutator belongs — and the cap
  in `CreateWarehouseCommandHandler` will then start to matter dynamically rather than only at
  creation.
- The three-org E2E fixture (`P20f allon` / `alloff` / `trackonly`) is a useful pattern for any
  future entitlement work — a flag combination where one of two required features is satisfied is
  what catches a short-circuiting gate.
