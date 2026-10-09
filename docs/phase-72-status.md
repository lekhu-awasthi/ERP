# Phase 72 — The restaurant bill finished: a discount is a term of the order, a bill names its own customer, and lowering a bill needs Approve

## TL;DR

**A restaurant order can now carry a discount and a declined service charge, each bill can be made out to its own guest
and left on their account, and a till sale that lowers a price anywhere needs someone who may approve invoices.** Before
this phase the bill screen took payment in full from the order's customer, an order bill refused any discount in the
Domain, and the Retail till let any cashier discount or type a lower price. The user's decisions (2026-10-09):

- **A discount lives on the order** (Decision A): a line's own discount and the order's discount, percentages, fixed
  once billing starts (the order's at its first bill, a line's at its first part), so every part is priced at the same
  terms and the bills still add up to the order (phase 65's rule, unchanged).
- **Percent only** (Decision B), as the Retail till and the ERP invoice already are. The vendor's Amount option is a
  recorded divergence.
- **Lowering a bill needs `Sales.Invoice.Approve` at the location** (Decision C), at both tills: a discount, a waived
  service charge, and (the user's further answer) a Retail line rung up below the catalogue's price. A till sale is
  approved the moment it is rung up, and a drawer count cannot catch a discount, so the approver's review happens when
  the price is lowered. No new keys.
- **A guest who declines the service charge** (Decision D): waived on the order, fixed like the discount. A mandatory
  service charge has been void since the Supreme Court's ruling of 2023-01-25 (phase 61 Decision A).
- **A bill names its own customer** (Decision E): the order's by default, or one chosen for that bill only, with credit
  under the Retail till's rules. The command already took it (phase 65); the screen now sends it.

**Read live first** (Hamro Samaan, its last trial day, read-only, every write blocked in the page): the vendor keeps
both discounts on the order (*Edit Item* per line, *Options > Discount* for the order), Percent or Amount, gated per
location by a role switch (*Can Give Discount*, beside *Can Remove Service Charge* and *Can Change Rate*), and its
customer is the order's (`update-customer`), with no per-bill choice.

| | |
|---|---|
| Live read taken | **yes**, read-only, 2026-10-09 (§ "Live read") |
| Migration | `Phase72OrderDiscount`, scaffolded and read: `pos.PosOrders.DiscountPct` (decimal(5,2)), `.ServiceChargeWaived` (bit), `pos.PosOrderLines.DiscountPct`, all NOT NULL with defaults true of every existing row |
| New endpoints | 3, all `PUT` under `/{org}/pos/orders/{id}`: `/discount {discountPct}`, `/lines/{lineId}/discount {discountPct}`, `/service-charge {waived}` |
| Changed endpoints | the order, line and bill-preview DTOs carry the terms and `gross`/`discount`; both till reads carry `canDiscount`; `POST /pos/sales` re-checks Approve for a discount or a cut price |
| New permission keys | **none**: `Pos.Order.Operate` (pipeline) and `Sales.Invoice.Approve` (handler, at the location) |
| Changed screens | the restaurant order screen (Discount, Waive/Restore Service Charge, a line's Discount), the bill screen (Bill to, credit), the estimate bill, the Retail till (says why before Complete); one shared customer search for all three |
| Initial bundle | **652.58 kB**, unchanged, against 680 kB (every POS screen is lazy) |

**Four things worth carrying forward.**

1. **A term that prices every part is fixed when the first part is priced.** The planner gives the last of a line
   exactly what is left of its money, so a discount changed after a part was billed would land on the last part alone.
   The order's own terms are fixed at its first bill; a line's at its own first part. Setting the value already there
   is no change, so a repeated request is not a refusal.
2. **A gate on one door is a price edit away from useless.** Gating the Retail till's discount fields alone would have
   left any cashier free to type a lower price. A line below its unit's catalogue rate (`ProductPrices.CatalogueRate`,
   the grid's own figure, so an unchanged price is equal to the paisa) is a discount by another name.
3. **A tab's terms travel with its food.** Phase 68's transfer copied a line's terms so a dish costs the same on
   either tab. With terms on the order too, a target with nothing on it takes the source's, and any other must already
   have them (a 409 naming both). Silently repricing on a transfer is the vendor's defect 1 in another shape.
4. **A control that removes itself drops a keyboard user onto the page.** Choosing a customer hid the result and its
   panel, and *Use Walk-in* hid itself: focus fell to `<body>` on the till and order screens since phases 62 and 64,
   unnoticed until this phase's keyboard pass. The shared search now hands focus back to the button that opened it.

Tests: Domain **959** (+13), Application.UnitTests **1665** (+13), Infrastructure.UnitTests 13, Api.IntegrationTests 46,
Angular **811** (+18, 99 files). `dotnet build`, all four .NET suites (Docker up, one at a time), `ng build` and
`ng test` are green.

---

## Live read, 2026-10-09 (*Hamro Samaan*, its last trial day), read only

The browser pane was still signed in to the vendor's portal, and the tenant still opened ("0 days remaining"). The POS
was entered through *Open Pos*'s hand-off, captured and followed in the same tab so the sign-in token never entered the
transcript. Before anything was clicked, the page's `XMLHttpRequest` and `fetch` were wrapped to log every API request
and **refuse any that was not a GET**: 21 requests were logged, all GETs, none refused. Phase 68's dine-in order on T2
(`SO0005/1002/83-84`, 813.60) was still open and was read without being changed.

1. **The order screen.** *Options* offers Discount, Add Note, Transfer List, Discard and Print Estimate bill. *Discount*
   opens **Apply Discount**: *Type of Discount* **Percent (%) | Amount** (Amount preselected) and one *Discount* field.
   Its schema refuses a percent over 100 ("Discount cannot be more than 100%") and an amount over the total.
2. **The line.** *Edit Item* has Quantity, Unit, Price (VAT-exclusive, 200), *Type of discount* **Percent (%) | Amount**
   (Percent preselected), *Discount (Item Line)* and Note.
3. **The order detail** shows Sub Total 660, **Discount** 0, Service Charge 60, Non-Taxable 0, Taxable 720, VAT 93.6:
   the discount sits before the service charge.
4. **The payment screen needs an open session**, so it was read from the client bundle (chunk `1295-c0df25e851357c82`).
   Both the whole bill and a split send the **order's** `discount`/`discount_type` (`state.orderList.discount`, the
   same state *Options > Discount* sets) and the order's `contact_id`, falling back to the default contact. After a
   split the order is refreshed from the server's answer, discount included; what the server does with an order
   discount across splits could not be seen without a write.
5. **The arithmetic** (`_app`'s totals helper): a line discount is a percent of the line or an **amount per unit**; the
   order's is a percent, or an amount spread over the lines by their net with the residue on the first; service charge
   on what is left; VAT on that plus the service charge. Paisa are **truncated** (`parseInt`), not rounded.
6. **The gate.** The Discount menu item and the line's discount field show only when `XM("discount_limit")` holds,
   which is **true for an Admin** and otherwise the role's switch for the current location: the role editor's
   location section has *Can Give Discount* (`discount_limit`), *Can Remove Service Charge* and *Can Change Rate*.
7. **The customer is the order's.** The customer chip opens *Customers* (one select, *View More*, Update), which writes
   `POST /pos/orders/{id}/update-customer`. There is no per-bill customer: a second guest's part is billed by changing
   the order's customer between splits.
8. **Credit** is a tender with no payment mode (`{type: "Credit", amount}`), the amount still due, with no check
   against the walk-in (phase 59's defect 5; phase 61 Decision G refuses it here).

## 1. What shipped

**Domain.**
- `PosOrder.DiscountPct`, `PosOrder.ServiceChargeWaived`, `PosOrderLine.DiscountPct`; `SetDiscount`, `SetLineDiscount`,
  `WaiveServiceCharge`, each refusing a change once anything it prices is billed (a 409 through `PosOrderCommands`) and
  treating the value already there as no change. Percentages are 0 to 100 with at most two decimal places.
- `PosOrder.FiguresOf(line, quantity)`: the one place an order line is priced, with its own discount, the order's, and
  `ServiceChargeRateOf` (none when waived). `PosOrderLine.Figures` is **deleted**, so the compiler listed every caller:
  the estimate, the planner's three sites and the order view.
- A take-away parcel keeps its line's discount (and a sibling whose discount changed since is not joined). A transfer
  carries the line's discount, and the target takes the source's order terms when it has nothing on it and none of its
  own; otherwise they must already match.
- `Invoice.AddPosOrderLine` takes the line's discount and no longer refuses a header discount; `InvoiceLine.CreatePosOrderPart`
  stores it.

**Application.**
- `UpdatePosOrderDiscountCommand`, `UpdatePosOrderLineDiscountCommand`, `UpdatePosOrderServiceChargeCommand`:
  `Pos.Order.Operate` in the pipeline, then `PosOrderTerms.LoadForChangeAsync` (the order at its own till, then
  `Sales.Invoice.Approve` at its location). The fields are nullable and required, so an absent or misspelt one is a 400
  naming it, never a silent 0 or "charge it".
- The bill (`CreatePosOrderInvoiceCommandHandler`) makes the order's discount the invoice's header `DiscountPct`, each
  line's its line `DiscountPct`, and the line's billed service-charge rate its `ServiceChargeRate`. The refund planner,
  the receipt and the sales readers already rebuild every discount from exactly those fields.
- DTOs: the order and its lines carry the terms and `Gross`; the bill preview carries `Gross`, `Discount`, the order's
  terms, and each line's billed service-charge rate. `PosRestaurantDto.CanDiscount` and `PosTillDto.CanDiscount`.
- `CreatePosSaleCommandHandler.EnsureMayLowerThePriceAsync`: a discount, or a line below `ProductPrices.CatalogueRate`
  for its unit, re-checks `Sales.Invoice.Approve` at the till's location. `CatalogueRate` is new and `PosOrderPricing`
  prices order lines through it.

**Api.** The three `PUT`s above.

**Angular.**
- `shared/pos/pos-customer-search`: the search box and results, lifted out of the Retail till and the order screen when
  the bill screen became the third to need them, with `returnFocusTo`.
- The order screen: *Discount* (the order's), *Waive / Restore Service Charge*, a line's *Discount*, each a panel in the
  screen's own pattern, offered while nothing they price is billed and only with `canDiscount`; Gross, Discount and
  "Service charge Waived" in the totals; a line's "N% off".
- The bill screen: **Bill to** (the order's customer, *Change Customer*, *Use Walk-in*), the credit checkbox under the
  Retail till's refusals, the whole bill on account allowed, a bill of nothing taken with no tender; Gross/Discount and
  the waiver in the part's totals.
- The estimate bill prints "less N% discount", Gross, Discount and a waived service charge, as the receipt does.
- The Retail till says before Complete why a discount or a cut price needs Approve, and will not complete it.

## 2. Scope decisions

**A. A discount is a term of the order, fixed when billing starts (the user).** The vendor keeps both its discounts on
the order, and phase 65's invariant (the bills add up to the order) holds only if every part is priced at the same
terms. So the order's discount and waiver are fixed at its first bill, and a line's discount at its own first part (a
dish not yet billed can still be discounted after its neighbours are paid). A void of every bill unfixes them. The
cost, accepted: no discount on one guest's part once splitting has begun. A part priced per bill would have replaced
the invariant with a weaker one, and the vendor's own server behaviour there could not be seen.

**B. Percent only (the user).** Exact on every part, and what the refund and report paths already handle. An amount
needs an absolute discount on the order line, the planner spreading it across parts and the refund planner refunding at
the charged price; it is recorded as a divergence, to be done for both tills together if wanted.

**C. Lowering a bill needs `Sales.Invoice.Approve` at the location, at both tills (the user).**
- Phase 61 Decision F's argument for credit applies to a price: a till sale is approved when it is rung up, and the
  drawer's count, which is the session's control, cannot catch a discount because the cash matches the lowered bill.
- **Where it is checked.** Restaurant: when the term is set, not when the bill is paid, so a manager approves a table's
  discount and a cashier without Approve can still take the payment. Retail: on the sale, because the cart lives in the
  browser.
- **Why the handler, not the pipeline.** The location is the loaded order's, which the pipeline cannot resolve from an
  order id; so a missing order is the handler's 404 and a real one outside the grant a 403 naming the key (phase 31's
  both-directions proof). The pipeline still refuses a caller without `Pos.Order.Operate` before anything is loaded.
- **The price edit (the user's further answer).** A Retail line below its unit's catalogue rate needs the same key;
  raising a price, or keeping it, needs nothing. The restaurant needs no such rule: its order lines carry no rate.
- A default Member keeps ringing up sales at the catalogue price and loses discounting at the Retail till until an
  Admin grants Approve there. The vendor's switch is off for a non-Admin by default too.

**D. The waiver is the tab's, and needs the same key (the user).** A guest may refuse a service charge, so the till must
be able to honour it, and it lowers the bill as a discount does (the vendor pairs the two switches). Each line keeps
its frozen rate; the order's flag decides whether it is charged, so restoring it before the first bill is exact.

**E. A bill's customer is chosen per bill, and the order keeps its own.** The vendor writes the order's customer between
splits; here the bill names its buyer (`contactId`, already on phase 65's command) and the next part defaults to the
order's again. A named buyer gets a full tax invoice (phase 62 Decision A) and may leave the rest on account, under
phase 61's rules: never the walk-in, Approve at the location, and the credit limit on the credit part. Unlike the Retail
till, the whole bill may go on account with nothing handed over, as a guest charging it to a company does; the server
always allowed it.

**F. A transfer carries the tab's terms (Domain).** See TL;DR 3. A new table's order takes the source's discount and
waiver; an occupied table on other terms is a 409 naming both, and the way out is to give both orders the same terms.

**G. One customer search, three screens.** The bill screen would have been the third copy, so the search moved to
`shared/pos` and the till and order screens use it (CLAUDE.md: retire copies 1..N before writing copy N+1). Each screen
keeps its own button and wording.

**H. The till says it first, and the inputs stay usable.** The Retail till's discount and price fields are not
disabled for a cashier without Approve: a recalled held cart may already carry a discount, and a disabled field could
not be put back. It says why in the cart and on the payment screen and will not complete, as it does for credit.

**I. A bill of nothing settles the table.** A fully comped order (every item 100% off) is billed at 0 with no tender;
the food was served and has to be on a bill. Pinned by a handler test.

**J. No new permission keys** (C), and no seed migration.

## 3. Bugs found and fixed

1. **Choosing a customer dropped keyboard focus onto the page** (TL;DR 4), on the bill screen in this phase's keyboard
   pass and, it turned out, on the Retail till and the order screen since phases 62 and 64. The shared search now takes
   `returnFocusTo`, and *Use Walk-in* sends focus back to *Change Customer*. Pinned by the component's spec.
2. **An absent field would have been a silent value.** The first endpoint draft read `discountPct ?? 0` and
   `waived ?? false`, so a misspelt request would have removed a discount or restored a service charge with a 200. The
   commands' fields are now nullable and required (a 400 naming the field), proven in the E2E.
3. **A floating-point check would have refused 10.1%.** The order screen's first rule compared `Math.round(p * 100)`
   with `p * 100`, and 10.1 × 100 is 1009.999…; it now compares `Number(p.toFixed(2))` with `p`. Caught on reading,
   before any test ran.
4. **A heredoc ate a backslash again**: `'The item\'s discount…'` arrived without it and would not compile. The later
   patches went through scripts written by `Write` (the known gotcha, met once more).

## 4. Evidence

**Suites** (Docker up, one at a time): Domain 959, Application.UnitTests 1665, Infrastructure.UnitTests 13,
Api.IntegrationTests 46, Angular 811 (99 files). `ng build` green; initial bundle 652.58 kB.

**E2E** (fresh organization `1fc9c5ae-135e-48d2-8e4f-7a7db113d46f`, VAT-registered, HeadOffice in Retail mode and
`R01` in Restaurant mode; seeded through the API, every status printed and every figure asserted).

- **A — a discounted order in two parts.** Chicken Momo × 2 (200, 10% service charge) and Coke × 2 (60): 10% off the
  momo, 5% off the order. The order reads Gross 520, Discount 64, Amount 456, **553.93**. A body without `discountPct`
  is a **400 naming `DiscountPct`**; 12.345% is a 400. The preview of one of each is **277** (Gross 260, Discount 32)
  of an order total of **554**. Part one billed to **Acme Retail** with Card 100: **277, 177 on credit**, a full tax
  invoice. Then a new discount and a waiver are each a **409**, and the same 5% again is a 200. The rest to the
  walk-in: **277**, the order settled.
- **B — the waiver.** Waived: service charge 0, total 587.60; a body without `waived` is a 400; billed whole at
  **588** with no service charge.
- **C — transfers.** T3 at 5% transfers one momo to the free T4: the new order **takes 5%**, and the dish is **236.17**
  on either tab. A transfer onto T1's order at no discount is a **409**: "Order ORD0005 is priced with no discount and
  service charge, and order ORD0003 with a 5% discount and service charge…".
- **The reads.** `canDiscount` is true for the Admin on both tills.

**403-not-404 and the key in both directions.**
- **Pipeline:** `PUT /pos/orders/{nonexistent}/discount` is **404** "Order not found." as Admin, and **403 naming
  `Pos.Order.Operate`** after the same user is moved to a role holding `Pos.Order.View` and
  `Tenancy.BillingLocation.View`, beside a **200** on `GET /billing-locations`.
- **Handler:** with a role holding everything a waiter needs but not `Sales.Invoice.Approve`, the nonexistent order is
  **404**, and on a real open order the order discount, the line discount and the waiver are each **403 naming
  `Sales.Invoice.Approve`**. Both reads say `canDiscount: false`.
- **Retail till**, same role: momo at 200 is **200** (249), a 10% line discount, a 10% bill discount and a cut to 190
  are each **403 naming `Sales.Invoice.Approve`**, and raising it to 210 is **200** (261). Restored to Admin in SQL, the
  10% line discount is **200** (224).

**In SQL** (`sqlcmd`, Windows authentication):
- `pos.PosOrders`: ORD0001 `DiscountPct 5.00`, ORD0002 `ServiceChargeWaived 1`, the transfer's ORD0004 `5.00`, ORD0005
  `.00`. `pos.PosOrderLines`: ORD0001's momo `10.00`, service-charge rate `10.00` (frozen).
- `sales.Invoices`: 0001 Acme, header `5.0000`, 277 rebuilt from its lines, 100 settled; 0002 the walk-in, 277; 0003
  588 with every line `ServiceChargeRate .00`. The momo's VAT is **24.45 / 24.46** across the two parts: the last part
  takes what is left of the line's 48.91. The retail sales are 0004 (249), 0005 (210, 261) and 0006 (line 10%, 224);
  the three refused sales wrote **no** invoice.
- Accounts Receivable's balance is **177.00**, Acme's credit. Service charge income has no row for 0003.
- `workflow.Audits` has Update rows for each term change on ORD0001–0003, and none for the refused attempts.

**Browser and keyboard pass** (`erp-web-ssl`, the cookie set from a file written by a script and deleted at once).
- **Order screen** (ORD0003): *Discount (5%)*, *Waive Service Charge*, the line's *Discount*; totals Gross 200.00,
  Discount (5% on the order) 10.00, Amount 190.00, Service charge 19.00, VAT 27.17, Estimate 236.17. By keyboard: Tab
  from *Print Estimate* reaches *Discount (5%)* (`aria-expanded` false, `aria-controls` the panel); Enter opens it with
  focus in the labelled, described input; typing 10 and Enter saves, closes the panel, puts focus back on the button
  (now *Discount (10%)*) and announces "A 10% discount is on the whole order."; totals 223.74. The waiver panel opens
  with focus on its button, described by the ruling, and Cancel returns focus to its opener.
- **Bill screen** (ORD0005, a Coke, due 68.00): *Change Customer*, Tab, type "Acme", Tab, Enter chooses **Acme Retail**
  and, after bug 1's fix, **focus is back on *Change Customer***. Arrow keys choose Till Card, the amount field takes
  20 with Enter, "Leave 48.00 on Acme Retail's account" appears and *Take Payment* stays disabled until Space ticks it,
  Enter pays: "Bill 0007 paid, 68.00. 48.00 left on Acme Retail's account. The order is settled and the table is free."
  The receipt: Tax Invoice, Acme Retail, Lazimpat, Buyer PAN 609876543, Till Card 20.00, **On credit 48.00**.
- **Paper:** ORD0003's estimate at 10% on the order (its momo has no discount of its own) prints Gross 200.00,
  Discount (10%) 20.00, Sub Total 180.00, Service Charge 18.00, VAT 25.74, Round Off 0.26, Estimated Total 224.00, and
  still says it is not a tax invoice. Bill 0001's receipt reads Gross 260.00, Discount 32.00, Sub Total 228.00, the
  momo's 10%, and 177.00 on credit.
- The console held no application error.

## 5. Left open, and for later phases

- **A remainder line refunds a paisa apart.** The last part of an order line stores what is left of its money (24.46 of
  VAT where a fresh figure is 24.45). A refund prices fresh and takes the rest of the sale through its round-off, so the
  refunded total is exact while the VAT account can keep a paisa that the Rounding account absorbs. True since phase
  65's equal splits; not changed here. A refund that took a remainder line's own figures would close it.
- **Amount discounts** (Decision B), at both tills together, if wanted.
- **A discount on one guest's part after splitting has begun** (Decision A's cost).
- **The vendor's *Can Change Rate***: the Retail till's raised price stays open to any cashier; only a lowered one is
  gated (Decision C).
- **An order discount's own audit field**: the Audit row says Update on the order, not which term changed.
- Carried unchanged: phase 71 § 5, phase 70 § 5 (the full-tenant export lacks service charge, round-off and the
  packaging fee; and now the order terms), phase 69 § 5, phase 67 § 5, phase 66 § 5, phase 63 § 5, phase 62 § 5.

## Writes made to the vendor tenant

None. Every API request the page made was logged and anything other than a GET would have been refused in the page; 21
were made, all GETs. *Start Session* was cancelled and no order was updated.
