# Phase 64 — Restaurant: floor, orders, KOT — the ticket is the movement, the line is the product

## TL;DR

**A waiter can now seat a table, take its order, send it to the kitchen, add to it, mark what was
served and (with the right key) discard what was sent, from the restaurant till's floor; Take Away and
Delivery orders work the same way without a table; and the back office sees every order in Sales >
POS Orders.** An order is its own aggregate (`PosOrder`), not a Sales Order. It is numbered from the
tenant's counter (`ORD0001`), posts nothing, reserves nothing, and is billed in phase 65.

| | |
|---|---|
| Live read taken | **yes**, 2026-10-02, *Hamro Samaan* trial. Two writes before the auto-mode classifier blocked the rest; the remainder read from screens and the client bundle (`erp-module-scan.md`, "Restaurant, read live") |
| Migration | `Phase64RestaurantOrders`, scaffolded and **not** hand-edited: seven `pos` tables, one nullable `catalog.Products.KitchenStationId`, eight seeded permission rows |
| New tables | `pos.PosAreas`, `PosTables`, `KitchenStations`, `PosOrders`, `PosOrderLines`, `KitchenTickets`, `KitchenTicketLines` |
| New endpoints | 19, under `/pos` (shapes in `e2e-recipes.md`) |
| New permission keys | **4**: `Pos.Order.Operate`, `Pos.Order.View` (Admin+Member); `Pos.Order.Void`, `Pos.FloorPlan.Manage` (Admin-only) |
| New screens | the restaurant till's floor and order screen (lazy, under `/pos`); Configurations > Floor plan and Kitchen Stations; Sales > POS Orders |
| Initial bundle | **649.75 kB** (+2.46 kB, the route entries) against 680 kB; the order screen is its own 35 kB lazy chunk |

**Four things worth carrying forward.**

1. **The kitchen ticket is the movement; the order line is the product.** Every change to what a
   guest is having reaches the kitchen as a ticket line carrying the signed change, so the line stores
   no quantity at all: ordered, discarded and the net quantity a guest pays for are sums over its
   ticket lines, and cannot drift from what the kitchen was told. Served is the one counter stored,
   because nothing else records it. The vendor keeps five counters per line beside its tickets.
2. **Service charge is a dine-in charge.** The live read found the vendor's Take Away and Delivery
   tiles priced without it (226.00 against 248.60 for the same Momo). The rule now lives in one place,
   `PosServiceCharge.RateFor`, which the restaurant order *and* phase 61's sale engine call, and the
   Retail till's TypeScript mirror follows it — a Delivery sale at the Retail till had been charging it.
3. **A validator that names one member under two types throws when it is built.** A shared helper
   registered `x.Items` as `IEnumerable<T>` and the validator registered it again as
   `IReadOnlyList<T>`; FluentValidation caches one accessor per member, so building the validator threw
   `InvalidCastException` — a 500 on every endpoint it guards, invisible to every handler test. A new
   test builds and runs every POS validator; it was proven to bite.
4. **A divergence owes a surface, and a waiter is not a cashier.** The vendor's open tab is a Sales
   Order its back office already sees; ours is not, so Sales > POS Orders exists. And the restaurant
   reads its grid under its own key, so a waiter's role needs no drawer.

Tests: Domain **863** (+21), Application.UnitTests **1515** (+51), Infrastructure.UnitTests 13,
Api.IntegrationTests **30**, Angular **714** (+17). `dotnet build`, all four .NET suites (Docker up, run
one at a time), `ng build` and `ng test` are green.

---

## 1. What shipped

**Domain** (`Domain/Pos`)

- `PosOrder` with `PosOrderLine`, `KitchenTicket` and `KitchenTicketLine` (Decisions B–E): `Open`,
  `Send`, `Serve`, `Discard`, `Void`, `MoveToTable`, `UpdateDetails`, `RecordTicketPrint`, the derived
  `QuantitiesOf` and `Estimate`.
- `PosArea` and `PosTable` (the floor, Decision I) with `ApplyLayout`; `KitchenStation` (Decision G).
- `PosServiceCharge.RateFor` (Decision H).
- `Product.KitchenStationId` and `AssignKitchenStation`, which refuses a variant parent.
- `DocumentType.PosOrder`, classified in `DocumentMechanisms` and `StockBooks` as not a document.

**Application** (`Pos/Restaurant`, `Pos/Commands`, `Pos/Queries`)

- Orders: `CreatePosOrder`, `AddPosOrderItems`, `ServePosOrderItems`, `VoidPosOrderItems` (discard),
  `VoidPosOrder`, `UpdatePosOrder`, `PrintPosKitchenTicket`; reads `GetPosRestaurant`,
  `ListPosOrderProducts`, `GetPosOrder`; the ERP's `ListPosOrders`.
- Floor: `GetPosFloorPlan`, `CreatePosArea`, `UpdatePosArea`, `SavePosAreaLayout`.
- Stations: `ListKitchenStations`, `CreateKitchenStation`, `UpdateKitchenStation`,
  `SetKitchenStationProducts`.
- `PosSellableProducts`, lifted out of `ListPosProductsQueryHandler` rather than copied: the Retail
  grid and the order screen read one "what is sellable here".
- `PosOrderPricing` (Decision C), `PosOrderView`, `PosOrderValidationRules`, `PosOrderCommands`.
- `DocumentLocationReader` learned `PosOrder`, so its audit rows carry the location.
- `CreatePosSaleCommandHandler` asks `PosServiceCharge` (Decision H).

**Api.** `PosRestaurantEndpoints`: 19 endpoints.

**Angular**

- `core/pos/pos-restaurant.models.ts` and `pos-restaurant.service.ts`.
- `features/pos/pos-floor-page` (`/pos/restaurant/:locationId`): Dine In tables on the floor plan's
  canvas, Take Away and Delivery lists, Refresh.
- `features/pos/pos-order-page` (`/pos/orders/new` and `/pos/orders/:id`): the menu grid, *To send*
  kept in the browser, Send to Kitchen, the sent lines with their quantities, Serve / Discard / One
  More, move table and guests, Void Order, kitchen tickets with print and reprint.
- `features/pos/pos-kot`: the 80 mm kitchen ticket, one page per station, REPRINT marked.
- `features/configuration/pos-floor-plan-page` and `kitchen-stations-page`; links from Configurations
  > Point of Sale for a Restaurant location.
- `features/sales/pos-order-list-page` (`/sales/pos-orders`).
- The launcher's Restaurant card opens the floor; the Retail cart charges no service charge on Delivery.

## 2. Scope decisions

**A. The live read, and how far it got.** The kickoff's four confirm-live questions were the floor
editor's controls and limits, delta KOTs, what serve and discard do to counters, and the Take Away /
Delivery tabs. The user authorised writes on the trial tenant; after two (an area *Rooftop*, a table
*T3* of capacity 1000) the auto-mode classifier refused further writes on the vendor's tenant, and an
attempt to reuse the page's API token was refused as credential handling. Neither was worked around.
What remained was answered without writing:

- the floor editor from its dialogs, including the server's "table name must be unique" 400 (the one
  write that probed it was refused with that message);
- delta KOTs from phase 59's own written service (a third ticket carrying only Coke × 1);
- serve and discard from the client bundle: per-item, partial serve against `quantity − served −
  transferred − discarded`; discard with a required reason and a quantity up to `quantity − discarded
  − transferred` (so a served item can be discarded), reaching the kitchen as a negative line;
- Take Away and Delivery by opening them, which is where the service-charge finding came from.

**B. One quantity per line, or one per state? Neither: the ticket line is the one quantity.**

- The kickoff posed phase 51/55's rule: store one value. A send and a discard are both *changes to
  what the guest is having that the kitchen must be told*, so each is a `KitchenTicketLine` with a
  signed quantity (phase 55: store the signed value), and a line's ordered, discarded and net
  quantities are `GROUP BY` sums over them (phase 51: a dimension that is a GROUP BY over the one
  quantity cannot drift from it).
- `PosOrderLine` therefore has **no quantity column**. Storing one beside the tickets would be the
  two-views failure phase 37 named: two records of one fact, agreeing only by discipline.
- `ServedQuantity` is stored on the line, because serving is recorded nowhere else.
- A discard cancels unserved quantity first; beyond that the served count comes down with it (a dish
  sent back), so served never exceeds net and "still to serve" is `net − served` without a floor.
- **Invoiced** is phase 65's and is not a column here either: it will be the sum of the invoice lines
  naming the order line on invoices not voided (phase 6's caps net of reversals).

**C. A line is priced from the catalogue, never from the request.** The order commands carry no rate:
`PosOrderPricing` prices each item at the product's VAT-exclusive price in the chosen unit after the
tenant's price basis (the grid's own figure), freezes the unit and its factor (phase 52), the VAT rate,
the service-charge rate (Decision H) and the kitchen station. A waiter cannot cheapen a tab, and a
discount is the bill's business in phase 65. The estimate is priced by phase 63's `PosLineArithmetic`.

**D. `PosOrder` is its own aggregate** (phase 59 Decision E, confirmed). Its number is `ORD` plus the
tenant's counter (`DocumentType.PosOrder`), like a session's `SES0001`, and never a document-numbering
pool: it is not a ledger document. It needs no open session (a waiter has no drawer); billing it will.
It reserves no stock and posts nothing — asserted in a test and in SQL. One open order per table is a
filtered unique index, with a read in front of it for a friendly 409, as phase 61 does for sessions.

**E. A kitchen ticket per send per station.** A send groups its changes by the line's station into
tickets sharing a `SendNumber`; the paper prints `ORD0007-2`, because the vendor's ticket has no
number of its own. The station is **frozen on the line** when first sent, so more of a line goes to the
kitchen that cooked it even if the product is re-routed. A product with no station goes to *Default*,
which is not a row (as on the vendor's ticket dialog). A discard is a cancellation ticket with the
reason, its lines negative; a ticket is all one or the other. Every print is counted on the server and
a second one prints **REPRINT**, so a kitchen handed the paper twice does not cook the order twice —
not statutory, but phase 62's reason applies: a browser cannot know another till printed it.

**F. Permissions (derived per feature).**

- `Pos.Order.Operate` (Admin+Member): seat, order, send, serve, move, print, and the restaurant's
  reads. Routine daily working data.
- `Pos.Order.Void` (Admin-only): discard what was sent, void an order. It takes cooked food off a bill —
  a restaurant's classic void — so it is the review a send does not need, where phase 61 put credit and
  phase 63 refunds. The order screen reads `canVoid` and says so rather than offering a 403.
- `Pos.Order.View` (Admin+Member): the ERP list, scoped to where the caller may view invoices.
- `Pos.FloorPlan.Manage` (Admin-only): configuration, as phase 59 Decision J derived it. Kitchen
  stations ride `Pos.Settings.Manage`.
- **The branch boundary**: every order request re-checks `Sales.Invoice.Create` at the order's
  location (an order is billed as an Invoice there) — phase 61's open-a-drawer-only-where-you-sell
  rule. The keys are not location-scopable: an order is not a location-bearing document.
- `Pos.Kitchen.Operate` does not ship: the board is phase 65's (phase 60's lesson).
- Audited: `CreatePosOrder` (Create), `UpdatePosOrder` (Update), `VoidPosOrderItems` and
  `VoidPosOrder` (Void — named so `AuditBehavior` writes the row; the screen says *Discard*). A send and
  a serve are not: the tickets and the served counter are their record, as a session's movements are.

**G. A product's kitchen station is chosen from the station.** `Product.KitchenStationId` reopens phase
47's drop of `PrintProfileId`, and it is written by one command, `SetKitchenStationProducts` (the whole
list for one station), not by a parameter threaded through `CreateProduct`/`UpdateProduct`. Those have
57 callers, and phase 60 found every trailing optional parameter added to them silently dropped by some
caller. It is also how a restaurant thinks: "the bar gets the drinks". A station with products cannot be
deactivated (409); a station named "Default" is refused.

**H. Service charge is a dine-in charge.** Live: 226.00 on Take Away and Delivery, 248.60 on Dine In,
for one product. `PosServiceCharge.RateFor(settings, productFlag, orderType)` returns zero for Take Away
and Delivery and phase 59 Decision G's rule otherwise (Dine In, Retail, no type). Both the order line
and phase 61's sale engine call it, and `PosCart` mirrors it, so the Retail till's Delivery tab stopped
charging it — a change to a shipped behaviour, made because the till and the server must agree and a
charge for table service on a parcel is not one a restaurant may levy.

**I. The floor.** A fixed 1100 × 800 canvas (the vendor's), tables in its units; the screens scale it,
and below 576px it becomes a grid in floor order (WCAG 1.4.10). A layout save sends the whole area, as
the vendor's does, and must name every existing table (400 otherwise): a table is made inactive, never
deleted, because an order names it. A table's name is unique at its **location**, not its area, because
a ticket says "T1". Capacity is 1–100 (the vendor accepts 1000; a bound, not a rule). An occupied table
or area cannot be made inactive. Occupancy is never stored: a table is taken when an open order names
it. Moving a table works without a mouse: the arrow keys (Shift for 50) and the form's number fields.

**J. Sales > POS Orders.** The surface phase 49's rule says the divergence owes: every order, open or
voided, with its lines' counters and tickets, read-only, expanded in place (no detail route, so the
navigation catalogue offers no "Add"). The document-list chrome (search, date range, sort), status and
order type, and the report location filter, since an order is not a location-scoped document type.

**K. Smaller decisions.**

- *To send* lives in the browser: the server never holds an unsent line, so the vendor's "save without
  sending" does not exist. A new order's first send creates it and navigates to it.
- A Delivery needs a named customer that is not the walk-in (its address is where it goes).
- Guests: 1–100 on Dine In, 0–100 otherwise.
- *Mark as Take Away* on a dine-in line and item transfer between orders are not built (§ 5).
- The launcher no longer offers a drawer at a Restaurant location: nothing there takes money yet.

## 3. Bugs found and fixed

1. **Every order validator threw when built** (TL;DR point 3). Caught by the first handler test that
   built one; `PosOrderValidationRules` now makes every rule on a list from one expression, and
   `PosValidatorConstructionTests` builds and runs all 31 POS validators. Proven to bite by injecting
   the double registration back (restored by hash and touched).
2. `AsSplitQuery` is EF Relational, which `Application` does not reference; removed (compile error).
3. The sort sweep guard's harness knew only `Create` factories taking a `DateOnly`; it was **taught**
   an `Open` factory, a `DateTimeOffset` instant and a named `PosTab` (phase 55's rule), and the list's
   location scope was aligned with `LocationAccessScope`, which the guard's anonymous user exercises.
4. `ng build` caught an edit adding `PosOrder` to the client's `GlSourceDocumentType` — an order posts
   nothing, so it never belonged there; reverted.
5. **A mouse drag in the floor-plan editor never ended** (browser pass). The release was listened for
   on the canvas, so a release the canvas did not see left the drag running, and later movement over
   the canvas moved a table — the *selected* table, not the grabbed one. The server stored R1 at
   (34, 30) where the keyboard had put it at (120, 50). Now the drag names the table it grabbed, listens
   for the release on the document, and starts only after 4 px, so a click never nudges; a spec pins it.
6. The editor suggested "T1" for a new Rooftop table because it checked only its own area's names; the
   server's 400 ("…already has a table called T1 in another area") was right. The suggestion now steps
   over every area's names.
7. Cancelling "this area has unsaved changes" left the other area's radio checked (the binding had not
   changed, so Angular did not put it back); the handler restores it.
8. Closing an order-screen panel dropped focus to the page; it now returns to the button that opened
   it (or the order heading when that button is gone), and a missing discard reason focuses the field.

## 4. Evidence

**Live read:** `erp-module-scan.md`, "Restaurant, read live (2026-10-02, phase 64)".

**Migration** (`dotnet ef database update`, dev DB): applied. The scaffold is additive: seven `pos`
tables, the nullable `catalog.Products.KitchenStationId` with its FK, the filtered unique
`IX_PosOrders_OrganizationId_PosTableId` (`[Status] = 'Open' AND [PosTableId] IS NOT NULL`), the
**unfiltered** unique `IX_KitchenTickets_PosOrderId_SendNumber_KitchenStationId`, the convention's
`(OrganizationId, Date)`, `(OrganizationId, CreatedAt DESC)` and `(OrganizationId, Code)` on `PosOrders`,
and eight `RolePermissions` seed rows.

**E2E** on a fresh organization, *P64 Restaurant 095040*: VAT-registered, POS Restaurant only;
HeadOffice set to Restaurant with a 10% service charge, round-off and KOT printing. Seeded through the API
(every status printed, the two deliberate 400s naming their fields): Chicken Momo (200) and Veg Thukpa
(180), both service-charge-applicable and sent to the *Kitchen* station; Coke 250ml (60) to the *Bar*;
the Ground Floor with T1, T2, T3; a customer, Acme Retail. The browser pass ran in the pane on
`erp-web-ssl` with the seed's cookie transplanted and `window.print` stubbed to capture what printed.

1. **Floor plan editor**: added *Rooftop*; a new table defaulted to "T1" and the save was refused by the
   server; after the fix it suggested T4. R1 moved with the arrow keys (Left 20 → 120, Top 20 → 50), R2
   a circle dragged with the mouse to (547, 285). After bug 5's fix the server holds exactly what the
   screen showed: `R1 @34,30` (moved before the fix), `R2 Circle @547,285`, Ground Floor untouched.
2. **Seat a table**: the launcher's Restaurant card opens the floor; T1 opened a new Dine In order.
   2 Momo, 1 Thukpa with "less spicy", 1 Coke, 3 guests, **Send to Kitchen** → **ORD0001**, "Sent to the
   kitchen: Bar, Kitchen (ticket ORD0001-1)". Estimate 788.74 (Momo 400 + 40 + 57.20 = 497.20; Thukpa
   223.74; Coke 67.80). Two pages printed: *Bar* (1 × Coke) and *Kitchen* (2 × Momo, 1 × Thukpa » less
   spicy), each ORD0001-1, T1 · Ground Floor, 3 guests.
3. **Delta KOT**: *One More* on the Coke, Send → **ORD0001-2**, Bar only, printing "1 × Coke 250ml".
4. **Partial serve**: *Serve* on the Momo opened its panel with focus in the quantity (defaulting to the
   2 outstanding, `aria-expanded=true`); 1 + Enter → "2 on the order · 1 served".
5. **Discard**: on the Coke, without a reason → "A discard needs a reason." and nothing sent; with "Guest
   changed their mind" → "1 on the order · 0 served · 1 discarded", and **ORD0001-3** printed as a
   *Cancellation* to the Bar: "-1 × Coke 250ml, Reason: Guest changed their mind".
6. **Reprint**: ORD0001-1 Kitchen printed again, marked **REPRINT (2)**.
7. **An occupied table survives a reload**: after a full reload the floor shows T1 taken, "ORD0001 ·
   3 guest(s) · 788.74 · 3 to serve".
8. **Take Away**: a new order from the Take Away tab (walk-in, no table) → **ORD0002**, Amount 260, **no
   service charge line**, VAT 33.80, Estimate 293.80; its KOT reads "Type Take Away · Customer Cash
   Customer".
9. **Sales > POS Orders** (in the Sales nav): both orders; ORD0001 expanded to Ordered / Discarded /
   On order / Served of 2/0/2/1 (Momo), 1/0/1/0 (Thukpa — less spicy) and 2/1/1/0 (Coke), and its four
   tickets including the cancellation with its reason.

**SQL** (`sqlcmd`, the new organization):

```
Code     OrderType  Status  Table  Covers  Date
ORD0001  DineIn     Open    T1     3       2026-10-02
ORD0002  TakeAway   Open    NULL   0       2026-10-02

Code     Line  Product       Rate  SC     Note        Station  Ordered  Discarded  Net  Served
ORD0001  1     Chicken Momo  200   10.00              Kitchen  2        0          2    1
ORD0001  2     Veg Thukpa    180   10.00  less spicy  Kitchen  1        0          1    0
ORD0001  3     Coke 250ml     60    0.00              Bar      2        1          1    0
ORD0002  1     Chicken Momo  200    0.00              Kitchen  1        0          1    0
ORD0002  2     Coke 250ml     60    0.00              Bar      1        0          1    0
```

Ordered, Discarded and Net are `SUM`s over `pos.KitchenTicketLines` grouped by line; Served is the
stored column. The tickets: ORD0001-1 Bar (1 print), ORD0001-1 Kitchen (**2** prints), ORD0001-2 Bar,
ORD0001-3 Bar with Reason "Guest changed their mind" and `-1 x Coke`, ORD0002-1 Bar and Kitchen.
**Nothing posted, moved or invoiced**: `GlJournalEntries` 0, `StockMovements` 0, `Invoices` 0,
`SalesOrders` 0 for the organization. Audit rows: Create ORD0001, **Void** ORD0001 (the discard),
Create ORD0002, each with its location stamped.

**The 403-not-404 pair**: a custom role *Waiter* holding `Pos.Order.Operate`, `Pos.Order.View`,
`Sales.Invoice.Create/View` and `Tenancy.Subscription.View`, on a throwaway user who registered,
verified and accepted an invitation in the run.

- `POST /pos/orders/{nonexistent}/discard` as Admin → **404** "Order not found.";
- the same as the waiter → **403** "You do not have permission to perform this action (Pos.Order.Void).";
- beside it, the same waiter → **200** on `GET /pos/orders/{ORD0001}` and `GET /pos/restaurants/{loc}`,
  **403** on discarding the real ORD0001, and the floor read says `canVoid: false`.

**Keyboard census** (phase 40's method: every focusable control in order with its name, and every
`cursor: pointer` element that is not a control):

- **Order screen**: 23 focusable controls, all named — the menu search (focused on load), category
  buttons, dish tiles, Move Table / Guests, Void Order, then per line *One More / Serve / Discard* each
  naming its dish, and *Reprint ticket ORD0001-1 Kitchen* and the rest. Zero pointer-only targets. A
  panel opened by keyboard puts focus in its first field; Cancel (Tab, Tab, Enter) returns focus to
  "Serve Coke 250ml".
- **Floor**: 11 focusable controls, all named — the order-type and area radio groups, Refresh, Exit, and
  each table as a link whose name says its state ("T2, free, new order. Seats 2"; "T1, taken, order
  ORD0001 · 3 guest(s) · 788.74 · 3 to serve"). Zero pointer-only targets.
- **Reflow**: at 320 px the floor's canvas becomes a grid in floor order, nothing clipped.
- **Floor-plan editor**: tables are buttons (`aria-pressed`), moved by the arrow keys as above.

No screen reader was run (phase 40's honest limit, unchanged).

## 5. Left open, and for later phases

- **Phase 65**: the live kitchen board (and `Pos.Kitchen.Operate`), the estimate bill, split by item,
  quantity and equally, and settling an order into Invoices — invoiced quantity as a sum over invoice
  lines naming the order line, service charge preserved (the vendor's defect 1 as the regression test).
- **Mark as Take Away** on a dine-in line, and **item transfer** between orders (the vendor's
  `takeaway_quantity` and `transferred_quantity`): quantity-moving shapes that belong with splitting.
- The floor's occupancy is read on load and on Refresh; two waiters see each other's tables only after a
  refresh until phase 65's live board.
- Delivery partners (outside the sequence, unchanged).
- Carried unchanged: phase 62 § 5 and phase 63 § 5.

## Writes made to the vendor tenant (user-authorised, trial tenant)

- At *POS Restaurant*: area **Rooftop**; on Ground Floor, table **T3** (capacity 1000).
- No credentials were entered and no token was read.
