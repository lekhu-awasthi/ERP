# Phase 76 — Separate till keys: a discount, a waiver and a changed price, each its own key

## TL;DR

**Each of the till's three price powers now has its own location-scoped key, and none of them is `Sales.Invoice.Approve`.**
Phases 72 and 74 gated a discount, a waived service charge and any price but the catalogue's on Approve at the
location. The cost: a cashier allowed to discount could also approve **every ERP invoice draft at that branch**. The
vendor keeps three separate per-location switches: *Can Give Discount*, *Can Remove Service Charge* and *Can Change
Rate*. They were read live and in its bundle on 2026-10-11. The user's decisions (2026-10-11), each the recommended
option:

- **Three keys, named for the till, under `Sales.Invoice`** (Decision A): `Sales.Invoice.TillDiscount`,
  `.TillRemoveServiceCharge` and `.TillChangeRate`, all Admin-only.
  - Under `Sales.Invoice`, phase 32b's derivation makes them grantable per location with no new code, which matches
    the vendor (its switches exist only per location).
  - "Till" in the name says they gate the till alone. An ERP draft's discount and rate are still reviewed at Approve.
- **Every existing Approve grant was copied to the three keys, at the same scope** (Decision B), so no working cashier
  lost a power on upgrade. The Admin revokes what they do not want.

**Credit stays on Approve** (Decision C): a receivable is what Approve reviews (phase 61 Decision F), and the vendor
has no switch for it.

**Read live first, with one accidental write** (abcagro1, § Live read). Phase 72 had placed the switches in "the role
editor's location section". In fact they are in the **POS app's own role editor**: the vendor keeps POS-channel roles
(Cashier, Manager, Waiter) apart from its ERP roles. Its ERP editor's location sections hold only the 94 transaction
keys. Each POS role stores per location `{location_id, permissions, discount_limit, remove_service_charge,
change_rate}`. Despite its name, `discount_limit` is an on/off switch, not a limit.

| | |
|---|---|
| Live read taken | **yes**, 2026-10-11, abcagro1 (one write: the Cashier role re-saved unchanged, § Writes) |
| Migration | `Phase76TillKeys`: six seeded `RolePermissions` rows (Admin true, Member false, per key), plus a hand-written backfill granting the three keys wherever a non-system role held Approve, at the same `LocationId` |
| New permission keys | **three**: `Sales.Invoice.TillDiscount`, `Sales.Invoice.TillRemoveServiceCharge`, `Sales.Invoice.TillChangeRate` (Admin-only, location-scopable) |
| Changed endpoints | none in shape. `POST /pos/sales`, `PUT /pos/orders/{id}/discount`, `.../lines/{lineId}/discount` and `.../service-charge` check the new keys. `GET /pos/tills/{loc}` gains `canChangeRate`, `GET /pos/restaurants/{loc}` gains `canRemoveServiceCharge`, and `canDiscount` reads the new key on both |
| Changed screens | Retail till: the refusal names the missing key (or both). Restaurant order: *Waive/Restore Service Charge* is offered on its own key, apart from *Discount*. The role editor needs no change |
| Changed print output | none |
| Initial bundle | **652.58 kB**, unchanged |

**Three things worth carrying forward.**

1. **Reusing a review key for a second power couples the two.** "Who may lower a bill is who may approve one" read
   well in phase 72. The grant it implied was wider than the question asked: Approve is also the ERP's review of every
   draft. Ask what *else* the borrowed key opens.
2. **A key split owes a backfill at the old key's scope.** Seven custom-role Approve grants on the dev database (one of
   them per location) became 21 rows. Without them, every working till that discounts would have started refusing on
   upgrade.
3. **Read a vendor's screen in the app that owns it.** The ERP role editor was the obvious place to look, and it holds
   none of the three switches. They live in the POS app's own role editor, and the bundle showed which keys it writes.

Tests: Domain 991, Application.UnitTests **1708** (+3), Infrastructure.UnitTests 13, Api.IntegrationTests 57, Angular
**833** (+3, 101 files). `dotnet build`, all four .NET suites (Docker up, one at a time), `ng build` and `ng test` are
green.

---

## Live read, 2026-10-11 (abcagro1, *Abc Agro Trading*)

Entered as in phases 74–75 (14 days left): the ERP was already signed in; the POS app was reached through *Open Pos*
with `window.open` replaced and the captured URL followed in the same tab (its hash never printed); the page's XHR
logged; 1440x900 emulated.

1. **The ERP role editor** (`#/config/user-permission/role-reference/{id}/edit`, the *Sales* role): *Organization-wide
   Permissions* 175 (General 20, Transactions 94, Settings 9, Reports 52), and *Location-specific Permissions* one
   collapsible per location, each **0 of 94** (25 + 25 + 24 + 20), the Transactions group alone. No switch. Every ERP
   role's `location_permissions` is `null` and its `channel` is `"ERP"`.
2. **The POS app has its own roles.** *Settings > Users & Permissions > Role Reference* lists **Cashier, Manager,
   Waiter** from `GET /erp/roles?channel=POS`. Their permissions are a small set: Cashier holds `product-view`,
   `contact-*`, `organization-configuration-view` and six reports. None holds `invoice-approve`.
3. **The POS role editor** (`/pos/settings/users-permissions/role-reference/edit?id=…`) has *POS General*, *Setting*,
   *Reports*, then one row per location with a checkbox (HeadOffice, POS Restaurant, POS Retail). A location's section
   opens only once it is ticked.
4. **The bundle** (`_app`, component `LocationAccordionItem`): each location's section is a matrix plus three
   switches:
   - The matrix: Orders `sales-order` add/view/void, Sales Bill `invoice` add/view/void, Return/Refund `credit-note`
     add/view/void, Settings `location-settings` view/edit, Floor Plan add/view/edit, Customer Payment
     `customer-receipt` add/view/edit/void.
   - The switches: `discount_limit` *Can Give Discount*, `remove_service_charge` *Can Remove Service Charge* and
     `change_rate` *Can Change Rate*, each written as a boolean beside `permissions` in `{location_id, permissions:
     […], discount_limit, remove_service_charge, change_rate}`.
   - There is no organization-wide form of the three, and no credit switch.
5. **The tills read them per location** (chunk `1295`): `XM("discount_limit")`, `XM("remove_service_charge")` and
   `XM("change_rate")` beside each other on the order screen and on both *Edit Item* dialogs. That is phase 72's and
   74's read, now placed in the right editor.
6. Server-side enforcement could not be read without a second, non-Admin user on the vendor's tenant. Ours is
   enforced on the server regardless.

---

## 1. What shipped

- **Keys.** `PermissionKeys.InvoiceTillDiscount`, `InvoiceTillRemoveServiceCharge`, `InvoiceTillChangeRate`, with the
  derivation in a comment beside them. `LocationScopedPermissions` scopes them by its existing rule: 90 scopable keys,
  up from 87.
- **Seed and migration.** `RolePermissionConfiguration.HasData` has six rows (Admin true, Member false).
  `Phase76TillKeys` adds a hand-written `INSERT … SELECT` with `NOT EXISTS`, run after `InsertData`. It grants the
  three keys at the same `LocationId` wherever a role holds Approve, which skips the system roles' seeded rows. Its
  `Down` deletes the backfill and then the seeds.
- **Retail sale.** `CreatePosSaleCommandHandler.EnsureMayChangeThePriceAsync` asks two questions now:
  - any discount (line or bill, percent or amount) → `TillDiscount` at the location;
  - any line off the catalogue → `TillChangeRate`.
  - A sale doing both needs both, discount first.
- **Restaurant order terms.** `PosOrderTerms.LoadForChangeAsync` takes the key from its caller. Order and line
  discounts pass `TillDiscount`; the waiver (both directions) passes `TillRemoveServiceCharge`.
- **Credit** at both tills is unchanged (`PosSaleCompletion`, Approve).
- **Till reads.** `PosTillDto.CanDiscount` reads `TillDiscount`, and `CanChangeRate` is new.
  `PosRestaurantDto.CanDiscount` reads `TillDiscount`, and `CanRemoveServiceCharge` is new.
- **Screens.**
  - The Retail till's `discountRefusal` names what is missing: "At this location, a discount needs
    Sales.Invoice.TillDiscount", ", and a price other than the catalogue's needs Sales.Invoice.TillChangeRate", or both.
  - The order page offers *Discount* on `canDiscount` and *Waive/Restore Service Charge* on `canRemoveServiceCharge`,
    each with its own explanatory line when withheld. The "terms are fixed" line shows to whoever holds either key.

## 2. Scope decisions

The user's (2026-10-11), each the recommended option:

- **Decision A — till-named keys under `Sales.Invoice`.**
  - *Rejected: the vendor's names, also enforced on the ERP invoice form.* It would change what an ERP Member can type
    on a draft, which the vendor's switches do not do.
  - *Rejected: `Pos.*` keys.* These would be organization-wide only. Making them per-location would need a second
    scoping rule, and the vendor's switches are per location.
- **Decision B — backfill every Approve grant at its own scope.**
  - *Rejected: start ungranted.* Every cashier who discounts today would be refused after the upgrade, with nothing
    saying why except a 403.

Decided in the session, not asked:

- **Decision C — credit stays on Approve.** It is what phase 61 Decision F derived, and the vendor has no fourth switch.
  A test now proves that holding all three till keys without Approve still refuses credit.
- **Decision D — Admin-only.** The same derivation as Approve: each key changes what a customer pays past a drawer count
  that matches the changed bill. Member holds none, as it never held Approve.
- **Decision E — restoring a waived service charge needs the waiver's key too.** It is phase 74's rule: a gate on a
  bill's terms is about the terms, not the direction.
- **Decision F — no change to the role editor.** The keys appear under *Sales* organization-wide and in each location's
  section, as raw keys like every other.

## 3. Bugs found and fixed

None in the product. Two in the session's own tooling, both known gotchas:

- A heredoc carrying a test file's replacement failed with "unexpected EOF" (phase 26a/57). The edits went through the
  Edit tool and a `Write`-made patch script.
- A `python -c` patch whose string held both quote kinds and a backslash-escaped apostrophe did not match its anchor.
  It went through the Edit tool.

## 4. Evidence

- **Backfill on the dev database**, checked in SQL before and after `database update`.
  - Before: 7 custom-role `Sales.Invoice.Approve` grants, one of them at a location.
  - After: 21 rows, 7 per key (6 organization-wide, 1 at that location).
  - Every Approve grant has a `TillChangeRate` row at the same scope (8 pairs, Admin's seeded one included). System
    rows: Admin true and Member false for each key.
- **Fresh-organization E2E** (*Phase 76 Till Keys*, `99461382-…`): HeadOffice `HO` is Retail with no service charge,
  `1002` *Lakeside Restaurant* is Restaurant at 10%, one Service product (Chicken Momo, 200) and one cash mode. The
  custom role **Cashier76** holds `Sales.Invoice.Create/View`, `Pos.Session.Operate`, `Pos.Order.Operate/View`,
  `Tenancy.BillingLocation.View` and `Tenancy.Subscription.View` organization-wide, plus `TillDiscount` **at HO only**
  and `TillRemoveServiceCharge` **at 1002 only**. It holds no Approve. The test user's membership was moved onto it in
  SQL and back (phase 72's recipe).
  - `GET /pos/tills/HO` returned `canDiscount: true, canChangeRate: false, canSellOnCredit: false`.
  - `GET /pos/restaurants/1002` returned `canDiscount: false, canRemoveServiceCharge: true`.
  - A sale at 10% off was **200**: `0001`, 203.
  - A sale at rate 190 was **403** *"(HO.Sales.Invoice.TillChangeRate)"*.
  - A sale with credit was **403** *"(HO.Sales.Invoice.Approve)"*.
  - The waiver on the real order was **200**, `serviceChargeWaived: true`.
  - The order discount was **403** *"(1002.Sales.Invoice.TillDiscount)"*.
  - The discount on a nonexistent order was **404** *"Order not found."* (the handler, before the key).
  - **403-not-404:** the custom role **Viewer76** holds `Tenancy.BillingLocation.View` and `Pos.Order.View`. It got 200
    on `GET /billing-locations` and **403** *"(Pos.Order.Operate)"* on the discount of a nonexistent order.
  - Back on Admin, all three till flags were true. The role matrix shows `TillDiscount` granted in HO's section and
    `TillRemoveServiceCharge` in 1002's.
- **SQL.** Cashier76's rows are the two location-scoped grants and no Approve. The only invoice is `0001`: Channel
  `Pos`, `DiscountPct` 10, Amount 180, VAT 23.40, round-off −0.40; the refused sales left nothing. `ORD0001` at 1002 had
  `DiscountPct` 0 and `ServiceChargeWaived` 1, then 0 after the browser pass. The membership was back on Admin.
- **Browser and keyboard** (as Cashier76, cookie transplanted by phase 71's recipe, the file deleted at once).
  - **Order page** at 1002: *Restore Service Charge* was offered, *Discount* was not, and the page said "Giving a
    discount needs the Sales.Invoice.TillDiscount permission at this location."
    - Tab from *Move Table / Guests* reached *Restore Service Charge* (`aria-expanded=false`).
    - Enter opened its panel with focus on the panel's first button. Enter restored the charge.
    - The always-present status region said "The service charge is charged on this order again.", focus returned to the
      opener (now *Waive Service Charge*), and the service charge read 40.00.
  - **Retail till** at HO, line opened with *Edit* and Enter:
    - Price 190 brought "At this location, a price other than the catalogue's needs Sales.Invoice.TillChangeRate."
    - A 10% discount added nothing about `TillDiscount`, which this cashier holds.
    - Price 200 cleared the message, total 203.00.
- **No PDF changed**, so none was rendered.

## 5. Left open, and for later phases

- **The ERP invoice form's discount and rate** are still gated only by Approve at review (Decision A). If a tenant
  wants a clerk who may draft but not discount, that is a new feature, not this split.
- **The vendor's `discount_limit` is a switch, but its name suggests a ceiling.** A maximum discount per role (a
  percentage) is a plausible later feature. It has no live evidence.
- **Server-side enforcement of the vendor's switches** is unread (§ Live read 6).
- **The vendor's POS-channel roles** (separate role sets per channel) are not modelled: our roles span both and are
  scoped by key and location. Nothing here needs them.
- Carried unchanged: phase 75 § 5 (and the phase 73 § 5 it carries), phase 74 § 5 (except its third item, done here),
  phase 72 § 5, phase 71 § 5, phase 70 § 5, phase 69 § 5, phase 67 § 5, phase 66 § 5, phase 63 § 5, phase 62 § 5.

## Writes made to the vendor tenant (abcagro1, with the user's leave)

- **The POS role *Cashier* saved once, unchanged.** The POS role editor's header button reads **Edit** and is its
  **Save** ("Editing role reference …", then back to the list). It was pressed expecting an edit mode. The form held the
  role exactly as loaded, so nothing changed.
- Nothing else was written, and nothing was deleted.
