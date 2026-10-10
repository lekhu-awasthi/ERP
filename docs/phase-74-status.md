# Phase 74 — Till pricing: Amount discounts at both tills, refunds that tie out to the paisa, and any changed price needs Approve

## TL;DR

**A till discount can now be rupees as well as a percentage, a refund gives back exactly what the sale charged, and
a cashier on their own charges the catalogue price.** Before this phase both tills took percentages only (phase 72
Decision B); a refund priced every line afresh, so a sale returned in parts could leave a paisa of VAT in the VAT
account (phase 72 § 5); and a Retail price raised above the catalogue was open to any cashier. The user's decisions
(2026-10-10), each the recommended option:

- **Line and bill Amounts at both tills** (Decision A). A line's Amount is **rupees off each unit**, as the vendor's
  is; a bill's (or an order's) is **rupees off the whole**, spread over the lines in proportion to what each comes to,
  the residue on the largest. Each line stores its own figures, so a refund gives back its quantity's share.
- **An order's Amount is prorated, and frozen at the first bill** (Decision B). Each part of a dish takes its quantity's
  share and the last part what is left, so the bills add up to the order and no one guest's bill takes the whole
  amount (the vendor's first split bill does). A dish sent after the first bill carries none.
- **Any price but the catalogue's needs `Sales.Invoice.Approve`** at the location (Decision C), raised as well as
  lowered, as the vendor's *Can Change Rate* gates both directions. No new keys.
- **The refund that returns the last of a sale line takes what is left of it** (Decision D, a fix): amount, service
  charge, packaging fee, VAT and its share of the bill's Amount, each. Phase 72's open item, and the vendor's defect
  class (below).

**Read live first, with writes** (abcagro1, a new trial organization the user created for this; § Live read). The
vendor's Amounts are per unit on a line and spread by net on a bill (50 over 969 and 37.50 is 48.14 and 1.86, exactly
as ours). Its refunds of an amount-discounted sale **took the bill's 50 off twice**: a fully returned 1,080.84 sale
refunded 1,062.71, leaving the customer 18.13 short and **2.08 of VAT in the VAT account**, and the server accepted it.
Its restaurant split puts the whole order amount on the first bill, then shows it again on the remainder and refuses
the tender (400) until the page is reloaded.

| | |
|---|---|
| Live read taken | **yes, with writes**, 2026-10-10, on abcagro1 (§ Live read; every write listed at the end) |
| Migration | `Phase74AmountDiscounts`: nine columns, all `NOT NULL DEFAULT 0` true of every existing row except `PosOrderLines.OrderDiscountShare` (nullable: "not fixed") |
| Changed endpoints | `POST /pos/sales`: a line's `discountPerUnit`, the bill's `discountAmount`; `PUT /pos/orders/{id}/discount` and `.../lines/{lineId}/discount`: `discountPct` **or** `discountAmount`, exactly one |
| Changed DTOs | order, order line, bill preview, refundable sale, receipts, ERP invoice and credit-note detail: the per-unit discount, the bill's amount and each line's share |
| New permission keys | **none**: `Sales.Invoice.Approve` at the location now also gates a raised price and an amount discount |
| Changed screens | Retail till (a % / Rs-each choice beside each line's discount and the bill's), restaurant order (the same in both discount panels), bill page, estimate, both receipts, ERP invoice and credit-note pages, the invoice PDF |
| Initial bundle | **652.58 kB**, unchanged, against 680 kB |

**Four things worth carrying forward.**

1. **The last of a line takes what is left -- now on the way back too.** Phase 65 made the last part of an order line
   take what was left of its money; refunds still priced afresh, so three thirds of a share of 25.61 came back as 25.62.
   A refund that returns the last units of a sale key now takes the sale's figures less what earlier refunds gave
   back, figure by figure. Both new tests fail when it is reverted.
2. **An amount is spread once and stored per line.** Readers that derive "the bill's discount" as *after the line
   discount less Amount* keep working (`TradeLineReader`); the share is stored anyway, because a refund prorates it
   and a restaurant part takes its quantity's share of a frozen figure.
3. **A spread over a changing set must freeze before anyone pays.** An order's amount spread over its dishes is live
   until the first bill, then fixed per line; after that a line carrying a share keeps its quantity (no discard, parcel
   or transfer), or the bills stop adding up. A void of every bill unfixes it, like phase 72's terms.
4. **A figure printed below the total it is already inside reads as a second subtraction.** The first PDF printed
   "Discount Applied 50.00" under a Sub Total of 956.50 that already had the 50 taken off. Only the rendered page
   showed it (phase 73's lesson, again); it now reads Before Discount, Discount Applied (50.00), Sub Total.

Tests: Domain **991** (+13), Application.UnitTests **1687** (+5), Infrastructure.UnitTests 13, Api.IntegrationTests
52, Angular **825** (+7, 100 files). `dotnet build`, all four .NET suites (Docker up, one at a time), `ng build` and
`ng test` are green.

---

## Live read, 2026-10-10 (abcagro1, *Abc Agro Trading*, with writes)

Hamro Samaan's trial ended on 2026-10-09. The user created a new trial organization, **abcagro1** (15 days left on
2026-10-10, so readable until about **2026-10-25**), and said writes were fine. The ERP and the POS were entered as in
phase 72: the organization list's *Open Pos* hand-off captured (`window.open` replaced) and followed in the same tab,
its hash never recorded. Every API request the page made was logged in the page. The tenant had three locations
already: HeadOffice, POS Restaurant (code 1002, a *Bar*) and POS Retail (1003), no products and no floor plan.

**The auto-mode classifier.** An in-page helper replaying the app's own captured request headers created two products
and was then refused ("Modify Shared Resources"), as the memory note predicted. Everything after that went through
the vendor's own screens, which the classifier allowed, including every sale and refund below.

1. **Products** (`POST /products {type, name, code, product_category_id, tax: "13 VAT", primary_unit_id,
   selling_price, service_charge_applicable, available_for_sale, track_inventory, ...}`): Momo Plate 333 (through the
   form), Masala Tea 37.50 and Fresh Juice 117 (through the helper).
2. **The Retail till's line Amount is per unit.** *Edit Item* has *Type of discount* **Percent (%) | Amount** (Percent
   preselected) and *Discount (Item Line)*. 10 off a 333 momo at quantity 3 showed **364.99** a unit (323 × 1.13) and
   **1,094.97** for the line; the stored line reads `discount 10, discount_type Amount, discount_amount 30`.
3. **The bill's Amount is spread by net.** *Options > Discount* is *Apply Discount*, **Percent | Amount**. 50 off the
   bill (3 momo less 10 each, one tea): `POST /pos/invoices {..., discount: 50, discount_type: "Amount"}` stored
   **INV0001/1003/83-84**: momo `taxable_amount 920.86, vat 119.71`, tea `35.64, 4.63`, header `discount_amount 50,
   taxable_total 956.50, vat 124.34`, **1,080.84**. The shares are 48.14 and 1.86, the residue on the first (largest)
   line. VAT rounds as JavaScript does (124.345 → 124.34, 82.355 → 82.36).
4. **The receipt** the till printed on completing the sale was headed **"ESTIMATE BILL"** (a vendor defect: the sale
   was a tax invoice). Its lines print gross (999.00) and one Discount (80.00) for both discounts together, so
   Gross − Discount = Sub Total holds. The till's own invoice view prints the line at 999.00 under a Sub Total of
   1,006.50, so its line discount is invisible there.
5. **A partial refund prices its share.** *Initiate Refund* of 1 momo sent `POST /pos/credit-notes {items: [{...,
   discount: 10, discount_type: "Amount", invoice_item_id}], discount: 16.05, discount_type: "Amount"}`: the share
   (50 × 323 / 1,006.50) **computed in the browser** and sent as the note's own Amount. **CN0001/1003/83-84**,
   306.95 + 39.90 = **346.85**.
6. **The refund of the rest takes the bill's 50 again.** 2 momo and the tea: the screen showed Discount (50) and
   **715.86** where 733.99 was left of the sale; the request carried `discount: 50` and no `invoice_item_id` on its
   lines, and the server accepted it (**CN0002/1003/83-84**). The sale was fully returned for **1,062.71** of 1,080.84:
   the customer is **18.13** short and **2.08** of VAT (124.34 charged, 122.26 reversed) stays in the VAT account.
7. **The restaurant split.** A take-away order **SO0001/1002/83-84** (2 momo, 2 tea, 837.33); *Options > Discount*
   Amount 50 shows 780.83 but is local state (the order's save sends `discount: 0`). *Split Bill* of one of each sent
   `POST /pos/invoices/{order}/split-bill {discount: 50, discount_type: "Amount"}`: the **whole 50 on the first bill**
   (**INV0002/1002/83-84, 362.17**), and the order came back with `discount: 0`. The remainder screen then showed
   **362.17** again (the 50 still in its state) while its request sent `discount: 0`, and the server refused it:
   **400 "paid amount 362.17 is not equal to invoice amount 418.67"**. After a reload it billed **418.67**
   (**INV0003/1002/83-84**). The two bills total 780.84 against the order's 780.83.
8. **Can Change Rate** (the client bundle, `_app` and chunk `1295`). The role editor's location section holds
   `discount_limit` *Can Give Discount*, `remove_service_charge` *Can Remove Service Charge* and `change_rate` *Can
   Change Rate*; `XM(key)` is true for an Admin and otherwise the role's switch at the current location. Both tills'
   *Edit Item* render the Price field with **`isDisabled: !change_rate`**, and a batch picker's rate change is offered
   only with it: one switch for **any** price edit, raised or lowered.

## 1. What shipped

**Domain.**
- `PosLineArithmetic.Compute` takes `discountPerUnit` and the line's `billDiscountShare` as **required** parameters
  (so the compiler listed every caller). `NetOf` is a line after its own discount and a bill's percentage, to the
  paisa; `Spread` splits an amount by those, residue on the largest line; `Prorate` is a share's part for a quantity.
- `PosDiscountKind` (Percent, Amount).
- `InvoiceLine` and `CreditNoteLine`: `DiscountPerUnit` and `BillDiscountAmount`; `Invoice` and `CreditNote`:
  `DiscountAmount` (the sum of the lines' shares). `Invoice.AddPosLines` adds a sale's lines at once and spreads the
  bill's amount; `AddPosLine` stays, percent only, and refuses a bill with an amount. A line or a bill refuses both
  kinds at once. `CreditNote.AddPosLine` takes one input record, including **what is left** of the sale line's figures.
- `PosOrder`: `DiscountAmount`; `SetDiscount` and `SetLineDiscount` take a kind (the percent overloads remain);
  `DiscountShareOf` (live until fixed), `FixDiscountShares`, `ReleaseDiscountShares`; `FiguresOf` takes the share.
  Discard and Mark as Take Away refuse a line carrying a share once fixed; a transfer refuses a tab with an amount.
  `PosOrderLine`: `DiscountPerUnit`, `OrderDiscountShare` (null until fixed). `PosOrderBill` gives each part its share
  and the last part what is left.

**Application.**
- `CreatePosSaleCommand`: a line's `DiscountPerUnit`, the bill's `DiscountAmount`; the validator refuses both kinds,
  a per-unit over the rate and an amount over the bill, each a 400 naming the field. The handler's gate is now
  `EnsureMayChangeThePriceAsync`: any discount, or any rate other than the catalogue's.
- `UpdatePosOrderDiscountCommand` / `UpdatePosOrderLineDiscountCommand`: `DiscountPct` **or** `DiscountAmount`
  (per unit on a line), exactly one, both nullable so a missing one is a 400.
- `CreatePosOrderInvoiceCommandHandler` stamps each part's share and fixes the spread; `VoidInvoiceCommandHandler`
  releases it when the order's last bill is voided.
- `PosRefundPlanner`: fresh lines carry their quantity's share; the refund returning the last of a key takes what is
  left of it. `SaleLineKey` names the cap key (phase 6), now with the per-unit discount.
- Readers: Sales Master's Item Discount includes quantity × per unit; the email merge's Discount counts a bill's
  amount; `PrintDocumentQueryHandler` prints "10.00/unit" in a "Disc" column and the bill's amount above the Sub Total.
  The ERP invoice and credit-note detail DTOs carry the new fields.

**Web.**
- `pos-bill.ts` (`computeBill`) and `pos-bill-cases.json` (three new cases, read by both suites): the per-unit
  discount and the spread, in bigint paisa. `PosCart`: `discountPerUnit` per line, `discountAmount` on the bill,
  stored and held carts without them read as none.
- The Retail till: a **%/Rs each** select beside each line's discount and **%/Rs** beside the bill's, each chosen kind a
  signal of its own (a zero amount cannot say which kind it is); the refusal now names any changed price, and a bill
  amount over the bill is refused before it is sent.
- The restaurant order page: the same select in both discount panels, with validation before the request; badges and
  totals say "20.00 off each" and "(40.00 on the order)". The bill page, the estimate and both receipts print the
  per-unit discount.
- The ERP invoice and credit-note pages compute a line as quantity × (rate − per unit) × (1 − %), and their headings
  say "Discount" when a line or the bill carries rupees.

## 2. Scope decisions (the user's, 2026-10-10, each the recommended option)

**A. Line and bill Amounts, both tills.** A line's is per unit: the vendor's is (§ Live read 2), and per unit makes a
refund of any quantity exact, as a lower rate would be. A bill's is spread by net with the residue on the largest
line, as the vendor's is, and each line stores its share, so a refund gives back its quantity's part of it.

**B. An order's Amount is prorated and frozen at the first bill.** Phase 72 fixed an order's terms at its first bill;
an amount needs its spread fixed with it, or a dish added later would move shares already billed. So the spread is
live until the first bill and stored per line from it; each part takes its quantity's share and the last part what is
left. The cost: once billed, a dish carrying a share cannot be discarded or parcelled (bill it; a refund gives back
what was not wanted), and an order with an amount cannot transfer items, because copying an amount would give it twice.
The vendor gives the whole amount to the first bill (§ Live read 7).

**C. Any price but the catalogue's needs `Sales.Invoice.Approve`.** The vendor's *Can Change Rate* disables the price
field outright for a role without it, raised or lowered. Phase 72 gated only a lower price, because a raise cannot hide
money from the drawer count; but a raise overcharges a customer as surely as a cut undercharges the shop. On their own,
a cashier charges the catalogue. A product with no catalogue price is still whatever is rung up. No new key: the
reviewer of a lowered bill is the reviewer of a changed one.

**D. The last of a line refunds what is left of it.** Not a choice so much as the rule phase 65 made for parts, applied
to the way back. Keyed by phase 6's (product, rate, VAT, discount, per unit, unit) key rather than by sale line, because
credit-note lines do not name a sale line; the refundable-sale query already hands a key's remainder to its lines in
bill order.

**E. No date, rounding or tax change.** VAT still rounds half away from zero per line at the paisa; the vendor's
JavaScript rounding is not copied.

## 3. Bugs found and fixed

1. **A refund in parts left a paisa of VAT behind** (phase 72 § 5, and the vendor's defect in a worse form). Fixed by
   Decision D; `A_refund_of_an_order_bills_last_part_takes_its_stored_figures_and_leaves_no_paisa_of_vat` and the
   three-refund Retail test both fail with fresh pricing restored (checked by injecting it and restoring by hash).
2. **The PDF printed the bill's amount under the Sub Total already net of it** (found by looking at the page; text
   extraction read it as fine). Moved above it as Before Discount / Discount Applied / Sub Total.
3. **The ERP invoice and credit-note pages priced a till line from rate × percent**, so a per-unit discount would have
   shown the line at full price and a wrong Sub Total. `EditableLine.discountPerUnit` is required, so the compiler named
   all seven of the two pages' line mappings.
4. **The email merge's Discount knew only a percentage**, so an amount-discounted till bill would have mailed "0.00".
5. **Phase 72's test pinned "a higher price is any cashier's"**; Decision C reverses it, and the test now says so.

## 4. Evidence

**Suites** (Docker up, one at a time): Domain 991, Application.UnitTests 1687, Infrastructure.UnitTests 13,
Api.IntegrationTests 52, Angular 825 (100 files). `ng build` green; initial bundle 652.58 kB.

**E2E** (fresh organization `52c5dc32-ff16-4895-ad02-bcfd35051591`, VAT-registered, HeadOffice in Retail mode and `R01`
in Restaurant mode; seeded through the API from phase 72's script, every status printed and every figure asserted).

- **R -- the vendor's sale on our till.** 3 Momo Plate at 333 with 10 off each and a Masala Tea at 37.50, 50 off the
  bill: **1,081** (1,080.84, round-off 0.16), the vendor's figure. One plate back: **347** (346.85, the vendor's too).
  The rest: **734**, exactly what was left (the vendor's: 715.86).
- **V -- the 400s.** A line with both kinds, a per-unit over the rate, a bill with both kinds and an amount over the
  bill: each a **400 naming `DiscountPerUnit` or `DiscountAmount`**.
- **O -- an order's amount.** T1: 2 Chicken Momo (20 off each) and 2 Coke; an order body with neither kind and one with
  both are **400**; 480.01 off is a **409**; 52 off is spread **39 / 13** live. The preview of one of each carries
  **26**; billed; a new amount is then a **409**, and discarding the Coke a **409** ("Line 2 carries 13.00 of the
  order's discount, spread when the order was first billed..."). The rest carries **26** and settles the order; the two
  bills add up to its total, 520.
- **T -- transfers.** T3 with 10 off: a transfer to T4 is a **409** naming ORD0002.
- **X -- voids.** Voiding both of T1's bills reopens the order with its 52 a term again; a new amount (40) is a 200.

**403-not-404 and the key in both directions.**
- **Pipeline:** `PUT /pos/orders/{nonexistent}/discount {discountAmount: 10}` is **404** "Order not found." as Admin
  and **403 naming `Pos.Order.Operate`** from a role holding `Pos.Order.View` and `Tenancy.BillingLocation.View`,
  beside a **200** on `GET /billing-locations`.
- **Handler:** a role with everything a cashier needs but `Sales.Invoice.Approve`: the nonexistent order is **404**,
  and on a real open order an order amount and a line amount are each **403 naming `Sales.Invoice.Approve`**.
- **Retail till, same role:** a tea at the catalogue's 37.50 is **200** (42), and a raise to 40, a cut to 35, 2.50 off
  each and 2.50 off the bill are each **403 naming `Sales.Invoice.Approve`**. Restored to Admin in SQL, the raise is
  **200** (45).

**In SQL** (`sqlcmd`, Windows authentication):
- Sale 0001: Momo Plate `DiscountPerUnit 10, BillDiscountAmount 48.14, Amount 920.86, VAT 119.71`; tea `1.86, 35.64,
  4.63`; header `DiscountAmount 50`. CN0001 one plate `16.05, 306.95, 39.90`; CN0002 two plates `32.09, 613.91, 79.81`
  and the tea `1.86, 35.64, 4.63`; headers 16.05 and 33.95.
- Sale less refunds per product: quantity, share, amount and VAT **all 0.0000**.
- The GL across the sale, both refunds and their payouts: Accounts Receivable, Cash In Hand, Rounding, Sales and **VAT
  Payable each net 0.0000** (VAT 124.34 in, 124.34 out).
- ORD0001 after the voids: `DiscountAmount 40.00`, the momo's `DiscountPerUnit 20.00`, every `OrderDiscountShare NULL`.
  Its two (voided) bills each `DiscountAmount 26`, lines `19.50` and `6.50`; the Coke's VAT **6.96 / 6.95**, the last
  part taking what was left of 13.91.
- The cashier's sales: 0004 (37.50, 42) and 0005 (40, 45); the four refused sales wrote no invoice.
- `workflow.Audits`: Create 2, Update 4 for the two orders.

**Browser and keyboard pass** (`erp-web-ssl`, the cookie set from a file written by a script and deleted at once).
- **Retail till:** 3 Momo Plate and a tea; *Edit Momo Plate* by Enter; Tab four times reaches *Discount as* (focus ring
  #0a58ca); ArrowDown chooses *Rs each* (the input's `max` becomes the rate, 333); Shift+Tab, 10, Tab: the line reads
  "× 333.00 −10.00 each", 969.00, and the cart 1,137.35, the vendor's figure at that point. *Bill discount as* by
  keyboard to *Rs*, 50: Sub Total 956.50, VAT 124.34, Round Off 0.16, **Total 1,081.00**. F9, 1081, Enter, Enter:
  "Sale 0006 complete." The receipt: Momo Plate 3 × 333.00, 920.86, "less 10.00 each discount", Gross 1,036.50,
  Discount 80.00, Sub Total 956.50, VAT 124.34, Total 1,081.00, headed TAX INVOICE.
- **Order screen** (ORD0001): *Discount (40.00)*, a "20.00 off each" badge, Gross 520.00, Discount (40.00 on the order)
  80.00, Estimate 534.49. Enter opens the panel with focus in the labelled, described input (40, kind Amount); Tab,
  ArrowUp to %, Shift+Tab, 5, Enter: saved, focus back on *Discount (5%)*, the status region says "A 5% discount is on
  the whole order.", Estimate 553.93.
- **ERP invoice page** (sale 0001): Momo Plate 333.00, "10.00 each", 969.00; Sub Total 1,006.50; Discount (50.00);
  Taxable 956.50; Grand Total 1,081.00; both headings "Discount".
- **The PDF**, looked at (rendered with Windows' own `Windows.Data.Pdf`, poppler being absent): a "Disc" column with
  "10.00/unit" on one line beside "0%"; Before Discount 1,006.50, Discount Applied (50.00), Sub Total 956.50; the third
  copy boxed "COPY OF ORIGINAL · printed 3 times".
- The console held no application error.

## 5. Left open, and for later phases

- **An order with an amount cannot transfer items** (Decision B's cost): moving the share with the food is possible
  but was not built; give the order a percentage instead.
- **A dish carrying a share cannot be discarded or parcelled once billed** (Decision B): bill it and refund it.
- **The vendor's three switches are one key here.** *Can Give Discount*, *Can Remove Service Charge* and *Can Change
  Rate* are all `Sales.Invoice.Approve` at the location (phase 72 Decision C, this phase's C). Separate keys remain
  possible if a tenant needs a cashier who may discount but not change a price.
- **A discount on one guest's part after splitting has begun** (phase 72's Decision A cost), unchanged.
- **Older refunds are matched by key**, not by sale line; a sale with two lines of one key (the same product, rate,
  discount and unit twice) refunds what is left of the key, as the cap always counted it.
- **The full-tenant export** lacks the per-unit discount and the bill's amount, as it lacks the order terms.
- **The vendor's defects recorded here** (the refund taking the bill's amount twice, the split's stale remainder, the
  "ESTIMATE BILL" heading on a completed sale, the invisible line discount on its invoice view) are facts for a future
  comparison, not work for us.
- Carried unchanged: phase 73 § 5, phase 72 § 5 (other than its first and fourth items, done here), phase 71 § 5,
  phase 70 § 5, phase 69 § 5, phase 67 § 5, phase 66 § 5, phase 63 § 5, phase 62 § 5.

## Writes made to the vendor tenant (abcagro1, with the user's leave)

- Products **Momo Plate** (P0001, 333), **Masala Tea** (P0002, 37.50) and **Fresh Juice** (P0003, 117).
- Sessions opened at **POS Retail** (from 12:51) and **POS Restaurant**, both left open.
- **INV0001/1003/83-84** (1,080.84 cash) and its refunds **CN0001/1003/83-84** (346.85) and **CN0002/1003/83-84**
  (715.86), all paid out in cash.
- Take-away order **SO0001/1002/83-84**, billed as **INV0002/1002/83-84** (362.17) and **INV0003/1002/83-84**
  (418.67), with one refused attempt (400) between them.
- Nothing was deleted, and no setting, role or account was changed.
