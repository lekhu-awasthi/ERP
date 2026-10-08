# Phase 68 — Mark as Take Away and item transfer: a move is a ticket, and the parcel's rate is decided once

## TL;DR

**A waiter can now parcel part of a dine-in line and move items to another table, and both bill correctly.**
*Mark as Take Away* moves a quantity from a dine-in line to its take-away sibling, whose service charge the
location's new **Service charge on take-away** setting decides (On by default, which is what the line carried
before) and which is **frozen on the sibling when it is marked**. *Transfer Items* moves food to another table's
open order, or opens one there, at the same rates. Both are kitchen tickets of new kinds, so a line still stores
no quantity and every figure is still a sum over tickets.

Before it, the statutory fix scheduled ahead of the phase: the ERP invoice and credit-note PDF print the buyer's
**"PAN: …" only when the contact has one** (VAT Rules Schedule 5).

| | |
|---|---|
| Live read taken | **yes**, 2026-10-08, *Hamro Samaan* trial, 1 day left. One write the user authorised (a saved dine-in order on T2: Chicken Momo × 3, Coke × 1). Both dialogs opened and read; the *Mark TakeAway* **Confirm** was refused by the auto-mode classifier (phases 64–66 again), so the vendor's server pricing of a take-away quantity is still unobserved |
| Migration | `Phase68TakeAwayAndTransfer`, scaffolded then **hand-edited twice**: `PosLocationSettings.ServiceChargeOnTakeAway` defaults to **1** (the scaffold's 0 would have switched every location off), and `KitchenTickets.Kind` defaults to `'Send'` with an `UPDATE … SET Kind = 'Cancellation' WHERE Reason IS NOT NULL` backfill. Plus `PosOrderLines.IsTakeAway`, `ParcelledFromLineId`, `KitchenTickets.CounterpartOrderId` (Restrict FKs, indexed) |
| New endpoints | `POST /pos/orders/{id}/take-away {lineId, quantity}`; `POST /pos/orders/{id}/transfers {tableId, items}` → `{source, target, targetCreated}` |
| New permission keys | **none**: both ride `Pos.Order.Operate` plus the order location's `Sales.Invoice.Create` (Decision F) |
| Changed screens | the restaurant order screen (*Take Away* per line, *Transfer Items* on the order, ticket kinds), the kitchen board (pack-to-go and transfer cards, *Moved*), the printed KOT, Configurations > POS (the setting, Restaurant locations only) |
| Initial bundle | **652.58 kB**, unchanged, against 680 kB |

**Four things worth carrying forward.**

1. **A move is a ticket, never a counter.** The vendor keeps `takeaway_quantity` and `transferred_quantity` on the
   line beside its tickets. Here a take-away mark is one ticket with minus on the dine-in line and plus on the
   parcel; a transfer is a ticket on each order, minus and plus, each naming the other. Net quantity is still
   `SUM(KitchenTicketLines.Quantity)`, and a move is neither *ordered* nor *discarded*, which needed the ticket's
   kind **stored**: phase 64 told a send from a cancellation by its signs, and a move has both.
2. **"Marking outranks the frozen rate" is honoured by deciding the rate once, at the mark.** The parcel copies the
   dine-in line's rate and takes service charge from the setting *at that moment*. A product exempt from service
   charge was already at zero on the dine-in line, so it stays exempt without the catalogue being read. Changing
   the setting later reprices nothing; a second mark at a different rate makes a second parcel line.
3. **A void after a transfer needs no rule of its own.** A transfer moves at most what is unbilled, so the source
   never falls below what its invoices name, and a bill names the line it bills. Voiding the T3 bill in the E2E
   gave its Momo back to T3's line and reopened T3, and T1 stayed settled.
4. **A transfer that empties a tab closes it** (the user's choice): voided "All items transferred to ORD…",
   which frees its table. One that leaves it fully billed settles it.

Tests: Domain **897** (+15), Application.UnitTests **1575** (+10), Infrastructure.UnitTests 13,
Api.IntegrationTests **43** (+4), Angular **762** (+7). `dotnet build`, all four .NET suites (Docker up, one at a
time), `ng build` and `ng test` are green.

---

## 1. What shipped

**The PAN fix (before the phase)**

- `PrintableDocumentDto.PartyPan` (trailing optional), set only by the invoice and credit-note builders from the
  contact's `Pan` (trimmed; blank is null); `DocumentPdfRenderer` prints `PAN: …` under the party address.
- Tests: a handler test (invoice and credit note, with and without a PAN; a quotation never carries one) and
  `BuyerPanPrintPdfTests` reading the PDF through `TestSupport/PdfText`. The seller's own PAN also prints as
  "PAN: …", so the PDF tests count lines: two with a buyer PAN, one without.

**Domain**

- `KitchenTicketKind` (`Send`, `Cancellation`, `TakeAway`, `Transfer`) stored on `KitchenTicket`, with
  `CounterpartOrderId`; `KitchenTicket.Create` validates each kind's shape (a take-away nets to zero; a transfer
  is one sign and names another order).
- `PosOrderLine.IsTakeAway`, `ParcelledFromLineId`, `CopyOf` (the frozen terms), `HasSameTermsAs` (what a
  transferred quantity may join).
- `PosOrder.MarkTakeAway(lineId, quantity, serviceChargeOnTakeAway, user, invoiced)` and
  `PosOrder.Transfer(source, target, items, user, sourceInvoiced, now)` → `PosOrderTransferResult`.
- `PosOrderLineQuantities.MovedIn` / `MovedOut`; `KitchenTicketLineProgress.Moved`; `KitchenTicketState.Moved`
  (a transfer's outgoing ticket, or a send whose food all moved before it was served).
- `PosLocationSettings.ServiceChargeOnTakeAway` (default true), a **required** parameter of `Update`, so the
  compiler listed every caller (phase 60's trailing-optional lesson).

**Application**

- `UpdatePosOrderTakeAwayCommand` and `CreatePosOrderTransferCommand` (names chosen so `AuditBehavior` writes
  their rows: Update and Create).
- `PosOrderView`: line `IsTakeAway`, `ParcelledFromLineId`, `MovedIn`, `MovedOut`; ticket `Kind`,
  `CounterpartOrderCode`. The kitchen board's line `Moved`, `IsTakeAway`, ticket `Kind`, counterpart; the
  *Served* view includes *Moved*; serving a *Moved* ticket is a 409.
- The setting through the settings command, DTO and reader, and on the restaurant read (so the till can say what
  a mark will charge before it is made).

**Web**

- Order screen: *Take Away* on a dine-in line with something unserved and unbilled (quantity defaulting to all of
  it, as the vendor's dialog does, and a sentence saying what happens to its service charge, tied to the input
  by `aria-describedby`); *Transfer Items* on a Dine In order (a table select naming whether each table joins an
  open order or opens a new one; a checkbox and quantity per line); a *Take away* badge; ticket kinds labelled.
  After a transfer that emptied this order, the till follows the food to the other order.
- Printed KOT: *Take Away: pack to go* (the parcel's lines only) and *Transferred to this table* with *From
  ORD…*. Marking prints its ticket where the location prints KOTs; a transfer prints the target's ticket.
- Kitchen board: *Pack to go* and *Transferred from / Moved to* lines on the card, a *Moved* badge, moved counts.
- Configurations > POS: *Service charge on take-away* under Service Charge, Restaurant locations only.

## 2. Scope decisions

**A. The live read.** The vendor's line menu on a sent dine-in line is *Edit Item / Mark as Take Away / Mark as
Served*. *Mark TakeAway* is a checkbox and a stepper per line (default the whole line, minimum 1) with *Confirm*
disabled until ticked. *Transfer Items* lists every line with a checkbox and a stepper, then *Area\** and
*Table\**; the table list excludes the order's own table. The one write (saving an order) was allowed; *Confirm*
on the take-away dialog was refused by the classifier, so whether the vendor's server reprices a take-away
quantity remains unobserved. It no longer matters: the user answered the product question on 2026-10-03.

**B. A parcelled part is a split line, moved by a third kind of ticket** (phase 64's suggestion). The
alternatives were a take-away counter on the line (the vendor's; a second view of one quantity, phase 37's
failure) or a per-line flag (cannot express "2 plated, 1 parcelled"). The rate is frozen on the sibling at the
mark: `setting On ? dineIn.ServiceChargeRate : 0`. A later mark joins the sibling only while its rate still
matches. Only Dine In, and only food not yet served **and** not yet billed: parcelling a plate already eaten from
would take the service charge off food the table was served. There is no un-mark (the vendor has none).

**C. Marking prints a new ticket, not a reprint.** The kitchen packs it rather than plates it, so it is told; the
take-away ticket is a first print. A reprint is still only ever the same ticket printed again.

**D. Transfer.** Dine In to Dine In at one location (the vendor's rule). The target is the open order at the
chosen table, or a new one (one guest, walk-in). Rates travel with the quantity; a target line with every frozen
term the same absorbs it. At most what is unbilled moves; unserved food first, and any served food that moves
takes its served count. Emptying an unbilled source voids it (**the user's choice**, 2026-10-08, over refusing
the transfer); leaving it fully billed settles it. Only the target's ticket prints: it tells the kitchen where
the food goes now.

**E. The bill needed no change.** A parcel is an ordinary line with its own frozen rate, so phase 65's planner
(last of a line takes what is left, rounding on the running total) applies as it is, asserted in a Domain test
and in the E2E.

**F. Permissions, derived.** `Pos.Order.Operate` at the order's location for both, as a send is: routine table
work, and the service charge a mark may remove is the location's setting, not the waiter's. A transfer moves a
charge between tabs, so both commands are audited (`Update`, `Create`). The setting rides `Pos.Settings.Manage`.

**G. The setting's surface.** Shown only for a Restaurant location (a Retail till has no orders to mark), under
Service Charge while it is on, and stored regardless so switching service charge off and on loses nothing. An
absent field on the settings `PUT` reads as On, never as a silent Off. A Take Away *order* still never carries
service charge (phase 64 Decision H), so with the default On a parcel from a dine-in tab pays it while a Take
Away tab does not; that difference is the user's default, recorded rather than changed.

## 3. Bugs found and fixed

1. **A reload after an order action stole focus to the menu search box.** `loadRestaurant` focused the search
   box on every call, so after a transfer (and, since phase 64, after *Move Table / Guests*) focus left the
   button that opened the panel, against phase 40's rule. Found by the keyboard pass, not by any test:
   `loadRestaurant(locationId, focusSearch)` now focuses the search only when the page opens, and both panels
   restore focus to their opener. Re-run on the rebuilt bundle: focus returns to *Transfer Items*, including
   after the till followed an emptied tab to the target order.
2. **The E2E seed expected 201 from three POS configuration POSTs that answer 200** (areas, kitchen stations,
   sessions). Not a product bug; the seed's first run left a half-seeded organization (`de19751c…`, *Phase 68
   Restaurant*), abandoned, and the second run used a fresh one.

## 4. Evidence

**Fresh organization** `8300d699-c690-403f-91f9-9c359561c7d8`, seeded through the API, every status printed, all
2xx: VAT-registered, POS Restaurant, Chicken Momo (200, service charge) and Coke 250ml (60), 10% service charge,
round-off on, floor T1/T2/T3, a Kitchen station, an open drawer.

**The flow** (`e2e68_flow.py`), every figure asserted:

- T1 seats 2 Momo + 2 Coke: 632.80. One Momo marked take away with the setting **On**: the parcel keeps 10%, the
  estimate stays 632.80, the ticket is `TakeAway`.
- Setting switched **Off**; the second Momo marked: a second parcel at 0%, the first still 10%, the estimate
  610.20. Marking again with nothing left: **409** naming the line.
- T2 seats 1 Momo + 1 Coke (316.40). A transfer to T1's own table: **400** naming `TableId`. One Coke T1 → T2:
  joins T2's Coke line (2), no new line. The uncharged parcel T1 → T3 (free): a new order, take away at 0%,
  226.00. T1 now 316.40.
- Bills: T1 **316**, T2 **384**, T3 **226**; service charge 20 / 20 / 0. Voiding T3's bill reopens T3 with its
  parcel to bill again; T1 stays settled.

**In SQL** (`e2e68_sql.py`): every order line's net is the sum of its ticket lines and its invoiced quantity the
sum of approved invoice lines naming it; the tickets read `Send`, `TakeAway` (−1/+1), `Transfer` (−1 naming
ORD0002 and ORD0003; +1 naming ORD0001 on each target); each bill rebuilt from its lines equals what was paid
(316 = 260 + 20 + 36.40 − 0.40; 384 = 320 + 20 + 44.20 − 0.20); the parcel's invoice line carries 20 of service
charge at its frozen 10% and the transferred parcel's carries none; approved bills total 700. The setting is
stored 0 for this location and 1 for the other 14; the backfill left both pre-existing cancellations as
`Cancellation` (both with a reason) and every send as `Send`. Audit rows: **Create × 4** (two seats, two
transfers), **Update × 2** (the two marks).

**Browser pass and keyboard census** (Browser pane, `erp-web-ssl`, a fresh ORD0004 on T1), real Tab / Enter /
Space / arrow presses with focus read after each:

- *Take Away Chicken Momo* is the Tab stop after *Serve*, with the `#0a58ca` ring and `aria-expanded`/
  `aria-controls`; Enter opens the panel and focus lands in the labelled quantity ("Pack to go (of 2 not yet
  served or billed)", value 2), described by "The parcel carries no service charge (this location's setting)"
  (the location was Off). Ctrl+A, 1, Enter marks it; focus returns to the button; the estimate falls 565.00 →
  542.40; ticket ORD0004-2 is labelled *Take Away* and printed itself as *Take Away: pack to go … 1 × Chicken
  Momo (plt) · TAKE AWAY*, the minus line left off.
- *Transfer Items* is the Tab stop after *Move Table / Guests*; Enter puts focus on the labelled *Transfer to*
  select, whose options read "T2 (Ground Floor) · free: opens a new order" and "T3 (Ground Floor) · joins
  ORD0003". Arrow keys choose; Tab walks the item checkboxes and **skips each disabled quantity**; Space ticks;
  Tab then reaches the now-enabled quantity, labelled "Quantity of Chicken Momo to transfer, of 1"; Enter
  transfers. (A batch that pressed Tab within the same task as Space outran the render and skipped it; at a
  human's pace it does not.) Moving the last item emptied ORD0004: "Everything moved to T2 (ORD0005); ORD0004 is
  closed", the till on ORD0005, focus on its *Transfer Items*.
- The kitchen board shows *Pack to go* and *Transferred from ORD…* cards, the *Take away* badge, and "0 served ·
  1 moved" on the original send whose Coke left; each *Serve* names the item and ticket.
- Configurations > POS: *Service charge on take-away* reads back unticked (stored Off), is labelled, described by
  its help text, and is the Tab stop after the service-charge account.
- Screenshots timed out (the app window was hidden); the evidence above is read from the DOM after real key
  events, phase 40's method.

**403-not-404** (`e2e68_perm.py`): `POST /pos/orders/{nonexistent}/take-away` and `/transfers` are **404** "Order
not found." as Admin, then **403** "…(Pos.Order.Operate)." from a custom role granting `Pos.Order.View` and
`Tenancy.BillingLocation.View`, beside a **200** on `GET /billing-locations` from the same user in the same run.
The role was restored to Admin in SQL (a role without `Tenancy.Role.Manage` cannot move itself back).

## 5. Left open, and for later phases

- **The packaging fee** (flat line / percentage / none per location, inside the VAT base, its own account), asked
  for on 2026-10-03 and carried separately by the user's decision.
- **Un-mark** (a parcel back to the table): the vendor has none; a discard plus a re-send covers it.
- **Discount and credit on a restaurant bill**, and **per-part customers** (phase 65 § 5, unchanged).
- Whether the vendor's *server* reprices a take-away quantity (the classifier refused the confirm). Settled for
  us by the user's answer; recorded only so nobody re-reads it as open.
- Carried unchanged: phase 67 § 5, phase 66 § 5, phase 63 § 5, phase 62 § 5.

## Writes made to the vendor tenant

- At *POS Restaurant*, table **T2**: one saved dine-in order, Chicken Momo × 3 and Coke 250ml × 1 (two KOTs). It
  was left open; nothing else was written. No credentials were entered and no token was read.
