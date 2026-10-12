# Phase 84 — returns at another branch, split payouts on the refund screen, and the money receipt as a bill

## TL;DR

**A till can now refund a sale made at another branch of the same organization.** Phase 63 refused it (Decision C
there): a refund belonged to the location that sold. The customer had to travel back. Now a retail sale comes back at
any branch. It is refunded out of that branch's drawer, numbered and located there, and its goods go back into that
branch's warehouse at the cost they left at. The refund screen also splits a payout across modes (the API always took
several). A sale with nothing left to give back says *Fully refunded*. The money receipt (a Payment's PDF) prints its
date in BS with AD beside it, as every bill does.

The vendor has no such control. Read live on 2026-10-11 (abcagro1, read-only): its till's refund starts from the till's
invoice list, and that list and its search are both pinned to the till's `location_id`. Searching the Restaurant till for
the Retail till's `INV0001` returned nothing. So cross-branch returns are our divergence, chosen by the user.

The user's decisions (2026-10-11), each the recommended option:

- **A — cross-branch, retail sales only.** A till refunds another location's sale when the sale carries no service
  charge and no packaging fee. Those two are posted to per-location accounts (`PosAccountResolver`), and reversing them
  at the refunding branch would leave the selling branch's account in credit and this one's in debit. A restaurant bill
  is a 409 that says to refund it where it was sold.
- **B — found by its full number, under the refund's own keys at this branch.** The search still lists the till's own
  sales. Another location's sale is added only when the term is its exact number, and only for a caller holding
  `Sales.CreditNote.Create` and `.Approve` at the till. No grant is needed at the selling branch: the customer is
  holding the bill. There is no browsing of another branch's sales.
- **C — the goods go back where they are.** `CreditNote.WarehouseId` (new, nullable) is the refunding location's default
  warehouse for an away sale, and the sale's warehouse at home. Null, on every older note and every ERP note, means the
  sale's warehouse, as before. A refunding location with no default warehouse is a 409 naming it.
- **D — the money receipt dates like a bill, and is not a tax document.** `PrintElements.BillDatedTypes` is the three tax
  documents plus `Payment`. Only the dates follow it (`BillDatedDocuments`). The locked seller/buyer elements and the
  two statutory dates stay with `TaxDocuments`.

| | |
|---|---|
| Live read taken | **yes**, read-only (abcagro1, 14 days left): the refund list and search are location-pinned, a fully refunded bill is still listed with an enabled *Initiate Refund* that opens an empty list, and the refund's payment step is the sale's multi-tender page |
| Migration | `Phase84CrossBranchReturns`: `sales.CreditNotes.WarehouseId` uniqueidentifier NULL, FK to `tenancy.Warehouses` (Restrict), its index. No backfill: null already means the sale's warehouse |
| New permission keys | **none**. An away sale is read and refunded under `Sales.CreditNote.Create` + `.Approve` at the till; a home sale under `Sales.Invoice.View` there, as before |
| Changed endpoints | `GET /pos/tills/{loc}/sales` adds the exact-number away match and `soldAtLocationName`, `fullyRefunded`, `refundableHere`; the refundable read **moved** from `/pos/sales/{id}/refundable` to `/pos/tills/{loc}/sales/{id}/refundable` and adds `soldAtLocationName`, `returnWarehouseName` |
| Changed screens | the refund page: the full-number hint, *sold at X*, *Fully refunded*, *Refund it at X* for a restaurant bill, "Sold at X … the goods go back into Y", and split payouts (*Part handed back*, *Add part*, a *Remove* per part) |
| Changed print output | the Payment PDF (Customer Receipt / Supplier Payment): `Date: 2083-06-25 BS (2026-10-11 AD)` whatever the toggle, and no calendar footer |
| Initial bundle | **652.58 kB**, unchanged, against 680 kB |

**Three things worth carrying forward.**

1. **When the vendor has no control, the way round is the specification.** Before this phase a customer returning at
   the wrong branch was sent back to the right one. That refund is the twin: the same sale refunded at home must leave
   exactly the same credit note figures, payout and GL lines (`PostedAsync`, location stamp aside), and the last unit
   refunded at home must tie the sale out to the paisa either way. Only *where* differs: the note's location, session
   and warehouse.
2. **One list deciding two things is two lists waiting to happen.** Phase 71 said "if the money receipt is a bill, it
   is one line in `TaxDocuments`". By phase 73 that list also locked a template's seller and buyer blocks and printed
   two statutory dates. Adding `Payment` to it would have done three things to answer one question. The date set is now
   its own list, built from the tax set plus `Payment`, and a test pins both.
3. **Moving a key from the pipeline into the handler shows what a handler test never had.** The refundable read used to
   be gated by `ILocationScopedDocument` in `AuthorizationBehavior`, which handler tests bypass. Re-checking
   `Sales.Invoice.View` in the handler (its key now depends on the loaded sale) failed two phase 63 tests: the fixture's
   cashier had never held View. A real Member does, so the fixture gained it; the gap was the test's, not the product's.

Tests: Domain **1029** (+1), Application.UnitTests **1778** (+13), Infrastructure.UnitTests 14, Api.IntegrationTests 71,
Angular **852** (+4, 102 files). `dotnet build`, all four .NET suites (Docker up, one at a time), `ng build` and
`ng test` are green.

---

## 0. The live read (2026-10-11, abcagro1, read-only)

Entered as in phases 74–81: the organization list showed *Trial Ends in 14 days*; `window.open` was overridden, *Open Pos*
clicked and the captured URL followed in the same tab; XHR was wrapped to log, and from the second page on it refused
every non-GET to the vendor's API.

- **No cross-branch return.** The till's *Invoices* tab is the refund's entry point (*View*, then *Initiate Refund*).
  Its list is `GET /erp/invoices?location_id=<till>&channel=POS&status=Approved`, and its search adds
  `search_phrase=`; the Restaurant till (1002) searching `INV0001` (the Retail till's sale, `INV0001/1003/83-84`)
  returned no rows. There is no other door in the till.
- **A fully refunded bill stays listed.** `INV0001/1003/83-84` (refunded in two parts in phase 74) still shows, its panel
  says *This Invoice has already been refunded* and names `CN0001` and `CN0002`, and *Initiate Refund* is enabled. It
  opens a drawer with no items and a disabled *Proceed to Payment*.
- **The refund's payout is the sale's payment page.** On `INV0002/1002/83-84`, ticking Masala Tea and a remark, then
  *Proceed to Payment*, opened `/pos/payments/{invoice}` with the mode tabs All, Cash, Card, E-Payment, Credit, Other,
  *Applied Payment Methods*, Received and Due. So the vendor splits a refund across modes; ours offered one.
- Nothing was completed; the page was left by navigation.

## 1. What shipped

**Domain**

- `CreditNote.WarehouseId` and `CreatePosRefund(…, warehouseId)` (required, refused when empty).
- `PrintElements.BillDatedTypes` (tax documents + `Payment`) and `IsBillDated`.

**Application**

- `PosRefundPlanner`: `IsHere`, the cross-branch rule in `EnsureRefundable`, and `WarehouseFor` (the sale's warehouse at
  home, the till's default warehouse away, a 409 naming the location when it has none).
- `CreditNoteApprovalPosting` restocks to `creditNote.WarehouseId ?? sale.WarehouseId`.
- `FindPosSalesQuery`: the exact-number away match for a caller who may refund here, `SoldAtLocationName`,
  `FullyRefunded` (sold less returned under every note not voided, the cap's rule) and `RefundableHere`.
- `GetPosRefundableSaleQuery(OrganizationId, LocationId, InvoiceId)`: now `ILocationFilteredQuery`; the handler checks
  View at the till for a home sale and the two refund keys there for an away one, then the planner's own rules, and
  answers `SoldAtLocationName` and `ReturnWarehouseName`. `CanRefund` is asked at the till, where the refund happens.
- `SalesMasterReportQueryHandler`: a note's own warehouse first, for the column and the warehouse filter.
- `BillDatedDocuments` drives `FormatDate` and the calendar footer in the print handler, and the date in the template
  preview.

**Api.** The refundable route moved under the till; the old one is gone (one door).

**Angular**

- `PosService.getRefundableSale(org, locationId, invoiceId)` and the new model fields.
- `pos-refund-page`: the search hint and empty-state wording, *sold at X* on a row, *Fully refunded* / *Refund it at X*
  instead of the button, the away note on the sale panel, and split payouts. A part is added from the chosen mode and an
  amount (Enter adds it), at most what is left; Complete needs the parts to come to exactly what is owed back once any
  is added, and hands back all of it in the chosen mode otherwise. Removing a part hands focus to the amount field.

## 2. Scope decisions

**A. Which sales may come back elsewhere: no service charge, no packaging fee.** Both are credited to a per-location
account when one is set (`PosAccountResolver.ServiceChargeAccountAsync` / `PackagingFeeAccountAsync`), and a refund's
reversal reads the account at *its* location. Returning a restaurant meal at another branch is rare; splitting one
refund's legs across two locations' accounts would be the first entry of its kind. The rounding account is the refund's
own (its round-off is the refunding till's figure), so it raises no such question. "Allow every sale" was offered and not
chosen.

**B. Finding it: the exact number, the refund's keys.** The pipeline keeps `Sales.Invoice.View` (location-filtered), so
a caller with no View anywhere is a 403 before any row is read. The cross-branch row needs `Sales.CreditNote.Create` and
`.Approve` at the till, the keys the refund itself asks for (phase 63 Decision E); a cashier without them finds nothing
more, silently, which is the same answer as a mistyped number. The alternative (View at the selling branch too) would
make every cross-branch return a two-branch grant. Per the derivation rule: routine daily-use working data, bounded to
one bill the customer holds, so no new key.

**C. The warehouse: where the goods are.** At home a till refund stores the sale's own warehouse, so nothing moves for
phase 63's refunds. Away it stores the refunding location's default warehouse. The layer is created there at the sale
line's `CogsUnitCost` (phase 37). Void reverses by source document (`ReverseIncrementAsync`), so it takes the goods back
out of the warehouse they went into with no change. "Selling branch" was offered and not chosen: the books would say the
goods are where they are not.

**D. The note's location, session and number are the refunding branch's.** Phase 63 already numbered the note in the
till's pool and paid it out of the caller's open session; nothing about that changed. Its GL entries, its stock
movement and its row in every location-filtered report are stamped with the refunding branch, the place the money
left. The sale stays its own branch's.

**E. A fully refunded sale is marked, not hidden.** The vendor lists it with a live button that leads nowhere; ours lists
it with *Fully refunded* and no button. Hiding it would read as "no such bill" to a cashier holding it.

**F. Split payouts: the till's tender pattern, one click when not splitting.** The common case (all of it in one mode)
stays one action. A split is the till's shape: choose a mode, enter a part, *Add part*. Parts are reset whenever the
preview changes, because they were sized against a figure that no longer holds. The server's rule (payouts sum to
exactly what is owed back) is unchanged and still the one that decides.

**G. The money receipt: dates only.** `Payment` joins the BS-dated set, both directions, because it is one type and one
template; a supplier's payment voucher is printed for the supplier and dates like the receipt. It does not join the tax
documents, so nothing on its template is locked and it prints one "Date", not the two statutory ones. The email body's
`$[DATE]$` stays AD (phase 30), and Annex 5/13 and the VAT Summary exports still follow the toggle (phase 71 § 5, not
asked this phase).

## 3. Bugs found and fixed

1. **Two phase 63 tests failed once the refundable read checked View in its handler.** `PosTestTill.CashierKeys` never
   held `Sales.Invoice.View`; the old route was gated by the pipeline, which handler tests skip. The fixture gained the
   key, with the reason in a comment. TL;DR point 3.
2. **The E2E seed's products could not be sold** (409 "not available for sale"): `POST /products` was sent without
   `availableForSale`, which binds to false. Recorded in `e2e-recipes.md`.
3. **A spec-patch script wrote a NUL into a TypeScript file.** A Python heredoc broke on the template literals (the
   known heredoc gotcha), and the Write-made replacement carried a placeholder line with `'\u0000'` inside a non-raw
   Python string, which became a real NUL byte. Found before the run, repaired by a second script that asserts no NUL
   is left. `known-gotchas.md` records it beside phase 62's backspace.

## 4. Evidence

**Unit tests** (`PosCrossBranchRefundTests`, 10; `PrintDocumentQueryHandlerTests`, 3 new cases; `PrintElementsTests`, 1;
`pos-refund-page.spec`, 4 new):

- *the twin*: 3 Coke sold twice at home; 2 of one refunded at the branch and 2 of the other at home give the same total,
  round-off, payout and GL lines; the last Coke of each refunded at home ties both sales out (203), AR and Rounding at 0;
  the away note is the branch's location, session and warehouse; the branch warehouse holds one layer of 2 at 40;
  every movement is stamped with the branch; stock conservation holds;
- the branch drawer pays it out and counts it (expected cash 932, one refund) and the home session counts none;
- a split payout away (Cash 100 + Card 36) posts both and the bank is −36;
- a service-charged bill: preview, refund and read at the branch are 409s naming the service charge; the search names it
  with `RefundableHere` false; it refunds at home;
- a branch with no default warehouse is a 409 naming the branch and "default warehouse";
- found only by the full number (a prefix and an empty term find nothing away); the read names the selling location and
  `Lakeside Branch Store`;
- a cashier with Approve at home only: no away row, the read and the refund are 403s naming `Sales.CreditNote.Approve`,
  and at home the same cashier may refund;
- *Fully refunded* after the last unit, and not again once that refund is voided;
- voiding an away refund empties the branch warehouse, AR 0, conservation holds;
- Sales Master reports the note at the branch warehouse, and its warehouse filter finds it there;
- the money receipt (Received under AD and BS, Paid under AD) prints `2083-04-16 BS (2026-08-01 AD)`, "Date", no issue
  date, no footer; `BillDatedTypes` is the three tax documents and Payment, and Payment locks nothing;
- the refund page: a split sends exactly the parts and says both in the banner; a part over what is left is refused on
  the field; a removed part hands focus back to the amount; an away row, a fully refunded row and a restaurant row show
  one Refund button between them; the away panel names the warehouse.

**E2E** (fresh organization `fa690b8a-0d4e-43d2-8ec2-9480f21f8e23`, *Phase 84 Branches 231206*: HeadOffice → Main
Store, Lakeside (`LKS`) → Lakeside Store, both Retail with 10% service charge and rounding, Cash and Card modes; Coke
(goods, 50 opening at 40) and Chicken Momo (service, service-charged); a session at each):

- Sales at HeadOffice: `0001` 3 Coke 203, `0002` 1 Momo 249, `0003` 1 Coke 68.
- Lakeside, `search=000`: 0 rows; `search=0001`: one row, `soldAtLocationName` HeadOffice, refundable, not fully
  refunded. The read: `HeadOffice | Lakeside Store | canRefund True`.
- Preview of 2 Coke at Lakeside: 136.00 (round-off 0.40); `POST /pos/refunds` with Cash 100 + Card 36: credit note
  `0001`, paid out 136.
- `0002` at Lakeside: the row with `refundableHere` false; the read and the preview are **409** "…carries a service
  charge or packaging fee, which that location's accounts hold…"; the read at HeadOffice is 200.
- The last Coke of `0001` at HeadOffice: `0002`, 67.00 (136 + 67 = 203); the home search marks it fully refunded.
- **403-not-404:** `GET /pos/tills/{LKS}/sales/{nonexistent}/refundable` is **404** "Sale not found." as Admin and
  **403 naming `Sales.Invoice.View`** from a role holding every Admin key but that one, beside its 200 on
  `GET /billing-locations`. From a role holding every key but `Sales.CreditNote.Approve`: the nonexistent id is **404**
  (the pipeline passed), sale `0003` is **403 naming `LKS.Sales.CreditNote.Approve`**, its exact search at Lakeside
  returns 0 rows, and `POST /pos/refunds` with a valid body is the same 403. Back on Admin, the read is 200.
- SQL: note `0001` Lakeside / Lakeside Store / SES0002, payouts Cash 100 and Card 36; note `0002` HeadOffice / Main Store
  / SES0001, Cash 67; layers Lakeside Store 2 (80.00) and Main Store 47; the two notes' stock movements stamped with
  their own warehouse and location; GL: Accounts Receivable 0.00, Cash 353.00 (203 + 249 + 68 − 100 − 67), Nabil Bank
  −36.00, Sales −260.00, Service Charge −20.00, VAT −36.40, Rounding −0.60 (the remaining two sales' round-offs; the
  refunded sale's nets to zero), COGS 40.00, Inventory −40.00 (opening stock posts no GL, phase 58's law).
- The money receipt: a 500.00 receipt from Acme Traders, printed as a draft and again approved (`0001`), with no
  calendar header sent: `Date: 2083-06-25 BS (2026-10-11 AD)`, one date, no footer. Looked at on the rendered page
  (`tools/pdf/pdf2png.ps1`).

**Keyboard pass** (`erp-web-ssl`, the cookie through `web/public`, deleted at once; Lakeside's refund screen):

- Load: focus in the search box, the hint "Another location's sale: type its full number."
- `0003`, Enter: one row, "0003 sold at HeadOffice … Refund bill 0003". Tab, Tab, Enter: focus on the sale's heading,
  and "Sold at HeadOffice. Refunded here, out of this drawer; the goods go back into Lakeside Store."
- Tab to the quantity, `1`, Tab: the server's preview (68.00, round-off 0.20) and the amount prefilled 68.00. A reason,
  Tab past the Cash radio to the amount, `50`, Enter: "Cash 50.00" listed, focus still in the amount, now 18.00.
- Tab to *Remove Cash 50.00*, Enter: the part gone, **focus on the amount field** (not `<body>`), amount 68.00.
- `50`, Enter again; Shift+Tab to the radios, ArrowRight to Card; Tab, Enter: "Card 18.00", "All of it is accounted
  for.", Complete enabled. Tab past both *Remove* buttons to *Complete Refund*, Enter.
- What was sent: `POST /pos/refunds {…, lines: [{…, quantity: 1}], payouts: [{Cash, 50}, {Card, 18}], reason:
  "Returned at Lakeside, flat"}`. The banner: "Refund 0003 complete: 68.00, handed back 50.00 in Cash and 18.00 in
  Card."; focus on the credit note's panel. In SQL: note `0003`, Lakeside / Lakeside Store, Cash 50 and Card 18, that
  reason; Lakeside Store holds 3.
- *Choose another bill*: the search re-runs and the row reads "0003 sold at HeadOffice … Fully refunded", no button.

## 5. Left open, and for later phases

- **A restaurant bill at another branch** (Decision A's refusal). Allowing it means reversing its service charge and
  packaging fee into the selling branch's accounts while the payout and round-off are the refunding branch's: two
  locations in one entry, which nothing posts yet.
- **The ERP credit-note page** does not show a till refund's warehouse; the detail DTO has no `WarehouseId`. Its location
  is shown, which is where an away refund differs.
- **The till's own tender list** drops focus when a tender is removed (`PosTillPage.removeTender`), the shape this phase
  fixed on the refund screen.
- **The money receipt on the roll** (phase 79 § 5) and **Annex 5/13 and the VAT Summary exports in BS** (phase 71 § 5)
  are not done; the receipt's A4 date is.
- **Carried unchanged:** phase 83 § 5 (a per-part service charge waiver; the receipt and roll printing a guest's
  discount apart), phase 81 § 5, phase 80 § 5, phase 79 § 5's other items, phase 77 § 5, phase 76 § 5, phase 75 § 5,
  phase 72 § 5, phase 71 § 5's other items, phase 70 § 5, phase 69 § 5, phase 67 § 5, phase 66 § 5 and phase 62 § 5.

## Writes made to the vendor tenant

None. Two refund drawers and one payment step were opened and left by navigation; from the second page on, the page's
own XHR wrapper refused every non-GET to the vendor's API, and none was attempted.
