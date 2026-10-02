# Phase 65 — Kitchen display and settling: a bill moves quantities, never rates

## TL;DR

**A restaurant can now cook from a live kitchen board, print an estimate, and bill an order whole, by
item and quantity, or equally, each part paid at the till as an ordinary approved Invoice.** The
vendor's split dropped the service charge from the remainder (phase 59 defect 1); ours prices every part
from the order lines' frozen rates, so both halves of phase 59's table carry their 20 and the table pays
40. An order settles when nothing unbilled is left, which frees its table; voiding one of its bills
reopens it.

| | |
|---|---|
| Live read taken | **yes**, 2026-10-02, *Hamro Samaan* trial, **read-only**. The board was empty (no open orders), so its states, the split dialog, *Mark as Take Away* and transfer were read from the client bundle. The user later authorised writes; the auto-mode classifier refused the first one (phase 64's experience again), so nothing was written |
| Migration | `Phase65KitchenAndSettling`, scaffolded and **not** hand-edited: `PosOrders.SettledAt`, `Invoices.PosOrderId`, `InvoiceLines.PosOrderLineId` (all nullable, with Restrict FKs and their indexes), two seeded permission rows |
| New endpoints | 4: the bill preview, the bill, the kitchen board, a ticket's serve (shapes in `e2e-recipes.md`) |
| New permission key | **1**: `Pos.Kitchen.Operate` (Admin+Member) |
| New screens | the bill screen (`/pos/orders/:id/bill`) and the kitchen board (`/pos/kitchen/:locationId`), both lazy; the 80 mm estimate bill |
| Initial bundle | **650.06 kB** (+0.31 kB, the route entries) against 680 kB |

**Four things worth carrying forward.**

1. **The last of a line takes what is left of its money; rounding runs on the running total.** A part
   is priced fresh, except the part that bills the last of an order line, which takes exactly what is
   left of that line's amount, service charge and VAT. And a part comes to `R(billed so far + this part)
   − billed so far`, so every bill is a whole rupee, every round-off is under a rupee, and the parts add
   up to the order's own rounded total without a special case for the last one (633 in halves is 316 then
   317 — phase 63's answer, reached by a rule rather than an exception).
2. **Invoiced is a sum, not a counter.** An order line's invoiced quantity is the sum of the invoice lines
   naming it, on invoices not voided. A void gives the quantity back by changing the invoice's status, so
   reopening an order needs no counter to be written back — phase 64 Decision B's rule, applied to billing.
3. **One sale engine, two doors.** Phase 61's work after the lines (payment, abbreviated invoice, stock,
   credit, approval, tenders) was lifted into `PosSaleCompletion` rather than copied; the bill for an
   order is a second door onto it, and the metered and location-bearing guards were taught its name.
4. **The board is derived, not stored.** A ticket's served and cancelled quantities come from the line's
   one served counter (given to the earliest sends) and its discards (taken from the latest unserved
   sends). Nothing is archived by age: the vendor hides a ticket unserved for five hours.

Tests: Domain **874** (+11), Application.UnitTests **1529** (+14), Infrastructure.UnitTests 13,
Api.IntegrationTests **30**, Angular **727** (+13). `dotnet build`, all four .NET suites (Docker up, run
one at a time), `ng build` and `ng test` are green.

---

## 1. What shipped

**Domain**

- `PosOrderStatus.Settled`, `PosOrder.SettledAt`, `SettleIfFullyBilled`, `Reopen`, `RemainingToBill`,
  `Touch` (public: a bill moves the order's rowversion). `Discard` and `Void` take the invoiced quantities
  and refuse billed food; `Serve` works on a settled order.
- `PosOrder.TicketProgress()` and `KitchenTicketState` (Pending / Served / Cancelled / Cancellation).
- `PosOrderBill.Plan` (`Domain/Pos/PosOrderBill.cs`): the split planner, pure.
- `Invoice.PosOrderId`, `BillPosOrder`, `AddPosOrderLine`, `SetPosRoundOff`; `InvoiceLine.PosOrderLineId`
  and `CreatePosOrderPart`.

**Application**

- `PosSaleCompletion` (`Pos/Sales`), lifted out of `CreatePosSaleCommandHandler`.
- `PosOrderBilling` (the one reader of what an order has billed) and `PosOrderBillPlanner`.
- `PreviewPosOrderBillQuery`, `CreatePosOrderInvoiceCommand`, `GetPosKitchenBoardQuery`,
  `ServeKitchenTicketCommand`.
- `VoidInvoiceCommandHandler` reopens a settled order (409 if its table is seated again).
- `PosOrderView` gains invoiced / to-bill per line, the order's bills, `SettledAt`; `GetPosRestaurant`
  gains the caller's open drawer, the print toggles, `CanKitchen`, and billed / to-bill per open order.

**Angular**

- `features/pos/pos-bill-page`: the split (Everything / By item / Equally), the server's preview, tenders,
  the tax invoice printed, the order reloaded, an equal split counted down.
- `features/pos/pos-kitchen-page`: the board, polled every 10 s while visible, filters by station and
  order type, per-line and per-ticket serve, the "to cook" summary, new tickets announced politely.
- `features/pos/pos-estimate`: the estimate bill.
- The order screen: *Bill & Pay*, *Print Estimate*, billed counts, the order's bills, the Settled state.
- The floor polls every 15 s, says whose drawer is open, links to the board, shows a part-paid tab.
- The launcher offers a Restaurant location's drawer again and its board.
- Sales > POS Orders: Settled filter and badge, a Billed column, the order's bills linked.

## 2. Scope decisions

**A. The live read, and what it settled.** The kickoff's questions, answered from screens and the bundle
(`erp-module-scan.md`, "Kitchen board and split, read live"):

- **The board** has Pending / Served / All tabs, filters KOT type (order type), area and table, one card
  per ticket per station, a per-item *Mark as Served* (partial by quantity), a card-level *Served* dialog
  with checkboxes and quantities, and an *Order Summary*. It has **no** polling (`cacheTime: 0`, a
  manual refresh). *Cancelled* and *Archived* are **badges, not actions**: cancelled is read from the
  ticket's status, and archived is a client-side test — unserved and older than five hours
  (`18e6` ms) — which also drops it from the pending count.
- **The estimate bill** is a menu item on a saved order, shown when the location's *Estimate Bill* toggle
  is on ("To print estimate bill, the order needs to be saved first"). Phase 59 already recorded that the
  vendor's receipt is headed ESTIMATE BILL even on a settled tax invoice (defect 3).
- **Split Bill** is one dialog: a checkbox and a quantity stepper per line (`min 1`, `max` the line's
  quantity, fractions only where the unit accepts them), *Pay* takes the ticked part to the payment
  screen, and splitting everything is refused ("Please split partial order only"). Each split is one
  invoice. **There is no equal split.** The payment screen also offers *Disable Service Charge*.
- **Mark as Take Away** is an *Update Takeaway* dialog with a quantity, stored as `takeaway_quantity` and
  shown as a take-away count on the line. The client does not reprice it (its price helper never reads
  `is_take_away`). Whether the **server** prices that quantity without service charge could only be seen
  by writing, and the write was refused (above).
- **Transfer** is *Transfer Items* (Dine In only, from *Transfer List*): pick a table other than this one,
  tick items with quantities, `POST /pos/orders/items-transfer {order_id, table_id, area_id, items}`.

**B. Equal split: N invoices with fractional lines, chosen one at a time.** The vendor has none, the
roadmap promises one. A tax invoice lists what was supplied, so an equal share of a meal is an equal
share of **every** line: one of N parts takes `remaining ÷ N` of each line, rounded to the order's four
quantity decimals, and the last part takes what is left (one Thukpa between three bills 0.3333, 0.3334,
0.3333). This is the industry shape (a check split into N with fractional items), keeps stock exact (the
quantities sum to the line), and needs no stored split state: the request says "one of N parts of what
is left" and the till counts N down. The alternative — one invoice with N tenders — already exists (the
payment screen takes several tenders); it is not a split of the bill.

**C. Invoiced is a sum over invoice lines naming the order line, on invoices not voided.**
`InvoiceLine.PosOrderLineId` plus a header `Invoice.PosOrderId` (what finds an order's bills and what a
void reads). Phase 6's caps net of reversals, through status rather than a counter. Consequences:

- a discard refuses billed food (409: "what is billed is refunded, not discarded");
- a part-billed order cannot be voided (409: discard the rest instead);
- discarding the unbilled rest of a part-billed order **settles** it;
- a refund (a credit note) of a bill changes nothing on the order: the food was billed and returned.

**D. Three ways to choose a part, one request shape.** `Whole`, `Items` (lines and quantities — "by item"
is a whole remaining quantity, "by quantity" a smaller one), `Equal` (parts). Whole on everything is
allowed (the vendor's refusal exists only because its split mutates the order).

**E. One planner for the preview and the bill** (phase 63's rule for a figure that depends on other
documents). `PosOrderBill.Plan` is pure Domain; `PosOrderBillPlanner` reads the order's bills and calls
it; the preview query, the bill command and the estimate bill all go through it. The till keeps no
TypeScript copy.

- **Rates never move.** Every line is priced from the order line's frozen rate, VAT rate and service
  charge rate (phase 64 Decision C). The regression test for the vendor's defect 1 is
  `A_split_by_item_keeps_the_service_charge_on_both_bills_and_they_add_up_to_the_order` (Domain) and its
  Application twin; the E2E shows it in SQL.
- **The last of a line takes exactly what is left** of each of its figures, so a line's parts sum to the
  whole line to the paisa (asserted for one Thukpa in three).
- **Rounding is cumulative**: `R(billed unrounded + part) − billed`. Each bill is a whole rupee, each
  round-off is under a rupee, and the bills sum to the order's rounded total. Rounding each part on its
  own would bill 11 + 11 + 0 for an order of 21.20 (pinned in a test); rounding the running total bills
  11 + 10 + 0. After a voided part the surviving bills can sit a rupee or so off the running total; if the
  formula would then make a bill negative or move it by a rupee or more, that part is rounded on its own.
- The consequence worth saying: a part that follows a part rounded down carries the difference. In the
  E2E, 316.40 was billed 316 and the rest, 540.14, was billed 541 (+0.86), so the table paid 857 — the
  order's own rounded total.

**F. The board polls; it is not pushed.** Every 10 s while the tab is visible (and at once when it
becomes visible again), plus Refresh; the floor polls every 15 s, closing phase 64 § 5's "live floor".
A push (SSE or SignalR) needs a backplane once there is more than one API instance, and a cookie-authed
long-lived connection is new infrastructure for a latency a kitchen measures in tens of seconds. New
pending tickets are announced through a polite live region, never on the first read.

**G. Permissions (derived per feature).**

- `Pos.Kitchen.Operate` (Admin+Member): the board and its serve. Routine daily work, no money and no
  customer beyond a Take Away's name, so a cook's role can hold it **alone** (the E2E's *Cook* role does);
  it is organization-wide like `Pos.Session.Operate`, because a cook holds no `Sales.Invoice` key for a
  branch boundary to hang on.
- The **preview** (and estimate) rides `Pos.Order.Operate` at the order's location: a waiter prints the
  estimate the guests ask for without a drawer.
- The **bill** is a till sale: `Sales.Invoice.Create` at the location in the pipeline, the caller's own
  open session at the order's location, and `Sales.Invoice.Approve` re-checked there if anything is left
  on credit (phase 61 Decision F). Named `CreatePosOrderInvoiceCommand` so `AuditBehavior` writes its row;
  metered, lock-date sensitive, location-bearing.
- The 403-not-404 pair is in § 4.

**H. Settling.** `Settled` means everything on the order is billed; the table is free because the
one-open-order index is on `Open`. Unserved food on a settled order stays on the board (a Take Away is
paid before it is cooked) with a *Paid* badge, and can be served. A settled order cannot be added to.
Voiding one of its bills reopens it — refused (409) if its table has since seated another order, before
anything moves; the way out then is a refund.

**I. The estimate bill.** Headed *Estimate Bill / अनुमानित बिल*, boxed **NOT A TAX INVOICE**, and closing
"This is an estimate, not a tax invoice. The tax invoice is issued when the bill is paid." It carries no
PAN block, no invoice number and **no print count**: nothing is issued, so the 2072 copy rule (which is
about invoices) does not reach it, and two prints are two estimates. It prints what is still to bill,
with *Already paid* when part is. Shown only where the location's *Estimate Bill* toggle is on.

**J. Two concurrent bills of one order** cannot both be counted against one remainder: the bill touches
the order, its rowversion moves, and the second save is the global concurrency 409.

**K. Smaller decisions.**

- The bill uses the order's customer (set it on the order to bill in a guest's name); the screen takes
  payment in full. Credit at the restaurant is the engine's (named customer + Approve) but not offered on
  this screen (§ 5).
- No discount on a bill: a discount breaks "the bills add up to the order", which is this phase's
  regression test, and discount schemes are already outside the sequence (§ 5).
- A product made unavailable for sale after it was ordered is still billed: the food was cooked.
- Stock leaves the location's default warehouse (an order names none); a location without one is a 409
  at the bill, as at the till.
- Serving from a ticket writes the line's one served counter; the board gives served to the earliest
  sends, so serving a later ticket of the same dish shows the earlier one served. Same dish, same count.

## 3. Bugs found and fixed

1. **The launcher sent a Restaurant cashier to the Retail grid** after *Start Session* (browser pass): it
   always navigated to `pos/till/:id`. It now opens the floor for a Restaurant location; a spec pins it.
2. **Focus fell to the page after paying** (keyboard census): the Pay button leaves with the bill it paid.
   Focus now moves to the "Bill N paid" heading; a spec pins it.
3. The metered and location-bearing sweep guards failed on `CreatePosOrderInvoiceCommand`, which is what
   they are for; each was taught a named second-door entry with its reason (phase 61's rule).
4. A first FluentValidation helper for the bill part took a captured `Func` selector — phase 25's gotcha —
   and was replaced before it ran by rules written directly on each command's members.
5. A `.btn-check` radio clicked by ref in the pane hits the hidden input, and nothing changes; the label
   is the control to click (a recipe, not an app bug).

## 4. Evidence

**Live read:** `erp-module-scan.md`, "Kitchen board and split, read live (2026-10-02, phase 65)".

**Migration:** applied to the dev database (`dotnet ef database update`); additive, three nullable
columns, two FKs (Restrict), their indexes, two seed rows.

**E2E** on a fresh organization, *P65 Kitchen 174236*: VAT-registered, POS Restaurant only, HeadOffice in
Restaurant mode with 10% service charge, round-off, KOT and estimate printing, Cash and Card modes;
Chicken Momo (200) and Veg Thukpa (180) to *Kitchen*, Coke 250ml (60, Goods, 48 in opening stock) to
*Bar*; T1–T3 on the Ground Floor. Seeded through the API with every status printed (all 2xx). The browser
pass ran in the pane on `erp-web-ssl` with the seed's cookie transplanted and `window.print` stubbed.

1. **Launcher → drawer**: *Start Session* (float 1,000) at the Restaurant card → SES0001 (bug 1 found here).
2. **Seat T1**: 2 Momo, 1 Thukpa, 2 Coke, 2 guests, *Send to Kitchen* → ORD0001, KOTs for Bar and
   Kitchen printed; estimate 856.54 (Momo 497.20, Thukpa 223.74, Coke 135.60).
3. **The board**: Pending (2), both cards, "To cook" Momo 2 / Coke 2 / Thukpa 1. *Serve* on the Momo line
   → "Chicken Momo served on ORD0001-1", focus stayed on the card. *All Served* on the Bar card → the card
   left Pending, focus moved to the board's heading, Pending (1).
4. **Estimate bill**: *Estimate Bill · अनुमानित बिल · NOT A TAX INVOICE*, Sub Total 700.00, Service Charge
   58.00, VAT 98.54, Round Off 0.46, **Estimated Total 857.00**.
5. **Split by item**: 1 Momo + 1 Coke → preview 316.00 (service charge 20, round-off −0.40), "541.00 will
   be left"; Cash 500 → bill **0001**, change 184, the Tax Invoice printed with its own Service Charge
   20.00 and Round Off −0.40 lines.
6. **Pay the rest**: preview 541.00 (service charge 38, round-off +0.86), Card → bill **0002**; "The order
   is settled and the table is free."
7. **Equal split** (phase 59's own table at T2, 2 Momo + 2 Coke = 632.80): *Equally*, 2 parts → 316.00
   (service charge 20); cash → **0003**; the screen counted down to 1 part → **317.00 with service
   charge 20** (where the vendor billed 294 with none) → **0004**; settled.
8. **Void reopens**: bill 0005 (ORD0003 at T3, 292.00) voided through the API → ORD0003 Open, invoiced 0,
   to bill 1 + 1, 0005 listed as voided, T3 taken again; billed again by keyboard → **0006**, focus on
   "Bill 0006 paid" (bug 2's fix).

**SQL** (`sqlcmd`, the new organization):

```
Order    Invoice  Amount  SC     VAT    RoundOff  GrandTotal
ORD0001  0001     260.00  20.00  36.40    -.40    316.00
ORD0001  0002     440.00  38.00  62.14     .86    541.00
ORD0002  0003     260.00  20.00  36.40    -.40    316.00
ORD0002  0004     260.00  20.00  36.40     .60    317.00

Code     Bills  SumBillSC  OrderSC  SumBills  OrderTotalRounded
ORD0001  2      58.00      58.00    857.00    857.00
ORD0002  2      40.00      40.00    633.00    633.00

Code     Line  Product       Net  Invoiced  Served  Bills
ORD0001  1     Chicken Momo  2    2         2       0001:1, 0002:1
ORD0001  2     Veg Thukpa    1    1         0       0002:1
ORD0001  3     Coke 250ml    2    2         2       0001:1, 0002:1
ORD0002  1     Chicken Momo  2    2         0       0003:1, 0004:1
ORD0002  2     Coke 250ml    2    2         0       0003:1, 0004:1
```

Net is `SUM(KitchenTicketLines.Quantity)`; Invoiced is `SUM(InvoiceLines.Quantity)` over lines naming the
order line on Approved invoices. **Each bill has two balanced GL entries** (the sale, 7 lines; its
tenders, 2 lines): 0001 356.40 / 316.00, 0002 581.00 / 541.00, 0003 356.40 / 316.00, 0004 357.00 / 317.00.
Across the four bills: Cash In Hand +949 (316 + 316 + 317), Nabil Bank +541, **Accounts Receivable 0.00**,
Sales −1,220, Service Charge Income −98, VAT Payable −171.34, Rounding −0.66, COGS +160 / Inventory −160
(4 Coke at 40). **Tables**: T1, T2, T3 with no open order (before ORD0003 was reopened). Audit rows:
Create Invoice ×4 and Create PosOrder ×2, every one with its location.

**The 403-not-404 pair** (a throwaway user who registered, verified and accepted an invitation in the run):

- `GET /pos/kitchen/{nonexistent}` as Admin → **404** "Billing location not found.";
- the same as a *Waiter* role (`Pos.Order.Operate`, `Pos.Order.View`, `Sales.Invoice.Create/View`,
  `Tenancy.Subscription.View`) → **403** "You do not have permission to perform this action
  (Pos.Kitchen.Operate).", and 403 on the real board;
- beside it, that waiter → **200** on `GET /pos/restaurants/{loc}`, with `canKitchen: false`;
- moved to a *Cook* role (`Pos.Kitchen.Operate`, `Tenancy.Subscription.View`) → **200** on the board and
  **403** naming `Pos.Order.Operate` on the restaurant read.

**Keyboard census** (phase 40's method):

- **Board**: 18 focusable controls, all named — Refresh, Floor, the three view radios ("Pending (5)"),
  Station and Order type selects, then per line *Serve 1 Veg Thukpa on ORD0001-1* and per card
  *All Served: ORD0001-1, T1*. Zero pointer-only targets. No horizontal scroll at 320 px.
- **Bill screen** (by item): 14 focusable controls, all named — the split radios, per line a checkbox
  ("Veg Thukpa (less spicy)") and *How many Veg Thukpa on this bill, at most 1*, the mode radios, the
  amount field, Add; *Take Payment* disabled until the bill is covered. Zero pointer-only targets. A
  keyboard-only payment: Arrow to *Everything*, Tab, Tab, Enter (adds the amount due), Enter (pays).
- No screen reader was run (phase 40's honest limit).

## 5. Left open, and for later phases

- **Mark as Take Away** and **item transfer** (phase 64 § 5), now read: both are quantity moves between
  lines (and orders), naturally a third kitchen-ticket kind. Not built, because Mark as Take Away under
  phase 64 Decision H would *change* a frozen service-charge rate on part of a line, against this phase's
  rule that rates never move; whether to reprice a parcelled dish is a product question for the user,
  and the vendor's server behaviour could not be read (the write was refused).
- **Discount on a restaurant bill**, and **credit** on the bill screen (the engine allows both rules;
  the screen offers neither).
- **Per-part customers** (each friend's invoice in their own name).
- **Push** for the board, if polling proves too slow or too costly.
- Carried unchanged: phase 62 § 5 and phase 63 § 5.

## Writes made to the vendor tenant

None. One write (starting a session at *POS Restaurant*) was attempted after the user authorised writes
and refused by the auto-mode classifier.
