# Phase 97 — the tax advisor's VAT answers built, and discount schemes

## TL;DR

**In plain words, what changed for someone using the app.**

1. **A 0% sale inside Nepal has its own line on the VAT return.** Goods sold at the zero rate to a customer in Nepal
   (not an export) used to be listed with *Local exempt sales*. The tax advisor said zero-rated and exempt are different
   under Nepali law, so the return now shows **Zero-rated local sales** right after *Exports*, and **Zero-rated sales
   returns** beside the exempt returns. Exempt sales show only truly exempt goods. Totals and the VAT figures are
   unchanged. The Sales Register still has one *Tax Exempt* column for both, as the sales book does.
2. **The VAT credit and Annex 13 needed no change.** The advisor confirmed what the app already does: an unused credit
   at the end of Asar carries into Shrawan exactly as it is, forever, and Annex 13 is before VAT, with the one-lakh
   threshold judged on the amount before VAT. Both are now pinned by tests using the advisor's own examples.
3. **Discount schemes.** *Configurations > Discount Schemes* sets up dated discounts that a sale picks up by itself:
   - **Item wise:** a percent off named products. A product with variants covers its variants.
   - **Category wise:** a percent off every product in a category and its sub-categories.
   - **Slab:** a percent off the whole bill once it reaches an amount ("Rs 5,000 or more: 3%"). The highest step
     reached applies.

   Each scheme has a start and end date and applies at all locations or at chosen ones. Where two schemes give one
   product a discount, the larger wins. They apply on Quotations, Sales Orders and Invoices, at the Retail till, and on
   restaurant orders.
4. **The scheme is where the discount starts.** Picking a product fills its line's discount with the scheme's percent,
   and the document's discount follows the slab as lines change. Anyone may keep those figures, **even without the
   discount permission**. Changing them is an ordinary discount and needs the permission it always did. A draft that is
   reopened is never changed by a scheme.
5. **Tigg's Discount Schemes screen is a mock-up.** Read live on 2026-10-21: its list is hard-coded empty, Save only
   goes back, and its product picker offers one fixed item. So every rule above is the user's (§ 2), not copied.

| | |
|---|---|
| Live read taken | **yes**, the vendor's POS bundle (abcagro1), read only (§ 0) |
| Migration | `Phase97DiscountSchemes`: `sales.DiscountSchemes`, `sales.DiscountSchemeLines`, `sales.DiscountSchemeLocations`; `pos.PosOrders.DiscountSetByHand` (bit, default 0, backfilled to 1 where an open order already had a discount); the two keys seeded and View granted to custom roles that draft sales. Applied to the dev DB |
| New permission keys | `Sales.DiscountScheme.View` (Admin+Member), `Sales.DiscountScheme.Manage` (Admin-only) |
| New endpoints | `GET/POST /sales/discount-schemes`, `PUT/DELETE /sales/discount-schemes/{id}`, `GET /sales/discount-schemes/applicable?date&locationId&productIds`, `GET /pos/tills/{locationId}/discounts?productIds` |
| Changed endpoints | the six ERP sales create/update commands, `POST /pos/sales`, the restaurant order's discount and line-discount updates (a scheme's own figure needs no discount key); `GET /reports/vat-summary` (two new lines) |
| New screen | Configurations > Discount Schemes |
| Changed screens | Quotation, Sales Order and Invoice forms (scheme discounts filled in); the Retail till (line and bill); the VAT Summary (two lines, from the server) |
| Changed print output | **none**. No PDF to look at (the VAT return's `.xlsx` gains the two lines) |

Tests: Domain **1055** (+14), Application.UnitTests **1914** (+17), Infrastructure.UnitTests 14,
Api.IntegrationTests 79, Angular **935** (+7). All green with Docker up.

## 0. Live read, 2026-10-21 (abcagro1), read only

The kickoff named discount schemes as an "Outside this sequence" item whose start condition (basic till discounts,
phase 62/72) was met. Phase 59's scan recorded the vendor's POS settings listing *Discount Schemes* with three kinds
(Item Wise, Category Wise, Slab Discount), a name, code, start and end date, a Vat Inclusive/Exclusive choice, applicable
locations, and rows of Product or Category, Rate and Issue Qty (slab: Lower and Upper Limit, Rate, Issue Qty).

Read in the POS bundle (`1295-*.js`), the screen is a **mock-up**:

- the list renders `data: []`, a static array, with no query;
- the form's Save is `buttonHandler: () => router.push(SETTINGS_POS_DISCOUNT_SETUP)`: it navigates back and sends nothing;
- the product search offers one hard-coded option, `"M004 - Mi TV"`, and the location picker only `"All"`;
- the location field's label is misspelt ("Disocunt Applicable Locations"), and none of the 168 permission keys names it.

So there was nothing to copy, and the four product questions went to the user before any code (§ 2).

## 1. What shipped

**The VAT return.** `SalesReturnReader.Bucketed` gains `ZeroRated`, the part of `TaxExempt` whose lines are `ZeroVat`
(zero on the purchase side). `SalesBookReader`'s line rows carry the stored `VatRate`, and both sales readers fill it.
`VatSummaryReportQueryHandler` puts a domestic invoice's zero-rated value on **`zeroRatedLocalSales`** (after
`exportSales`) and only the rest on `localExemptSales`; a credit note's on **`zeroRatedSalesReturns`**. The block
totals include both. The screen and the `.xlsx` render the return's lines generically, so neither needed a change.

**Discount schemes.** Domain: `DiscountScheme` (code unique per organization, kind, inclusive dates, all-or-named
locations, active), its rows `DiscountSchemeLine` (product, category or minimum amount, and a percent of at most two
decimals) and `DiscountSchemeLocation`, built and checked by `BuildLines`/`BuildLocations`. `PosOrder` gains
`DiscountSetByHand`, `AmountBeforeOrderDiscount` and `ApplySchemeDiscount`; a new order line takes a `DiscountPct`.

Application: `DiscountSchemeRates` (the reader), the settings requests (list, create, update, delete, each validated
to a 400 naming its field, a duplicate code a 409), `ListApplicableDiscountsQuery` (`Sales.DiscountScheme.View`) and
`ListPosTillDiscountsQuery` (`Pos.Session.Operate`). The gates: `SalesPriceGate.EnsureMayPriceAsync` takes a required
`SchemeBasis` (the document's day and its amount in rupees), so the compiler found all six callers; the till sale's
check; `PosOrderTerms.LoadForChangeAsync` takes a `keyNotNeeded` test for the order's discount and a dish's. A dish is
priced at its scheme percent in `PosOrderPricing`, and the order's slab follows after a send, a new order and a discard
(`PosOrderSchemes`).

Angular: the settings page (`/configuration/discount-schemes`, a card on Configurations, and in global search by the
route-derived catalogue); `shared/sales/discount-schemes.ts` (`followDiscountSchemes`, `withSchemeRate`,
`slabDiscountPct`), wired into the three ERP forms; `PosCart` keeps the till's scheme rates and slab ladder, starts a
line at its rate, grows a line still at its scheme's percent, follows the slab until the cashier sets the bill's
discount, and keeps that choice through a hold and a reload; the till's refusal message ignores a scheme's own figures.

## 2. Scope decisions

**A–C are the tax advisor's** (answered 2026-10-21, relayed by the user):

- **A. The credit carries on, unchanged and without limit.** "The unused credit at the end of Asar carries into Shrawan
  exactly as it is. It is never lost, and there is no time limit." A refund is the taxpayer's own optional claim, never
  forced. Phase 89 Decision G already did this; two tests now pin it (three empty years leave a credit whole; Shrawan
  opens with exactly Asar's closing credit on the year before's return).
- **B. A domestic zero-rated sale is not an exempt one.** "Map local 0% sales to Line 5 (Zero-Rated Exports / Sales) …
  or a distinct sub-line." We chose the **distinct sub-line**, *Zero-rated local sales*, placed right after *Exports*,
  so exports stay their own figure. A zero-rated **return** gets *Zero-rated sales returns* beside the exempt returns.
  The split is by the line's stored `VatRate` (`ZeroVat` vs `NoVat`); both compute no VAT, which is why the registers'
  `VatAmount == 0` rule could not tell them apart. The readers carry it as `Bucketed.ZeroRated`, a part of `TaxExempt`,
  so the Sales Register's one Tax Exempt column (the sales book's) is unchanged and still equals the return's exempt +
  export + zero-rated lines (the agreement test says so).
- **On the input credit**, the advisor's point was that listing zero-rated with exempt "would erroneously trigger
  proportional input tax credit restrictions under VAT Rule 39". **This app has never apportioned input VAT** for exempt
  sales either: the return claims the whole month's purchase VAT whatever was sold. So separating zero-rated sales
  changes no figure here; a zero-rated test pins that its input VAT stays whole. Whether *exempt* sales should restrict
  the credit is a separate question (§ 5).
- **C. Annex 13 is before VAT, threshold included** (phase 90 Decision C stands). A test uses the advisor's example: an
  invoice of 95,000 + 12,350 VAT keeps the party off the annex at the default one-lakh threshold.
- **D–G are the user's** (asked first, recommended option each, all four taken):
  - **D. Where:** the till and the ERP's Quotation, Sales Order and Invoice. Restaurant orders too: a dish starts at its
    scheme percent when it is ordered.
  - **E. Percent only.** The vendor's *Issue Qty* (free goods) is left out: free units raise their own VAT and stock
    questions, and "buy 10 get 1 free" can be priced as about 9% off.
  - **F. A slab is judged on the bill's amount before VAT**, after each line's own discount and before the bill's, in
    rupees (a foreign-currency document is folded at its rate).
  - **G. The scheme is the start.** A line (or the document) starts at the scheme's figure; anyone may keep it; changing
    it needs the discount key it always needed. Two schemes on one product: the larger wins.
- **H. One reader for every scheme figure** (`DiscountSchemeRates`): the ERP forms' suggestion
  (`ListApplicableDiscountsQuery`), the till's (`ListPosTillDiscountsQuery`), a restaurant dish as it is ordered, and the
  three gates (`SalesPriceGate`, the till sale, an order's discount and a dish's). Phase 81's rule: a form suggests
  exactly what the gate keeps.
- **I. Our own slab shape: "a bill of at least X".** The vendor's Lower/Upper Limit pair leaves gaps (9,999.50 between
  "5,000–9,999" and "10,000–") and invites overlaps. A threshold ladder has neither; the highest step reached applies.
- **J. The Vat Inclusive/Exclusive field is left out.** A percent off a VAT-inclusive price is the same percent off the
  price before VAT, so for a percent-only scheme the field changes nothing.
- **K. A category scheme covers sub-categories; an item-wise row on a product with variants covers each variant.** The
  product tree and the category tree are what a shop thinks in; naming every leaf would make a "Bakery" scheme miss a
  cake added next week.
- **L. A sale with no location gets only the every-location schemes.** A document type outside the tenant's location
  scope stores no location, so a scheme for named locations cannot know it applies.
- **M. On a restaurant order** the order's discount follows the slab its total reaches as dishes are sent or discarded,
  until the first bill fixes it (phase 72's rule) or a cashier sets it, **including confirming the same figure**
  (`PosOrder.DiscountSetByHand`). A transfer's empty target takes the source's flag with its terms.
- **N. Applied on the form, never re-applied to saved work.** A draft reopened, a conversion, or an AI-prefilled
  document keeps what it carries; a scheme moves only a line whose product was picked on this form and whose discount
  the user has not touched, and a fresh document's own discount.
- **O. Keys.** `Sales.DiscountScheme.View`, Admin+Member: what a scheme gives is routine working data every sales form
  reads, and exposes no PAN or contact. `Sales.DiscountScheme.Manage`, Admin-only: a scheme lowers every customer's
  price at every till it covers without the per-sale discount key, so writing one is a pricing decision for the
  business, like the till keys. Organization-wide (a scheme names its own locations). The till reads schemes under
  `Pos.Session.Operate`, so a till-only role need not hold View. The migration grants View to every custom role that can
  create or edit a Quotation, Sales Order or Invoice, as phase 81's did for its keys.

## 3. Bugs hit and fixed

- **Confirming the slab's figure did not count as setting it.** `PosOrder.SetDiscount` returned early when the value
  was unchanged, so a cashier who set the order's discount to the figure the slab had just given it left the order
  still following the slab, and the next dish moved it. Found by the restaurant handler test. The flag is now set before
  the early return (a Domain test pins it), and the gotcha is in CLAUDE.md.
- **The till's test double lacked the new read** (`getTillDiscounts`), failing 24 till specs with "is not a function".
  The stub answers no schemes unless a test says.
- **Two seed-script shapes** (the E2E's, not the app's): location settings need a `denominations` list, and opening a
  session answers 200. Added to `e2e-recipes.md`.

The ERP gate was proven to bite: with the line exemption removed from `SalesPriceGate`,
`A_clerk_without_the_discount_key_may_keep_a_schemes_percent_but_not_change_it` fails with a 403 naming
`Sales.Invoice.Discount`; the file was restored by hash and touched.

## 4. Verification

- **Tests.** Domain +14 (the scheme's rules; an order following its slab, stopping when set by hand or billed).
  Application +17: the VAT return (a zero-rated line apart from exempt with the input VAT whole; three empty years
  leave a credit whole; Shrawan opens with Asar's close; the register agreement now with zero-rated sales and returns),
  Annex 13 (the advisor's 95,000 + 12,350), the reader (larger wins, ancestors, variants, dates, inactive, locations, a
  merged ladder), the settings requests and validators, and the three gates. Angular +7 (the helper, the settings page,
  the till: a scheme kept without the key, and a cashier's bill discount kept). `Api.IntegrationTests` 79 of 79 with
  Docker up.
- **E2E on a fresh organization** (*Phase 97 Schemes 093213*, VAT-registered, Retail till at Head Office). Schemes
  TEA10 (Masala Tea 10%), BAKERY5 (Bakery 5%, reaching the Cakes sub-category) and SLAB (Head Office only: 1,000 → 3%,
  5,000 → 6%). A slab row naming a product: **400** naming `Lines`; a second TEA10: **409**. The applicable read gave
  Tea 10 and the slice 5 at Head Office, and with no location the slab was absent. As a role with every Admin key but
  the two discount keys: an invoice at the scheme figures (10 tea at 10%, 2 slices at 5%, 3% on 1,280) **201**; a line
  at 12% and a document at 6% each **403** naming `HO.Sales.Invoice.Discount`; a till sale of 12 tea at 10% with 3% off
  **200**, 1,184.00; a till line at 15% **403** naming `HO.Sales.Invoice.TillDiscount`. In SQL: three invoices only
  (nothing from the refusals), each line at its scheme percent, `DiscountPct` 3 on both bills, the seed sale `ZeroVat`.
  The VAT return for 2083: taxable 2,289.20, **zero-rated local sales 200.00**, local exempt 0, total 2,489.20.
- **403-not-404.** `PUT /sales/discount-schemes/{nonexistent}`: **404** "Discount scheme not found." as Admin; **403**
  naming `Sales.DiscountScheme.Manage` from a role holding only `Sales.DiscountScheme.View`, beside a **200** on
  `GET /sales/discount-schemes` from the same user; **404** again once restored.
- **Migration backfill.** Every custom role on the dev database that drafts sales holds the View key (40, plus this
  run's two viewer roles); the 18 orders that had a discount are marked set by hand, the other 33 not.
- **Keyboard, in a real browser** (keys only, from the form's heading). On Discount Schemes: the type select by
  arrow keys to Slab; code and name typed; the native date fields (Tab walks their parts); *All locations* unticked by
  Space and Head Office ticked; two slab rows (*Add row* by Enter; the disabled *Remove* skipped); *Add scheme* by
  Enter. Focus returned to the form's heading and the list showed BIGBILL at Head Office, "from Rs 2000 4%, from Rs 5000
  7%". Tab to its *Edit* (named "Edit BIGBILL"), Enter: focus moved to *Edit Discount Scheme* with a visible ring and
  the form filled. On a new Invoice: the product picked by arrow key filled 5% (Bakery through Cakes), quantity 10 gave
  the document 3% (1,900 at Head Office), 11 gave 4% (2,090 reaches BIGBILL's 2,000, the larger); saved by Enter, the
  draft reopened at 5% and 4% and kept them when the quantity dropped to 2. SQL: Draft, 4% and 5%, 2,006.40.
- **PDF.** None changed, so none was rendered.

## 5. Left open, and why

- **Should exempt sales limit the input VAT claimed?** The advisor's answer implies a proportional restriction when a
  business makes exempt supplies ("would erroneously trigger proportional input tax credit restrictions under VAT Rule
  39"). This app has never apportioned: every month claims all its purchase VAT. Written as a question to hand over:
  *When a VAT-registered business sells some exempt (not zero-rated) goods in a month, must it reduce the purchase VAT it
  claims that month, and by what rule (in proportion to exempt sales, or only the VAT on purchases used for them)? Is
  the adjustment monthly or settled at the year's end?*
- **Free goods in a scheme** (the vendor's *Issue Qty*): left out by the user's choice. Start condition in the roadmap.
- **Restaurant slab after a transfer.** A transfer between two orders on different slab figures is refused, as any two
  orders on different terms are (phase 72), with the message to give both the same discount first. The slab is not
  re-applied after a transfer; the order's next send re-applies it.
- **Not fixable without new information** (unchanged from phase 95): a partial refund of a sale from before phase 94/95
  that walked its batches returns into the oldest-taken batch.

## Writes made to the vendor tenant

None. The read was the POS client's own bundle, fetched in the page; no request was sent to the vendor's API.
