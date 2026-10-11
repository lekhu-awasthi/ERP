# Phase 83 — a discount on one guest's part, and three small fixes

## TL;DR

**A restaurant bill can now carry a discount of its own: one guest's, on their part only, after splitting has begun or
before it.** Phase 72 fixed an order's discount at its first bill, so that every part is priced at the same terms and
the bills add up to the order. The cost, recorded in phase 72 § 5, was that once one guest had paid, nobody else at the
table could be given a discount on what they ate. The only way round was to bill the guest and refund part of their
bill: a credit note in the VAT records for a price nobody charged.

The user's decisions (2026-10-11), each the recommended option:

- **A — the discount lives on the bill, not the order.** The bill screen takes a percentage of what the part comes to,
  or rupees off it, spread over the part's lines after every term of the order. The order's own terms stay fixed, so
  every other guest's part is priced as before, to the paisa. Each invoice line keeps the guest's share apart
  (`InvoiceLine.PartDiscountAmount`, inside `BillDiscountAmount`). The order's bill planner prices the **whole** of a
  line less what its bills not voided gave off it, so the last part of a dish never takes an earlier guest's discount
  back. A void of the discounted bill gives the discount up with nothing written back (phase 65's rule).
- **B — whoever takes the payment needs `Sales.Invoice.TillDiscount` at the order's location.** This is phase 76's key
  for a till discount, checked in the handler because the location is the loaded order's. The preview needs no key.
- **C — no live read.** Phases 72 and 74 read the vendor's split, phase 74 with writes. It has no per-part discount and
  puts the whole order amount on the first bill. Ours is a decision, not a copy.

Bundled at the user's request, the three small items from phases 81 and 82 § 5:

- **The ERP forms offer a clerk the removal of a discount** (phase 81 Decision F: removing one needs no key). A *Remove*
  button sits under each disabled discount on Quotation, Sales Order and Invoice drafts. Once used it says "Discount
  removed" and keeps keyboard focus.
- **The price note says "in this organization" when the document carries no location**, as a quotation does under the
  default scope. It reads this from the server's answer (`SalesPricePowersDto.AtLocation`), not from the form's empty
  location field.
- **The discard panel's maximum is what is unbilled** ("of 1 not yet billed; 1 billed is refunded, not discarded"), and a
  fully billed dish offers no Discard.

| | |
|---|---|
| Live read taken | **no**, by the user's choice (phase 74 § Live read 7 holds the vendor's split behaviour) |
| Migration | `Phase83PartDiscount`: `sales.InvoiceLines.PartDiscountAmount` decimal(18,4) NOT NULL DEFAULT 0, true of every existing line |
| New permission keys | **none**: `Sales.Invoice.TillDiscount` (phase 76) at the order's location, checked in the bill handler |
| Changed endpoints | `POST /pos/orders/{id}/bill/preview` and `POST /pos/orders/{id}/invoices` take `discountPct` **or** `discountAmount` (at most one, a 400 naming `DiscountAmount` for both); `GET /sales/price-powers` adds `atLocation` |
| Changed DTOs | bill preview `partDiscount` and each line's `partDiscountShare`; order `partDiscount` (and each line's), with `total` now what the bills will add up to |
| Changed screens | the restaurant bill page (*Discount on this bill*, % or Rs, disabled with the key named for a cashier without it); the order page (*Discounts on bills* beside the order's own discount; the discard cap); Quotation, Sales Order and Invoice forms (*Remove*); the shared price note |
| Changed print output | the ERP invoice PDF when a bill carries both kinds: *Discount Applied 10%*, *Before Bill Discount*, *Bill Discount*, *Sub Total*, in that order |
| Initial bundle | **652.58 kB**, unchanged, against 680 kB |

**Three things worth carrying forward.**

1. **A discount on one part needs the planner to know which part of a line's discount was the order's.** "The last
   part takes what is left" works only when everything already billed was priced at the same terms. A guest's
   discount breaks that unless the whole is priced less what the bills gave. That is one figure the reader sums over
   bills not voided (`PosOrderLineBilled.PartDiscountAmount`), so it is derived and never counted. The test is phase
   82's: a twin tab billed without the discount, and the next guest's bill identical on both.
2. **A figure printed below the total it is already inside reads as a second subtraction, and that includes a
   percentage.** The first PDF printed *Discount Applied 10%* under a Sub Total that already had the 10% in it, below a
   *Bill Discount* row. Only the rendered page showed it (phase 74's lesson, a second time). It now prints first.
3. **A control that takes itself away drops focus; so make it stay.** The first *Remove* button disappeared when the
   discount reached 0, and focus fell to `<body>`. The input beside it is disabled, so it cannot take focus instead.
   The button now stays as an `aria-disabled` "Discount removed" (phase 72's lesson, a third surface).

Tests: Domain **1028** (+9), Application.UnitTests **1765** (+5), Infrastructure.UnitTests 14, Api.IntegrationTests 71,
Angular **848** (+4, 102 files). `dotnet build`, all four .NET suites (Docker up, one at a time), `ng build` and
`ng test` are green.

---

## 1. What shipped

- **Domain.**
  - `PosPartDiscount(Kind, Value)` is the part's own discount.
  - `PosOrderBill.Plan` prices each chosen line at the order's terms first. It converts a percentage to rupees (to the
    paisa, of the part's sum), refuses more than the part, and spreads it by `PosLineArithmetic.Spread` over what each
    line comes to.
  - A part that is not a line's last is priced fresh with its share added. A last part is `WhatIsLeft`: the whole line
    at `FiguresOf(line, net, given before + this share)` less the bills' figures.
  - The order's running total (`OrderTotal`) prices the rest of each line the same way.
  - `PosOrderBillLine.PartDiscountShare` and `PosOrderBillPlan.PartDiscount` carry the result.
  - `PosOrderLineBilled.PartDiscountAmount` is the reader's second figure; `BillDiscountAmount` stays the order's share
    only, so `ShareCarriedBy` (transfer, parcel, discard) is unchanged.
  - `PosOrder.FiguresOf(line, quantity, partDiscount)` is the one pricing method, and the old overload calls it with 0.
  - `PosOrder.Estimate(billed)` prices each line less what its bills gave.
- **Invoice.**
  - `InvoiceLine.PartDiscountAmount` is never more than the line's `BillDiscountAmount`, which includes it.
  - `Invoice.AddPosOrderLine` takes it, and keeps phase 74's "a percentage or an amount, not both" for the **order's**
    share only.
  - `CreditNote` drops that rule: a refund gives back what its sale line charged, and the sale decided what may be
    combined.
- **Application.**
  - `PosOrderBilling` sums `BillDiscountAmount − PartDiscountAmount` and `PartDiscountAmount` per order line.
  - `PosOrderBillPart.DiscountOf(pct, amount)` maps a request (zero is none).
  - `CreatePosOrderInvoiceCommand` and `PreviewPosOrderBillQuery` take both fields, and their validators allow at most
    one, a valid percentage and whole non-negative paisa.
  - The bill handler checks `Sales.Invoice.TillDiscount` at the order's location before planning, only when a discount
    is given. It passes `BillDiscountShare + PartDiscountShare` as the line's bill discount, with the part kept apart.
  - `PosOrderView` prices lines and the estimate less what bills gave, and adds `PartDiscount`.
  - The Api's request records carry both fields, so they do not bind silently to null (phase 27b).
- **Print.** `PrintDocumentQueryHandler`: when a bill has both a percentage and an amount, the percentage prints first
  and the amount reads *Before Bill Discount* / *Bill Discount*. A bill with one kind prints as before.
- **Export.** `TillExportTests` names `PartDiscountAmount` as *inside Bill Discount* (Decision D).
- **Bill page.**
  - *Discount on this bill* (a number and a % / Rs select, kind kept in its own signal) is labelled and described.
  - It is disabled for a cashier without `canDiscount`, with the key named.
  - Every preview and the bill send it, and it is cleared after each payment.
  - The totals show the order's discount and this bill's apart, and the announcement names what came off.
- **Order page.**
  - *Discounts on bills* sits beside the order's own discount, which is shown net of it.
  - Discard is offered only while something is unbilled, its maximum is `toBill`, and its label says what is billed.
- **ERP forms** (Quotation, Sales Order, Invoice):
  - *Remove* under the disabled line discount and the document discount, through `removedDiscounts`.
  - The shared `SalesPriceNote` says "at this location" or "in this organization" from `atLocation`, and that a
    discount already there can still be removed.

## 2. Scope decisions

The user's (2026-10-11): A–C in the TL;DR.

- **Decision A — rejected alternatives.**
  - *Re-price the unbilled food:* let the order's discount change after the first bill for what is unbilled. That is
    every remaining guest's discount, not one guest's, and a dish split across two guests would be priced twice.
  - *A line discount on the unbilled remainder:* per dish, not per guest, and a dish shared by two guests could not be
    discounted for one of them.
- **Decision B — rejected: a manager sets a "next bill's discount" on the order beforehand.** A cashier without the key
  could then take the payment, but the order gains pending state that a void, a transfer and a second cashier would all
  have to reason about. Phase 72 checked the order's discount when it was set because it was a term of the order; this
  one is a term of the bill, so it is checked when the bill is made.

Decided in the session, not asked:

- **D — the export names the figure rather than adding a column.** `PartDiscountAmount` is already inside the exported
  *Bill Discount*. A column of its own would read 0 on the refund that gave it back, because a credit note line gives
  back its sale's bill discount whole and does not split it. Phase 77's rule (each part of a line's money owes a column
  or a named reason) is met by the reason.
- **E — a percentage becomes rupees on the bill.** The invoice already stores an Amount per line (phase 74), and a
  second percentage would need a third discount field on every reader. The bill prints the rupees.
- **F — the preview needs no key.** It lowers nothing, and a cashier without the key can show the guest what a manager
  would be asked to approve. The bill is checked.
- **G — the field clears after each payment.** The next part is another guest's, and a discount carried over by default
  would be given without anyone asking.
- **H — one PDF change, only for bills carrying both kinds.** A single kind prints exactly as before.
- **I — the price note's location comes from the server.** The form's location field is empty both when the type
  carries none and before one is picked on a type that does, while the server resolves the second to the default
  location. Only the server knows which applies, so it says (`AtLocation`).
- **J — no per-part service charge waiver.** A guest may decline the service charge on their part as well. That is the
  same shape, but it was not asked for; recorded in § 5.

## 3. Bugs found and fixed

In this phase's own work, before it shipped:

- **The PDF read as a second 10% off.** The first render printed *Discount Applied 10%* under a Sub Total that already
  included it. The percentage now prints first; a handler test asserts the order of the four rows.
- **The *Remove* button dropped keyboard focus.** It disappeared once the discount was 0 and focus fell to `<body>`. It
  now stays as an `aria-disabled` "Discount removed".

In the session's tools, both known gotchas:

- A Python heredoc in the Bash tool lost its backslashes, so the newline-preserving patch did not apply and three CRLF
  Domain files (`InvoiceLine.cs`, `Invoice.cs`, `CreditNote.cs`) were written LF. They were converted back and checked
  byte by byte. Later patches went through a `Write`-made helper that keeps each file's own newline.
- The E2E seed script expected 201 from `POST /pos/locations/{id}/areas` (it is 200) and placed a fourth table off the
  1,100-wide floor. Each stopped the run before any order, leaving two partial organizations, *Phase 83 Guest Discount
  d2fbd8* and *d8ed7d*, with no orders (recorded in § 4).

## 4. Evidence

**Tests.**
- Domain (`PosPartDiscountTests`, 9 new):
  - A guest's 26 is spread 20 / 6 over a momo and a Coke, after the order's terms, and the order's own terms do not move.
  - **The next guest's bill equals a twin tab billed without the discount**: same quantities, figures and shares. Both
    bills add up to 601.16 unrounded and 601 billed, against 633 without.
  - Three momo, one each: the first with 15% off (30), and the last part is identical to the twin's.
  - A discount on the last part comes off what is left of the line (350 + 35 + 50.05).
  - A void of the discounted bill gives it up and the food rebills at 632.80.
  - Beside the order's 10%, a guest's 20 is 15.38 / 4.62. Beside an order amount of 52, every bill still takes its
    share and the shares add up to 52.
  - A transfer after a guest's discount moves only what is left of the order's share (16.67).
  - The refusals: over the part, negative, not whole paisa, over 100%, three decimals. All of it is a bill of 0.
  - The invoice keeps the guest's share inside its bill discount, refuses an order share beside a percentage, and
    refuses a guest share larger than the line's.
  - **The twin test bites:** with `WhatIsLeft` ignoring earlier bills' discounts, 2 of the 9 fail. The file was restored
    by hash.
- Application (`PosPartDiscountHandlerTests`, 5 new):
  - Preview, bill and stored lines; the order's total midway (601.16); the next guest at 316.
  - A percentage beside the order's percentage, with the PDF's four summary rows asserted in order.
  - A cashier without `TillDiscount`: the preview is 200, the bill is 403 naming the key, a missing order is 404, and the
    bill without a discount is 200.
  - Both validators.
  - A void taking the discount back off the order.
  - `SalesPriceGateTests` asserts `AtLocation` both ways.
- Angular (4 new):
  - The bill page sends and shows the discount, switches kind, and clears it after paying.
  - It is disabled with the key named.
  - The order page's discard cap and its label.
  - The price note's "in this organization".

**E2E on SQL Server** (fresh organization *Phase 83 Guest Discount ee2045*: HeadOffice in Restaurant mode with a
warehouse, four tables, momo 200 at 10% service charge, Coke 60, cash on a Cash account). Two earlier runs stopped at
the seed (an area answers 200; a table off the floor). Their organizations hold no orders.

- **A (T1):** the guest's part (a momo and a Coke) with Rs 26 off previewed 284.76 → 285, shares 20 / 6, and billed 285.
  The order then read `partDiscount` 26 and `total` 601.16. The rest previewed 316.40 → 316 and settled the order. SQL:
  invoice `0001` DiscountAmount 26.0000, its lines BillDiscount = PartDiscount = 20.0000 / 6.0000; `0002` at the order's
  terms, 0 / 0.
- **B (T2):** the order at 10%, the guest's part 10% more. The preview was 23.40 (18 / 5.40) and 256.29 → 256. The bill
  was printed (counted) and refunded in full through the till (256). SQL: invoice `0003` DiscountPct 10.0000 beside
  DiscountAmount 23.4000. The credit note `0001` mirrors it (10.0000 / 23.4000, lines 18.0000 and 5.4000), which
  phase 74's credit-note rule would have refused. The order kept DiscountPct 10.00, DiscountAmount .00.
- **C (T3):** billed with Rs 26 off, then voided through the ERP. The order read `partDiscount` 0 and `total` 632.80,
  and the whole bill previewed 633.
- **The key proofs.**
  - A cashier role holding every Admin key but `Sales.Invoice.TillDiscount` previewed the guest's discount (200, 285).
  - Billing it was **403** "…(HO.Sales.Invoice.TillDiscount)".
  - Billing a nonexistent order with the same valid body was **404**.
  - Billing the same part with no discount was 200 (316).
  - **403-not-404:** `POST /pos/orders/{nonexistent}/bill/preview` with a valid body carrying a discount was **404** as
    Admin and **403** "…(Pos.Order.Operate)" from a role holding `Pos.Order.View` and `Tenancy.BillingLocation.View`,
    beside that role's 200 on `GET /billing-locations`.
  - Admin was restored in SQL.
- **PDF** (`tools/pdf/pdf2png.ps1`, looked at): invoice `0003` first printed *Before Bill Discount 234.00, Bill Discount
  (23.40), Sub Total 210.60, Discount Applied 10%*. That read as a second 10% (§ 3). Re-rendered after the fix: *Discount
  Applied 10%, Before Bill Discount 234.00, Bill Discount (23.40), Sub Total 210.60*, Service Charge 16.20, VAT 29.49,
  Round Off −0.29, Grand Total 256.00. The reprint carries "COPY OF ORIGINAL · printed 2 times".
- **Keyboard (browser pass, https dev server at 1440×900, cookie transplanted through `web/public`; the file was deleted
  at once).**
  - Bill page, ORD0004 (one momo and one Coke left):
    - From the *Everything* split, one Tab reached *Discount on this bill* (labelled, `aria-describedby` the note).
    - I typed 15 and pressed Tab to the kind (Percent). The server priced 39.00 off and Due 269.00.
    - Three Tabs reached the amount (prefilled 269.00). Enter added it and Enter again paid.
    - Focus went to "Bill 0006 paid: 269.00", and the live region said "39.00 off this bill. The order is settled…".
    - SQL: `0006` DiscountAmount 39.0000, lines 30.0000 / 9.0000 as PartDiscountAmount.
  - Order page, ORD0002 (one of each billed):
    - It showed *Discount (10% on the order) 52.00* and *Discounts on bills 23.40*, Amount 444.60, Estimate 541.05.
    - Enter on *Discard Chicken Momo* opened the panel at max 1, labelled "of 1 not yet billed; 1 billed is refunded,
      not discarded".
  - Quotation draft (line 10%, document 5%), as a clerk role holding every key but `Sales.Invoice.Discount`:
    - The note read "…permission in this organization. A discount already on the document can still be removed."
    - Both inputs were disabled with *Remove* beneath them. The first try dropped focus to `<body>` (§ 3).
    - After the fix, Enter left focus on "Discount removed on line 1" and Tab went on to the row's VAT select. One
      earlier run, right after the rebuild, jumped to the page's first link; it did not recur in two later runs from a
      fresh load.
    - Tab, Tab reached the document's *Remove*, then Enter. Tab, Tab reached *Save Draft*, then Enter.
    - SQL: the quotation saved with DiscountPct .0000 and its line .0000.
  - The membership was restored to Admin.

## 5. Left open, and for later phases

- **A per-part service charge waiver** (Decision J): a guest declining the service charge on their own part. Same shape
  as this phase, not asked.
- **A guest's discount on the 80 mm roll and the till receipt** prints as the bill's *Discount* (the receipt derives
  gross − subtotal), not split from the order's. Legible, not separated.
- **Carried unchanged:** phase 81 § 5's other items (a price change between saves, no other door), phase 80 § 5,
  phase 79 § 5, phase 77 § 5, phase 76 § 5, phase 75 § 5, phase 72 § 5's remaining items, phase 71 § 5, phase 70 § 5,
  phase 69 § 5, phase 67 § 5, phase 66 § 5, phase 63 § 5 and phase 62 § 5.

## Writes made to the vendor tenant

None. The vendor was not opened this phase.
