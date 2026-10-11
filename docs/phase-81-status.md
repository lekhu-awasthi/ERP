# Phase 81 — the ERP's price keys: a clerk who may draft a sale but not price it

## TL;DR

**A discount or a changed rate on a Quotation, Sales Order or Invoice draft now needs its own key at the document's
location: `Sales.Invoice.Discount` and `Sales.Invoice.ChangeRate`.** Until now an ERP draft's price was reviewed only at
Approve, which is no review for a clerk who also approves (phase 76 § 5). Phase 76 had split the *till's* price powers
off Approve; this is the same question for the ERP forms.

The user's decisions (2026-10-11), each the recommended option:

- **A — two new keys**, Admin+Member seeded true, grantable per location, and backfilled to every custom role that can
  create or edit any of the three documents, at the same scope. Nothing changes on upgrade; an Admin makes a clerk who
  cannot price by revoking them on a custom role. Kept apart from the till keys (phase 76's lesson: a borrowed key
  grants everything the first one does).
- **B — Quotation, Sales Order and Invoice**, each Create and Update. A line carried unchanged from the saved draft, or
  from the quotation it was converted from, needs no key. A quotation must be gated too, or a clerk could discount a
  quotation and convert it.
- **C — a changed rate is one the form would not suggest**: neither the catalogue's price in the line's unit (through
  the tenant's price basis) nor, in Recent mode, the last sale's rate in that unit. Raised and lowered alike, as at the
  till. A product with no selling price, and a document in another currency, have no price to keep.
- **D — the forms disable what Save would refuse**, from a small read (`GET /sales/price-powers`), with one line naming
  the missing key. The server decides regardless.

**Read live first** (abcagro1, GETs only, 14 trial days left). The vendor's ERP bundle holds **168 permission keys, none
about price**: its invoice keys are `invoice-view/add/edit/approve/void`, and `discount_method` is a field of the
document, not a permission. So the vendor reviews an ERP draft's price only at Approve, and these keys are **our
divergence**, chosen by the user. A **maximum discount per role** exists in neither of its bundles (the POS
`discount_limit` is a switch, phase 76), so it is closed, not built.

**A bug found while planning, fixed:** picking a unit on any of the five sales forms dropped the unit row's **raw**
selling price into Rate, ignoring the price basis. On an Inclusive-of-VAT tenant that is a VAT-inclusive figure in a
VAT-exclusive field (the customer pays the VAT twice), and the new gate would have refused the clerk's own unchanged
suggestion. The form now asks the server (`suggested-rate?unitId=`), whose Recent mode also reads the last sale **in
that unit** now.

| | |
|---|---|
| Live read taken | **yes**, 2026-10-11, abcagro1, read only (§ Live read). Every request a GET; nothing written |
| Migration | `Phase81SalesPriceKeys`: four seeded `RolePermissions` rows (Admin and Member true, per key) and a hand-written backfill granting both keys wherever a custom role holds any of the six Quotation/SalesOrder/Invoice Create/Edit keys, at the same `LocationId` |
| New permission keys | **two**: `Sales.Invoice.Discount`, `Sales.Invoice.ChangeRate` (Admin+Member, location-scopable; 92 scopable keys, up from 90) |
| Changed endpoints | **new** `GET /sales/price-powers?documentType=&locationId=` (`{canDiscount, canChangeRate}`, the type's View key). `GET /products/{id}/suggested-rate` takes an optional `unitId`. `POST`/`PUT` on `/quotations`, `/sales-orders`, `/invoices` can now 403 naming either key |
| Changed screens | Quotation, Sales Order and Invoice forms: the discount inputs and a locked rate are disabled, with a line naming the key. All five sales forms (plus Credit Note and Delivery Note) take a picked unit's rate from the server |
| Changed print output | none |
| Initial bundle | **652.58 kB**, unchanged |

**Three things worth carrying forward.**

1. **"Unchanged" is a question per power, not per line.** The first version judged a line carried only if its whole
   (product, unit, rate, discount) matched a saved one. Taking a manager's discount off a line the manager had also
   repriced then changed the tuple, and the clerk was asked for `ChangeRate` to charge *more*. The gate now asks two
   multiset questions: is this discount carried (product, unit, discount), and is this rate carried (product, unit,
   rate)?
2. **A gate and the suggestion it must accept share one reader.** `ProductRateReferences` is what the form suggests and
   what the gate keeps; building the gate first exposed that the form's unit pick had never gone through the price
   basis at all.
3. **A refused handler leaves its tracked entities dirty, and a test that calls again on the same context reads the
   dirt as saved.** A refused Update had already cleared the draft's lines, so the next call found nothing carried and
   refused the wrong thing. One context per request (`TestAppDbContext.Create(name)`), as in the Api.

Tests: Domain 1014, Application.UnitTests **1759** (+20), Infrastructure.UnitTests 14, Api.IntegrationTests 71, Angular
**842** (+3, 102 files). `dotnet build`, all four .NET suites (Docker up, one at a time), `ng build` and `ng test` are
green.

---

## Live read, 2026-10-11 (abcagro1, *Abc Agro Trading*), read only

Entered as in phases 74–80: *Open Tigg* with `window.open` replaced and the captured URL followed in the same tab (its
hash never printed), XHR and fetch wrapped to refuse anything but a GET, 1440x900 emulated.

1. **The ERP bundle** (`main` plus all 58 lazy chunks, fetched by GET): no `discount_limit`, no `change_rate`, nothing
   matching a maximum or a lock on a discount or a rate. The one `change_rate` hit is `exchange_rate`.
2. **Its permission keys**: 168 hyphenated keys; none contains *discount*, *rate* or *price*. The invoice's are
   `invoice-view`, `-add`, `-edit`, `-approve`, `-void`.
3. **`discount_method`** is a document field (passed to every line table as `discountMethod: x.discount_method`) and a
   default in Configurations > General's state (`discount_method: "Non"` beside `suggest_selling_price: "recent"`). It
   chooses how a discount is entered, not who may enter one.
4. Conclusion: the vendor reviews an ERP draft's price at Approve and nowhere else. The keys here are a choice, not a
   copy (phase 49: a divergence you choose owes a surface — the form's line naming the key is that surface).

---

## 1. What shipped

- **Keys.** `PermissionKeys.InvoiceDiscount`, `InvoiceChangeRate`, with the derivation beside them. Under
  `Sales.Invoice`, so phase 32b's rule scopes them per location.
- **Seed and migration.** `RolePermissionConfiguration.HasData` has four rows (Admin true, Member true).
  `Phase81SalesPriceKeys` adds an `INSERT … SELECT DISTINCT (RoleId, LocationId)` over the six Create/Edit keys, with
  `NOT EXISTS`, after `InsertData`. `Down` deletes the backfill, then the seeds.
- **`SalesPriceGate`** (`Application/Sales`), called by all six Create/Update handlers after the location and units are
  resolved and before any line is added:
  - discount: the document's discount when it differs from the one carried, or any line discount not carried by
    (product, unit, discount) → `Discount`;
  - rate (base currency only): any line whose rate is not carried by (product, unit, rate) and is neither the
    catalogue's rate in that unit nor, in Recent mode, the last sale's → `ChangeRate`;
  - discount first, then rate; at the document's location, or organization-wide when the type is outside the tenant's
    scope (a Quotation on the default *Sales Transactions Only*).
  - Carried: the saved draft's lines and discount on Update; the source quotation's on a conversion (`Include(Lines)`
    added to that load).
- **`ProductRateReferences.RecentRateAsync`** (`Application/Catalog`): the last approved invoice line's rate for a
  product **in a unit** (null = primary). `SuggestProductRateQuery` gained `UnitId` and reads its catalogue rate through
  `ProductPrices.CatalogueRate` (primary or secondary, through the basis); a unit the product is not sold in is a 404.
- **`GetSalesPricePowersQuery`**: resolves the location as Create would (`LocationResolver`) and answers both keys
  through `SalesPriceGate.IsGrantedAsync`. Gated on the type's View key; `ILocationAgnosticRequest`, like the GL
  previews (a helper for an unsaved form). The validator refuses any type but the three (400).
- **Web.**
  - `SalesService.getPricePowers`, `SalesPricePowers` / `PricedSalesDocumentType`.
  - `shared/sales/sales-price-powers.ts`: `salesPricePowers(…)` (an effect keyed on the header location, the
    `locationAwareProducts` shape), `rateIsLocked(…)` (the server's three exemptions), and `app-sales-price-note`.
  - Quotation, Sales Order and Invoice forms: `[disabled]` on the line and header discount inputs and on a locked rate;
    the note above the lines table, drafts only.
  - All five sales forms' `onUnitChange` asks `suggestProductRate(…, unitId)` for the rate.
- **Tests.** `SalesPriceGateTests` (20): each key alone, both directions of a rate, discount-first, zero-price and
  foreign-currency exemptions, Recent and Fixed modes, edit of a priced draft (quantity yes; discount, document discount,
  rate no), taking a discount off, one carried line vouching for one, conversion, Quotation/Sales Order/Update Quotation,
  a grant at one location (with the read agreeing), the screen's read, a secondary unit through the inclusive basis, and
  Recent read per unit. `TestSupport/SalesPricing.PricerAsync` gives the 50 existing seeding constructions a user holding
  both keys. `sales-price-powers.spec.ts` (3).

## 2. Scope decisions

The user's (2026-10-11), each the recommended option: A–D in the TL;DR.

- **Decision A — rejected alternatives.** *Widen the till keys to both channels*: couples the two (phase 76's lesson)
  and, since they are Admin-only, every Member would lose ERP discounting on upgrade with no way back on a system role.
  *New keys, Admin-only*: the same loss.
- **Decision B — rejected: Invoice only.** A clerk could still promise a discount on a quotation, and converting a
  manager's discounted quotation would need the key.
- **Decision C — rejected: discount only.** Phase 72's hole: a discount gate is a price edit away from useless.
- **Decision D — rejected: server only.** A 403 on Save names the key, but the form would have offered the field.

Decided in the session, not asked:

- **E — Admin+Member.** An ERP draft is still reviewed at Approve, so a price on it is routine daily-use working data,
  and Member has always priced ERP drafts. The till's keys are Admin-only for a different reason (a drawer count matches
  a lowered bill, so nothing reviews it).
- **F — taking a discount off needs no key.** Anyone who may draft may charge the full price. The rate is gated both
  ways, as at the till. *The form does not offer the removal* (its discount inputs are simply disabled); the API allows
  it.
- **G — rate and discount are carried separately** (TL;DR lesson 1).
- **H — Credit Note and Delivery Note are not gated.** A credit note's price is capped by its invoice (phase 69); a
  Delivery Note posts nothing and converts into nothing priced.
- **I — the price-powers read is location-agnostic.** Scoping its View key to the asked location would refuse a
  HeadOffice clerk the answer "no" about a branch; the location it is about is the price keys', which the handler asks.
- **J — no maximum discount per role.** Neither vendor app has one (§ Live read; phase 76's `discount_limit` is a
  switch). Closed with that evidence.

## 3. Bugs found and fixed

1. **A picked unit's raw selling price went into Rate** on Quotation, Sales Order, Invoice, Credit Note and Delivery Note
   (phase 52's `onUnitChange`), ignoring `ProductPriceBasis`. On an inclusive tenant a box at 1,130 inclusive was rated
   1,130 exclusive. Now the server's suggestion: 1,000 (E2E).
2. **Recent mode ignored the unit**: the last sale of a product in boxes was suggested for a line in pieces. Now per
   unit.
3. In the session's own work, before shipping: the whole-line carried match (TL;DR 1); a test reusing one context after
   a refusal (TL;DR 3); `SingleAsync` on `TenantSettings` in the gate, which 500s a tenant with no settings row (now the
   defaults, as everywhere else); and the price-powers read first declared `ILocationBearingCommand`, which
   `LocationBearingCommandSweepGuardTests` refused, rightly — that marker means "writes a location".

## 4. Evidence

- **Backfill on the dev database**, read in SQL before and after `database update`: 14 custom-role scopes held one of the
  six Create/Edit keys (none at a location); after, each new key has 16 rows (14 copied, 2 seeded), and no role that can
  create or edit lacks `Discount` at that scope.
- **Fresh-organization E2E** (*Phase 81 Price Keys*, `dae2f39b-…`): HeadOffice `HO` and branch `BR1`; general settings
  *Fixed* and *Inclusive of VAT*; one Service product, *Consulting*, 113 inclusive at 13% (100 exclusive) with a *box* of
  ten at 1,130 (1,000). The Admin raised a quotation at 90 less 10% and 5% (approved) and an invoice draft at the same
  prices. The custom role **Clerk81** holds every key Member holds organization-wide except the two, plus `Discount` **at
  HO only**.
  - As Admin: `suggested-rate?unitId=box` **1000**, without a unit **100**; price powers `{true, true}`.
  - As Clerk81: price powers at HO `{canDiscount: true, canChangeRate: false}`, at BR1 `{false, false}`.
    - An invoice at 100: **201**; in boxes at 1,000: **201**.
    - At 95 and at 105: **403** *"(HO.Sales.Invoice.ChangeRate)"*.
    - 10% off at HO: **201**; at BR1: **403** *"(BR1.Sales.Invoice.Discount)"*.
    - The Admin's priced draft with quantity 3: **200**; with rate 85: **403** *"(HO.Sales.Invoice.ChangeRate)"*.
    - The approved quotation converted unchanged: **201**.
    - A quotation at 80: **403** *"(Sales.Invoice.ChangeRate)"* — organization-wide, because a quotation carries no
      location on the default scope.
    - A sales order 3% off at BR1: **403** *"(BR1.Sales.Invoice.Discount)"*.
  - **403-not-404** (validation first, so a valid body): `PUT /invoices/{nonexistent}` is **404** "Invoice not found." as
    Admin and **403** *"(Sales.Invoice.Edit)"* from **Viewer81** (only `Tenancy.BillingLocation.View`), beside its **200**
    on `GET /billing-locations`. Viewer81's price-powers read is **403** *"(Sales.Invoice.View)"*; a `CreditNote` type is
    a **400**.
- **SQL.** Five invoices, all Draft at HO: the edited draft (3 × 90, 10% + 5%), the clerk's three (100; 1,000 in
  `box`; 100 less 10%) and the conversion (2 × 90, 10% + 5%, `ReferrerType` Quotation). The quotation is *Converted* with
  a NULL location. No sales order exists: the refused requests wrote nothing. Clerk81's only price-key row is `Discount`
  at HO.
- **Browser and keyboard** (as Clerk81, the cookie transplanted by phase 71's recipe, the file deleted at once).
  - The priced draft at HO: Rate **disabled** showing 90; both discount inputs enabled; the line *"A rate other than the
    product's price needs the Sales.Invoice.ChangeRate permission at this location."*
  - Keyboard: Ctrl+A, 4 in Qty; Tab went to Unit, Item Batch, Serial numbers, then **Discount %** — the disabled Rate is
    skipped. Enter on *Save Draft* sent `PUT` with `quantity 4, rate 90, discountPct 10`, document 5, HO: **200**. SQL:
    HO, 5.00, 4 × 90.00, 10.00.
  - ArrowDown on the location picker moved it to *Lakeside (BR1)*: both discounts and the rate became disabled and both
    lines showed (not saved).
  - The new Quotation form: both discounts disabled, both lines — the organization-wide answer, as the server's.
  - The console's only errors: `configuration/custom-templates` 403 three times (a key Member does not hold, so neither
    does Clerk81; pre-existing) and the cookie fetch from the error page before the dev server was up.
- **No PDF changed**, so none was rendered.

## 5. Left open, and for later phases

- **The form cannot take a discount off for a clerk** (Decision F): the API allows it, the disabled input does not.
- **The note says "at this location" on a quotation** that carries no location on the default scope, where the answer
  is organization-wide. Accurate in effect, loose in wording.
- **A price change between saves.** If the catalogue price changes after a clerk's draft was saved at the old one, the
  saved line is carried and keeps the old price; a new line takes the new one. That is the carried rule working, but a
  re-priced catalogue does not reprice drafts.
- **No other door.** Nothing but the Api's own endpoints sends the six commands (no importer creates these documents,
  and the till builds its invoices without them), so the gate has no second path to cover. An importer for them would
  meet the gate under the importing user (phase 21a's `IJobActingUser`).
- **Carried unchanged:** phase 79 § 5, phase 77 § 5, phase 76 § 5's server-side enforcement read and POS-channel roles,
  phase 75 § 5, phase 74 § 5, phase 72 § 5, phase 71 § 5, phase 70 § 5, phase 69 § 5, phase 67 § 5, phase 66 § 5,
  phase 63 § 5 and phase 62 § 5; phase 80 § 5.

## Writes made to the vendor tenant

None. Every request was a GET (the page's XHR and fetch refused anything else); no screen was saved.
