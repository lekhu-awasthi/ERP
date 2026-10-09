# Phase 70 — The packaging fee: a parcel's charge rides its line, like service charge

## TL;DR

**A location can charge a packaging fee on every parcel**:
- a Take Away or Delivery sale, at either kind of till;
- dine-in food marked as take-away.

It is never charged on plated food, a counter sale or an ERP invoice. The fee is either a **percent of the line** or a **flat amount per unit**. It is inside the VAT base, posts to its own account, and is frozen on the line when the line is rung up, sent or marked.

The design choice that made the rest cheap: **the fee rides each line exactly as service charge does**. Because of that, phases 63, 65 and 68's rules cover it with no new rule:
- a split bill's last part takes what is left;
- a refund gives back in proportion;
- a transfer carries the fee with the food.

VAT follows the item, so an exempt item's fee is exempt and VAT stays in code. A product can be exempted with a flag that defaults to on (the user's choice).

The vendor has no packaging fee, so **no live read was needed**: phase 59 had already opened its Location Settings, its till's product form and its order and invoice payloads.

| | |
|---|---|
| Live read taken | **no**: the vendor has nothing to read (Decision A). *Hamro Samaan* was not used |
| Migration | `Phase70PackagingFee`: `PackagingFeeBasis` (nvarchar 20, default `'None'`) + `PackagingFeeValue` (9,2) on `pos.PosLocationSettings`, `pos.PosOrderLines`, `sales.InvoiceLines`, `sales.CreditNoteLines`; `PackagingFeeAmount` (18,4) on the two document lines; `pos.PosLocationSettings.PackagingFeeAccountId` (Restrict FK); `tenancy.TenantSettings.DefaultPackagingFeeAccountId`; `catalog.Products.PackagingFeeApplicable` bit **DEFAULT 1**. Additive only; no backfill (existing rows mean "no fee", and every product pays it, the user's default) |
| Changed endpoints | `PUT /pos/locations/{id}/settings` takes `packagingFeeBasis?`, `packagingFeeValue?`, `packagingFeeAccountId?` (an absent basis keeps the stored fee); `POST/PUT /products` take `packagingFeeApplicable?` (absent: on for a create, unchanged for an update); `PUT /accounting-defaults` takes `defaultPackagingFeeAccountId`. Every till, order, bill, refund, receipt, report and detail DTO carries the fee beside its service charge |
| New permission keys | **none** (Decision J) |
| Changed screens | Configurations > Point of Sale (a Packaging Fee fieldset), the product form (*Packaging Fee Applicable*), Accounting Defaults (a fourth POS default). The fee appears beside service charge on the till cart, the receipts, the restaurant bill, estimate and order, the refund screen, the session page, the ERP invoice and credit-note pages and PDF, the Day Report, Sales Master, Sales by Item and by Customer, and Sales Summary, plus their `.xlsx` exports |
| Initial bundle | **652.58 kB**, unchanged, against 680 kB |

**Four things worth carrying forward.**

1. **Ride an existing mechanism and the hard questions are already answered.** Splits, refunds, transfers, voids, the
   abbreviated-invoice limit and every reader of what a customer owes all took the fee through code that already
   handled service charge. The work was one value type and a sweep, not a set of new rules.
2. **A line's money summed by hand is a sweep the compiler cannot find, and it hides in four shapes:**
   - 33 `x.Amount + x.ServiceChargeAmount` formulas;
   - 8 anonymous projections that never selected the new column (the compiler caught these);
   - 2 records with a field *named* `ServiceCharge` that the first regex could not see;
   - 2 client-side sums in the browser.

   One guard in each language now fails a sum that leaves the fee out.
3. **Give a figure its own arithmetic, then retire the copies.** `PosLineArithmetic.Figures` gained `Total`, `+` and
   `-`, and the split planner's hand-written sums were replaced with them. The fourth part therefore reached every
   sum by being added in one place.
4. **A flag defaulting to on needs its own mutator.** An optional parameter defaulting to true on `Product.Update`
   (57 callers) would have switched an exemption back on from every caller that doesn't know the field: an update
   import, the variant editor. `SetPackagingFeeApplicable` is written only by the product form. The model needs
   `ValueGeneratedNever()`, or EF reads an unticked `false` as "unset" (phase 2 bug #2). Both were proven on
   SQL Server.

Tests: Domain **946** (+39), Application.UnitTests **1638** (+31), Infrastructure.UnitTests 13,
Api.IntegrationTests 43, Angular **785** (+16). `dotnet build`, all four .NET suites (Docker up, one at a time),
`ng build` and `ng test` are green.

---

## 1. What shipped

- **Domain**
  - `Sales/PackagingFee` is a value type: a `PackagingFeeBasis` (None / Percent / PerUnit) and one `Value`. It has a
    private constructor and a validating `Create`, and `default` means no fee.
  - `PosLineArithmetic.Compute` takes the fee: the fee is taken on the rounded amount or per unit, and VAT is charged
    on amount + service charge + fee.
  - `Figures` gained the fee, `Taxable`, `Total`, `+`, `-`, `Zero` and `Parts`.
  - `Pos/PosPackagingFee.For`/`ForParcel` decide which lines carry it.
  - `InvoiceLine`, `CreditNoteLine` and `PosOrderLine` freeze the fee's terms, and the two document lines store
    the amount.
  - `MarkTakeAway` decides the parcel's fee at the mark, and `HasSameTermsAs` includes it.
  - `PosLocationSettings` holds the fee and its account, and `TenantSettings` the default account.
  - `Product.PackagingFeeApplicable` is on by default and set through its own mutator.
- **Application**
  - **Selling:**
    - the till sale (`CreatePosSale`);
    - order pricing and the take-away mark;
    - the order bill (the planner's preview and `CreatePosOrderInvoice`);
    - the refund planner.
  - **Posting:** a third till leg on the invoice and the credit note. `PosAccountResolver.PackagingFeeAccountAsync`
    resolves the location's account, then the tenant default, then a 409.
  - **Readers:** every service-charge reader now shows the fee beside it:
    - the till and refund receipts and the refund preview;
    - the restaurant order view and bill preview;
    - `GetInvoice` and `GetCreditNote`, the PDF and the email merge fields;
    - `PosSalesReader` (sessions, Day Report, dashboard);
    - `TradeLineReader` with Sales by Item, Sales by Customer and Sales Summary;
    - Sales Master.
  - **Formulas:** the 33 hand-written line sums include it (Decision F).
  - **Settings:** the settings and accounting-defaults commands and queries carry it, and so do the product
    commands.
- **Api:** the request records for the settings, products and accounting defaults, plus `.xlsx` columns. The
  Sales Master's total column moves to index 21, and the trade reports' totals row gains a column.
- **Web**
  - **Arithmetic:** `pos-bill.ts` prices the fee in exact paisa, pinned to `pos-bill-cases.json` (six new cases,
    both bases, an exempt item, a bill discount).
  - **Cart:** `pos-cart.ts` decides each line's fee from the tab, the product flag and the till's terms.
  - **Rows:** a Packaging row on every screen and receipt that shows a service-charge figure.
  - **Settings screens:** the settings fieldset (native radios in a labelled radiogroup), the product checkbox, and
    the Accounting Defaults field.
  - **Take-away mark:** its note now says what the fee will be.
- **Guards:** `PackagingFeeSweepGuardTests` (C#) and `packaging-fee-sweep-guard.spec.ts` (Angular).

## 2. Scope decisions

**A. No live read.** The vendor has no packaging fee anywhere. Phase 59 read its *Location Settings > General*
(service charge, round off, cash verification, default tab, printing), its till's product form and its
order/invoice payloads. None has a packaging, parcel or container charge. A read could only have confirmed an
absence, so *Hamro Samaan* (its last trial day) was not used.

**B. Per item, riding the line (the user's choice, 2026-10-08).** The alternative was one charge per bill. That
would have needed new rules for:
- which split part pays it;
- when a refund returns it;
- what VAT a bill mixing taxable and exempt items charges on it.

Per item, phases 63/65/68 answer all three. The two bases:
- **Percent** is of the line's amount *after* its discounts, the base service charge is taken on.
- **PerUnit** is rupees for each unit of the line's quantity, in the unit it was entered in. A bill discount reduces
  a percent fee but never a per-unit one.

One value and a basis, not a rate and an amount, so the two can't disagree (phase 55's rule).

**C. Inside the VAT base; VAT follows the line.** VAT = round((amount + service charge + fee) × the line's rate).
Neither charge is taken on the other. A fee on an exempt item is exempt, which is the ancillary-supply reading, and
VAT stays in code. The roadmap's "VAT stays in code; only fees are settings" is met by construction.
**Citation status:** the VAT Act 2052 §13 citation (the value of a supply is the whole consideration charged) is
phase 61's, made for service charge. Its packaging-specific wording was **not verified this session**: the IRD's
server refused the connection, and the mirror copy's text could not be extracted. It is recorded here as phase 61's
reading applied to a second charge, not as a fresh check.

**D. Every parcel (the user's choice).** The fee applies to:
- Take Away and Delivery sales, including the Retail till's Delivery tab;
- dine-in food marked as take-away.

It never applies to plated food, a Retail-tab counter sale or an ERP invoice. `PosPackagingFee` is the mirror of
`PosServiceCharge`, which charges only what is *not* a parcel. A parcel from a dine-in table can therefore carry
both charges, as phase 68's *Service charge on take-away* allows. The E2E bills exactly that line:
200 + 20 + 10 + 13% of 230 = 259.90.

**E. A product can opt out; on by default (the user's choice).** `Product.PackagingFeeApplicable`:
- **Every existing product is on.** The migration's `DEFAULT 1`; no backfill is needed because that is the wanted
  state.
- **It has its own mutator,** for the kitchen station's reason (phase 64 Decision G).
- **An absent field changes nothing.** `CreateProductCommand`/`UpdateProductCommand` take `bool?`: null means on
  for a create and unchanged for an update.
- **A variant starts with its parent's value.**
- **The importer and the variant editor never touch it.**

The model's `.HasDefaultValue(true).ValueGeneratedNever()` is load-bearing. The E2E checks it in SQL: Coke's `0`
survives the create and an update that omits the field.

**F. Decided once, frozen on the line (phase 68's rule).**
- A Take Away or Delivery order line freezes its fee at first send.
- A parcel freezes it at the mark, reading the product flag then.
- A till sale freezes it at the sale.

A later change of the setting reprices nothing. The E2E changes the restaurant from Rs 20 a unit to 5% between the
order and its bill: the order still bills 40, and a parcel marked afterwards takes 5%.

**G. No new rule for splits, refunds, transfers or voids.**
- **Split bills:** the planner's "last of a line takes what is left" now covers the fee too. A Rs 20 fee in thirds
  bills 6.67 + 6.67 + 6.66.
- **Refunds:** a refund copies the sale line's terms.
- **Transfers:** a transfer copies the source line's, and merges only into a line with the same fee.
- **Voids:** a void reverses the entry's own lines (phase 16a), so nothing in reversal code changed.

**H. Accounts.** The fee is credited to:
- the location's `PackagingFeeAccountId`;
- else the tenant's `DefaultPackagingFeeAccountId`;
- else the sale is refused with a 409 naming both places to set it.

It is resolved only when a document carries a fee, before the number is drawn, so a missing account consumes
nothing. The credit note debits the same resolution.

**I. An absent field never switches a fee (phase 68's rule, three-valued).**
- **The settings `PUT`:** `PackagingFeeBasis?` null keeps the stored fee *and* its account. The validator gives 400s
  naming `PackagingFeeValue` (0 < percent ≤ 100; 0 < per unit ≤ 10,000; two decimals) and `PackagingFeeBasis`.
- **The product commands:** the same, via `bool?`.
- **`UpdateAccountingDefaults`:** it *replaces* every default, so the web form sends the field on every save. The
  browser pass saved the page and re-read the account.

**J. Permissions: no new keys.**
- The settings use `Pos.Settings.Manage`.
- The product flag uses the product keys.
- The default account uses `Accounting.Defaults.Manage`.
- Selling uses the existing sale keys.

The fee is the location's setting, not the cashier's or the waiter's choice. That is why marking a parcel needs
only `Pos.Order.Operate`, as in phase 68.

**K. The settings screen shows the fee at both kinds of till.** A Retail till has a Delivery tab, so its parcels
pay too.

**L. Carried, not built:**
- **The BS date on every bill** (raised by the user on 2026-10-08, carried by their choice; in the roadmap). The
  receipts this phase touched still print AD by default.
- **The full-tenant export**, which now lacks the fee as well as service charge and round-off.

## 3. Bugs found and fixed

None shipped before this phase. Found and fixed during it:

1. **Two hand-written sums escaped the first guard.** `PosSalesReader.DocumentFigures.GrandTotal`
   (`Amount + ServiceCharge + Vat + RoundOff`) and `TradeLineReader.Fact.TotalAmount` add a field *named*
   `ServiceCharge`, not a line column. The guard's first regex matched only `x.Amount + x.ServiceChargeAmount`.
   Session totals, net sales, the Day Report and Sales by Item would all have been short by the fee. Both were
   fixed, and the guard gained a third check over named-figure sums.
2. **Eight anonymous projections fed the swept sums without the column.** The sweep rewrote
   `x.Amount + x.ServiceChargeAmount` inside `g.Sum(...)` over projections that never selected `PackagingFeeAmount`.
   The compiler found all eight (contact ledger, payment allocations, creditable invoices, the credit-note cap, Sales
   Master). Without that it would have been a sweep silently doing nothing.
3. **The ERP invoice and credit-note pages compute a till sale's taxable bucket in the browser**
   (`l.amount + l.serviceChargeAmount`). Without the fee they would have shown Taxable 460 against a printed 480.
   Fixed, and the client guard now fails any such sum.
4. **The accounting-defaults Api record compiled without the new account**, because the command's parameter is
   optional (phase 27b). It was added to the record. The browser pass confirmed a save keeps it.
5. **A test helper summed an estimate's parts by hand** (`PosTakeAwayAndTransferTests.Total`). It now uses
   `Figures.Total`.

## 4. Evidence

**Suites.** Domain 946, Application.UnitTests 1638, Infrastructure.UnitTests 13, Api.IntegrationTests 43 (Docker
28.0.4), run one at a time. `ng build`: initial 652.58 kB. `ng test`: 97 files, 785 tests.

**Fresh-organization E2E** (*Phase 70 Parcel Kitchen 6350*, `5a0621b6-66fd-4e0a-9e06-936a4fb51824`), seeded through
the API with every status printed. Three earlier runs each stopped at a wrong status expectation in the script
(204, 200, the area endpoint's shape) and left a partly seeded organization: `c54e84e2…`, `7f355576…`, `95d05f66…`.

- **Setup**
  - **HeadOffice:** Retail, a 5% fee, the tenant-default account.
  - **Restaurant:** 10% service charge, Rs 20 a unit, its own *Restaurant Parcel Income* account.
  - **Products:** Momo (200, service charge, fee by default) and Coke (60, created exempt).
  - **Accounting Defaults:** `defaultPackagingFeeAccountId` round-trips through the Api record.
- **Settings `PUT`**
  - From a client omitting the three fields, it kept the fee (PerUnit 20, its account).
  - 0% is a **400 naming `PackagingFeeValue`**.
- **Coke's exemption** survives an update that omits the flag.
- **The Retail till (HeadOffice)**
  - The till DTO reads Percent/5.
  - **A Delivery sale** (2 Momo + 1 Coke): fee 20 (Coke exempt), 542.40 → **542**.
  - **A counter sale:** no fee, **226**.
  - **The receipt** carries the 20.
- **The restaurant**
  - **A Take Away order** (2 Momo + 1 Coke): fee 40, no service charge, estimate **565.00**.
    - Changing the setting to 5% left it at 40.
    - Billed in two equal parts: **283 + 282**, fee 20 + 20.
  - **A dine-in order** (3 Momo): no fee.
    - One momo was marked take-away: the parcel took the 20 service charge and the **5%** fee (10), total **259.90**.
    - The whole bill was 497.20 + 259.90 = 757.10 → **757**.
  - **The refund** of the parcel: the refundable line shows fee 10; the preview gives fee 10 and payout 260; the
    refund carries fee 10.
  - **The session** shows fee 50 sold and 10 returned.
- **In SQL** (`sqlcmd`)
  - Coke `PackagingFeeApplicable = 0`, Momo `1`.
  - Every 13% line's `VatAmount = ROUND((Amount + SC + Fee) × 0.13, 2)`.
  - Every invoice's lines + round-off equal its tenders net of change (542, 226, 283, 282, 757).
  - The take-away order's two bills come to 565 with fee 40.
  - Order lines carry the frozen terms (PerUnit 20 on the take-away lines, Percent 5 on the parcel).
  - The credit-note line has Percent 5 and fee 10.
  - **GL:** *Packaging Income* (tenant default) credit 20, and *Restaurant Parcel Income* 40 + 10 − 10 = 40.
  - All 12 entries are balanced, and Accounts Receivable is 0.
- **403-not-404**
  - `PUT /pos/locations/{nonexistent}/settings` with a valid body is **404** "Billing location not found." as Admin.
  - It is **403** "You do not have permission to perform this action (Pos.Settings.Manage)." after the same user is
    moved to a role holding only `Tenancy.BillingLocation.View`.
  - That user got **200** on `GET /billing-locations`.
  - The Admin role was restored through `tenancy.OrganizationMemberships`.
- **Browser pass** (`erp-web-ssl`, cookie transplanted)
  - **Settings page:** the Packaging Fee fieldset shows HeadOffice's 5% and the tenant default.
  - **Product page:** Coke's overview says *Packaging Fee Applicable: No*. Its edit form shows the box unticked, and
    the four checkboxes wrap at full width (no crushed control; phase 52).
  - **Till, Delivery tab:** two Momo show *Packaging 20.00*, VAT 54.60, round off 0.40, **475.00**. Adding Coke
    leaves Packaging at 20.00 (VAT 70.20, **610.00**). The Retail tab drops the row (VAT 67.60, **588.00**). The cart
    was cleared afterwards.
  - **Session page X report:** *Packaging 20.00*, and 660 + 0 + 20 + 88.40 − 0.40 = **768**.
  - **A reprint of the delivery receipt** reads Sub Total 460, Packaging 20, Taxable 480, VAT 62.40, Round Off
    −0.40, Total 542. Its dates are AD, the carried item.
  - **Accounting Defaults:** shows *Default Packaging Fee Account*. Save, then a re-read, still holds it.
  - **The ERP invoice page:** *Packaging 20.00*, Taxable Total **480.00** (the browser-computed figure this phase
    fixed), VAT 62.40, Grand Total 542.00, and the GL panel's *Packaging Income 20.00*.
- **Keyboard check** (real keypresses, focus read from a `focusin` log)
  - The settings page runs the service-charge checkbox → the checked basis radio → ArrowDown selects *A flat
    amount per unit*, and the value's label becomes *Amount per unit (Rs)* → the value input → the account select →
    Round off.
  - The focus ring is the app's `#0a58ca` outline.
  - The product form's checkbox is described by its visually hidden help text.

## 5. Left open, and for later phases

- **The BS date on every bill** (the user, 2026-10-08): its own item in the roadmap.
- **The VAT Act §13 wording for packaging**: unverified this session (Decision C).
- **The full-tenant export** has no service-charge, round-off or packaging-fee column (phase 61 § 5, widened).
- **A receipt or estimate line showing which items carried the fee** (only the total row prints). Nothing asked
  for it, and a line's own fee is in the DTOs.
- **A restaurant's tiles show the rate, not a tab price**, so a Take Away tile does not include the fee. That was
  already true for service charge, by phase 62's design.
- Carried unchanged: discount and credit on a restaurant bill, per-part customers (phase 68 § 5), real printing
  templates, phase 69 § 5, phase 67 § 5, phase 66 § 5, phase 63 § 5, phase 62 § 5.

## Writes made to the vendor tenant

None. The vendor was not opened this phase.
