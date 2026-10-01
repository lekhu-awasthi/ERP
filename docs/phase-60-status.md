# Phase 60 — POS foundation: a mode on every location, the settings a till reads, and two import holes

## TL;DR

**The configuration a till needs now exists, and nothing sells yet.** This phase adds what phases
61–66 read. It covers a POS mode on every billing location, per-location POS settings, payment modes
that know their kind and their account, the payment modes each till offers, a walk-in customer on
every tenant, three tenant-default accounts, a service-charge flag on products, and one permission
key. It also adds *Configurations > Point of Sale*, where all of it is set. No confirm-live pass was
needed: phase 59 had already read the vendor's configuration screens (`erp-module-scan.md`,
"Configuration, as read").

| | |
|---|---|
| Live read taken | **no**: phase 59's read covers every screen this phase models (Decision J) |
| Migration | `Phase60PosFoundation`, hand-edited twice (the retired-type mapping and the walk-in backfill) |
| Migrated rows (dev DB) | 212 locations → `PosMode = None` (192 HeadOffice, 20 Standard, none on the retired 3/4); 7 payment modes → `Kind = Other`; **192/192** organizations got exactly one walk-in |
| New permission keys | **1** (`Pos.Settings.Manage`, Admin-only). The other four of phase 59 §4 J wait for their requests (Decision D) |
| New tables | `pos.PosLocationSettings`, `pos.PosLocationPaymentModes` |
| New pipeline mechanism | `IRequireAnyFeature`, the any-of half of `FeatureGateBehavior` |
| Bugs found in passing | **2**, both the phase-51 optional-parameter shape (§ Bugs) |
| Code posting to the GL | **none**; phase 61 is the first |

**Four things worth carrying forward.**

1. **A POS type is a mode, and switching it off must always work.** `BillingLocation.PosMode`
   (`None | Retail | Restaurant`) replaces the two reserved `BillingLocationType` members. Retail
   needs the POS Retail entitlement and Restaurant needs POS Restaurant, checked in the handler
   because the answer depends on the value asked for. `None` needs neither, so a tenant that loses an
   entitlement can still turn its tills off.
2. **An "any of these features" gate belongs in the pipeline too.** Every POS configuration request
   serves a Retail tenant and a Restaurant tenant alike. `IRequireAnyFeature` joins `IRequireFeature`
   in `FeatureGateBehavior`, and the 403 names both features.
3. **A setting a till reads must be refusable where it is set, not where it is used.** A payment mode
   linked to a till must be active and name a cash or bank account. That is refused at link time
   (400, naming the modes) *and* at edit time (409 if a linked mode would lose its account or be
   deactivated). Otherwise the cashier with a customer waiting would be the one to find out.
4. **Adding a trailing optional parameter is how you find the call sites that never passed the last
   one.** `ServiceChargeApplicable` went on the end of `Product.Update`. The two production callers
   that omit trailing arguments had already been silently clearing five fields between them (§ Bugs).

Tests: Domain **777** (+13), Application.UnitTests **1397** (+15), Infrastructure.UnitTests 13,
Angular **632** (+5). `dotnet build`, the three unit suites, `ng build` (645.98 kB initial, budget
680) and `ng test` are green. **`Api.IntegrationTests` was not run**: Docker Desktop was not running
this session, and that suite fails in its constructors without it. Nothing in it was changed.

---

## 1. What shipped

**Domain**

- `Domain/Tenancy/PosMode.cs`: `None = 0, Retail = 1, Restaurant = 2`, stored as a string.
  `BillingLocation.PosMode` plus `SetPosMode`, which refuses a till on an inactive location but always
  allows `None`. `BillingLocationType` is now `HeadOffice | Standard`; ordinals 3 and 4 stay unused.
- `Domain/Pos/PosLocationSettings.cs` has one row per location:
  - service charge (enabled, rate, account);
  - round-off (enabled, account);
  - cash verification and denominations (default `1000/500/100/50/20/10/5/2/1`, stored largest first,
    distinct and positive);
  - default tab;
  - four print toggles (estimate bill, invoice, credit note, KOT).
  `CreateDefault` is what an unsaved location reads as. `EffectiveDefaultTab(mode)` absorbs a stale tab
  after a mode change.
- `Domain/Pos/PosTab.cs`: `Retail | DineIn | TakeAway | Delivery`, with `PosTabs.For(mode)` as the one
  statement of which tabs each mode has.
- `Domain/Pos/PosLocationPaymentMode.cs` is the link row: location × payment mode, unique.
- `PaymentMode.Kind` (`Cash | Card | EPayment | Other`, no member 0) and `PaymentMode.AccountId`.
- `Contact.IsWalkInCustomer`, `Contact.CreateWalkInCustomer` ("Cash Customer", code `WALKIN`), and a
  `Deactivate` that refuses it.
- `Product.ServiceChargeApplicable`, copied to a variant at creation like `AvailableForSale`.
- `TenantSettings.DefaultServiceChargeAccountId / DefaultRoundingAccountId /
  DefaultCashOverShortAccountId` plus `SetPosDefaults`.

**Application.** `Pos/`:

- `GetPosConfigurationQuery` returns the entitlements, the walk-in and the locations with their modes.
- `GetPosLocationSettingsQuery` and `UpdatePosLocationSettingsCommand`. The settings reader is shared,
  so a save answers with exactly what a reload shows.
- `SetLocationPosModeCommand`.
- `SetPosLocationPaymentModesCommand`, which diffs the set through the child `DbSet`.

Outside `Pos/`:

- `PaymentModeAccountRule`: a payment mode's account must be Cash or Bank.
- The payment-mode commands carry `Kind` and `AccountId`.
- `UpdateAccountingDefaults` and `GetAccountingDefaults` carry the three new accounts.
- `CreateOrganization` seeds the walk-in.
- `DeactivateContact` returns 409 for the walk-in.
- `IRequireAnyFeature` and `PosFeatures.Any`.

**Api.** `PosEndpoints.cs` under `/api/organizations/{id}/pos`:

- `GET /configuration`
- `GET|PUT /locations/{locationId}/settings`
- `PUT /locations/{locationId}/mode`
- `PUT /locations/{locationId}/payment-modes`

The payment-mode, product and accounting-defaults request records carry the new fields. That is
phase 27b's rule: a field on the command alone binds to its default in silence.

**Angular**

- *Configurations > Point of Sale* (`configuration/pos`) has a location picker, the POS type, the
  General settings, and a payment-mode checklist that disables a mode with no account and says why.
  The route guard is `anyFeatureGuard('PosRetail', 'PosRestaurant')`, and the hub card shows only
  where a POS entitlement exists.
- *Payment Modes* gained Type and Payment Account (cash and bank accounts only).
- *Accounting Defaults* gained a Point-of-Sale section with the three accounts. That is phase 29's
  rule: grep `web/` for each field before calling it done, and all three are there.
- The product form and detail gained *Service Charge Applicable*.
- `BillingLocationType` lost its two POS members, and `BillingLocation` gained `posMode`.

## 2. Scope decisions

**A. Every existing location migrates to `PosMode = None`, and so is every new HeadOffice.** The
vendor types its HeadOffice Retail, so "HeadOffice → Retail" was the obvious alternative. It was
refused for three reasons:

- No till existed before this phase, so `None` is the only value that is *true* of existing rows.
- A mode requires its entitlement. Migrating to Retail would have put 192 tenants into a state their
  own flags forbid, or would have meant guessing per tenant from flags nobody had ever acted on.
- The vendor's type field has no "none", because every vendor location has a till. Ours are ERP
  locations first.

A tenant opts a location in on the POS screen. The migration also maps any row still holding the
retired `LocationType` 3 or 4 to Standard plus the matching mode. None existed, but a database that
had one must not keep an enum value the model has lost.

**B. The walk-in customer is a flagged `Contact`, not a setting that points at one.** The question
phase 61 asks is "is *this* contact the walk-in?", about a row it has already loaded, in order to
refuse a Credit tender (phase 59 defect 5). A re-pointable setting has two costs:

- It would turn yesterday's walk-in into an ordinary creditable customer in silence.
- It would make historical walk-in sales indistinguishable from named ones.

The flag has the standing HeadOffice has. There is at most one per tenant (a filtered unique index on
`OrganizationId WHERE IsWalkInCustomer = 1`), it cannot be deactivated (409), and it may be renamed.
It is seeded for **every** tenant, not only POS ones: the plan can change after creation (phase 41)
while the seeding path runs once, and a walk-in is a useful contact for an ERP cash sale anyway. Its
code `WALKIN` sits outside the numbered pool (codes there are digits).

**C. Denominations are per location**, as the vendor's are. A drawer's denominations are the notes
that drawer holds; they are a fact about one till, not about the currency.

**D. Only `Pos.Settings.Manage` ships.** The kickoff asked for the five keys of phase 59 §4 J.
Four of them gate requests that do not exist yet:

| Key | Arrives with |
|---|---|
| `Pos.Session.Operate` | phase 61 |
| `Pos.Session.ViewAll` | phase 61 |
| `Pos.FloorPlan.Manage` | phase 64 |
| `Pos.Kitchen.Operate` | phase 64 |

A key in the role editor that gates nothing is present-and-ignored: an Admin who revokes it has been
told they changed something (phase 43's `WorkspaceName`, applied to a permission). Their derivations
are already recorded in §4 J, so each later phase seeds its own in its own migration.
`Pos.Settings.Manage` is Admin-only, organization-wide (it is not a transaction key, so phase 32b's
scope does not reach it), and reads as well as writes, as `AccountingDefaultsManage` does.

**E. `Product.AvailableForSale` already existed.** It has been in the domain since phase 3, with a
form checkbox, an import column and an export column, and nothing reads it. The phase adds no field.
Phase 62's product grid is its first consumer. `ServiceChargeApplicable` is the only new product
field. It defaults to false, which is true of every existing product, since no service charge was
ever charged.

**F. The settings are their own table, read as defaults until first saved.**

- A location exists for every tenant, but these nine settings mean something only where a till runs.
  Nobody else joins to them.
- `GET` writes nothing. It projects `CreateDefault` with `isSaved: false`, and the first `PUT` creates
  the row.
- Both accounts are optional even when their feature is on. Empty means "use the tenant default",
  resolved lazily by phase 61 when a sale actually carries a charge or a rounding. That laziness is
  what makes the tenant defaults (phase 59 Decision I) worth having beside a per-location field.
  Switching a feature off clears its account and, for service charge, its rate, so the pair can never
  disagree about whether a charge applies.
- The default tab is checked against the location's **current** mode when saved (400 naming
  `DefaultTab`). A later mode change can leave it stale; the DTO's `effectiveDefaultTab` shows what
  the till will actually open on.

**G. Payment modes are extended, not paralleled.**

- `Kind` backfills to `Other` for every existing mode. Guessing `Cash` from a name is exactly the
  match phase 17 refused for cheques.
- `AccountId` must be a Cash or Bank account. It is optional, because `Payment` never read it and
  still does not.
- The per-location link is a table, and it cascades when a mode is deleted: the delete is the
  decision, and a link to nothing would be a till tab that cannot post.
- Linking refuses an inactive or account-less mode. Editing a linked mode to drop its account or
  deactivate it is a 409 naming the way out (unlink first). That is phase 45's rule: guard the add
  *and* the edit.

**H. The gate: behaviour for "any POS", handler for "this mode".** The four configuration requests
carry `IRequireAnyFeature(PosRetail, PosRestaurant)`. `SetLocationPosModeCommand` deliberately does
not: which entitlement it needs depends on the requested value, and `None` needs none. This is
phase-20f Decision #4's split between behaviour and handler.

**I. The mode is set on the POS screen, not on the location dialog.** The vendor puts it on its
Locations form. Here `UpdateBillingLocationCommand` replaces a location whole, so a client that did
not know about the field would switch every till off by saving an address. The mode lives where it
acts.

**J. No confirm-live pass.** Phase 59 read every configuration screen this phase models. Two vendor
fields remain unread semantically, and neither was modelled:

- **Amount Based Billing**, a print toggle whose effect is unknown. It is omitted rather than stored
  inert.
- **`round_amount`** on `/round-amount`, which may be a flag or an increment. It is modelled as a
  flag (round to the rupee, the one behaviour observed: 632.80 → 633). Phase 61 owns the rule.

## 3. Bugs found and fixed

1. **Editing a variant silently switched its batch and serial tracking off.**
   `UpdateProductVariantCommandHandler` called `Product.Update` without the trailing `batchTracking`
   and `serialTracking` arguments, so both defaulted to false on every variant edit since phase 51.
   It was found while adding a third trailing argument that would have been dropped the same way.
   It now passes all three from the variant. This is phase 51's gotcha exactly: a sweep through
   optional parameters is enumerated by nothing.
2. **An "Update Existing Records" product import cleared five fields.** `ProductImporter`'s update
   plan passed nothing after the four GL accounts, and every omitted parameter took its default:
   - the SKU and the barcode were cleared;
   - the product's locations widened to *all*;
   - batch and serial tracking were switched off.
   The plan now passes the existing values, loading `Locations` to do so.
   `Update_mode_keeps_every_field_the_template_has_no_column_for` pins it. It was proven to bite by
   removing the fix (it failed on the SKU), then restoring the file by hash and `touch`ing it.
3. **The payment-mode form's labels stopped lining up** once the account help text made one column
   taller. The help text moved to its own row. Seen in the browser pass.

## 4. Evidence

**Migration** (dev DB, `sqlcmd`, after `dotnet ef database update`):

```
LocationType,PosMode,N        Kind,N,NoAccount     Orgs,WalkIns,OrgsWithWalkIn
1,None,192                    Other,7,7            192,192,192
2,None,20
Pos.Settings.Manage  Admin 1 / Member 0
```

**E2E** on two fresh organizations: *P60 Momo House 003733* (POS Restaurant only) and *P60 No POS
003733*. Master data was seeded through the API, and every status code was printed and matched its
expected value.

| Area | Checked | Result |
|---|---|---|
| Walk-in and mode | configuration shows the walk-in (`WALKIN`, "Cash Customer") and HO at `None` | yes |
| | mode Retail on a Restaurant-only tenant | **403**, naming POS Retail |
| | mode Restaurant | 200; tabs DineIn/TakeAway/Delivery |
| Settings | `PUT` then `GET` round trip | `saved == read`; denominations sent unsorted, stored `1000…1` |
| | `defaultTab: Retail` at a Restaurant | 400, `DefaultTab` |
| | duplicate denomination | 400 |
| | rate 0 with service charge on | 400 |
| Payment modes | an income account on a payment mode | 400, `AccountId` |
| | linking an account-less mode | 400, naming `'Voucher'` |
| | linking Cash | 200 |
| | removing the linked Cash mode's account | **409** |
| Other writes | the three POS default accounts through `PUT /accounting-defaults` | round trip |
| | a product with `serviceChargeApplicable: true` | reads back true |
| | deactivate the walk-in | **409** |
| No-POS tenant | `GET /pos/configuration` | **403**, *"…Point of Sale (Retail) or Point of Sale (Restaurant)…"* |
| | `PUT` settings | 403 |
| | mode Restaurant | 403 |
| | mode None | **200** |
| Negative permission | `GET /pos/locations/{random}/settings` as Admin | **404** |
| | the same user on a custom role granting only `Tenancy.BillingLocation.View`: `GET /billing-locations` | **200** |
| | same role, `GET` settings and `PUT` mode on the random id | **403 naming `Pos.Settings.Manage`** |
| | own role restored afterwards | via `sqlcmd` (phase 52's recipe) |

**SQL agrees with the API:**

- HO is `Restaurant` on the POS tenant and `None` on the other.
- The settings row is `10.00, NULL, 1, 1, "1000,500,100,50,20,10,5,2,1", TakeAway, PrintCreditNote 0`.
- Cash is `Kind Cash → Cash In Hand` and linked; Voucher is unlinked.
- Each organization has exactly one active `WALKIN`.
- The three defaults name the three accounts.
- *Chicken Momo* is `ServiceChargeApplicable 1`.

**Browser pass** (erp-web-ssl, cookie transplant):

- The POS page rendered the API-saved state. Changing the rate to 12.5 and the default tab to
  Delivery in the UI, then saving, persisted `12.50, Delivery` (`sqlcmd`). No console errors.
- Payment Modes renders kind and account per row.
- `configuration/pos` on the no-POS tenant redirects to `/home`. The hub card is absent there and
  present on the POS tenant.

## 5. Left open, and for phase 61

- **The three rule questions of phase 59 §6 are still open**: service charge in the VAT base,
  abbreviated tax invoice conditions, and reprint marking. The first must be settled before phase 61
  posts a service charge.
- `Product.ServiceChargeApplicable` has **no import or export column**. The update import preserves
  it (bug 2's fix), but a file cannot set it. Add both columns if a tenant needs bulk editing.
- The rail and search catalogue list *Point of Sale* for every tenant, as they list every
  entitlement-gated route; the route guard redirects. Filtering the catalogue by entitlement would be
  a sweep over every gated route, not a POS change.
- Phase 61 resolves accounts **location → tenant default → 409 naming the missing one**, lazily. It
  also refuses a Credit tender against `IsWalkInCustomer`, requires a Cash-kind mode for drawer
  movements, and seeds `Pos.Session.Operate` / `Pos.Session.ViewAll`.
