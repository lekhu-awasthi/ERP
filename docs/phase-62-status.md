# Phase 62 — Retail till: the screen a cashier sells from, and the two rules its receipt owed

## TL;DR

**A cashier can now open a drawer, sell, take several tenders with change, leave a bill on a named
customer's account, park and recall carts, reprint a bill, move cash and close with a count, all
on one full-screen till.** It runs on phase 61's engine. Every rupee still goes through
`POST /pos/sales` and the session commands; the till posts nothing of its own.

| | |
|---|---|
| Live read taken | **no**. Phase 59 read and wrote the vendor's till end to end, and nothing here needed a fact it lacked |
| Rule questions settled first | **yes, both** (phase 59 §6 q2 and q3), from the VAT Rules and the IRD's computerised-invoicing procedure; sources in Decisions A and B |
| Migration | `Phase62RetailTill`, scaffolded and **not** hand-edited: two additive `bit` columns defaulting to `false` (true of every existing row) and one table |
| New tables | `sales.InvoicePrints` (append-only, unique on organization × invoice × print number) |
| New columns | `Invoice.IsAbbreviatedTaxInvoice`, `PosLocationSettings.AbbreviatedTaxInvoiceEnabled` |
| New endpoints | 5: `GET /pos/tills`, `GET /pos/tills/{id}`, `GET /pos/tills/{id}/products`, `GET /pos/sessions/{id}/sales`, `POST /pos/sales/{id}/prints` |
| New permission keys | **none**. The till's reads ride `Pos.Session.Operate`, and a print rides `Sales.Invoice.View` at the sale's location |
| New screens | 3 lazy routes under `/organizations/:id/pos` (launcher, till, session), with the shell's chrome stepping aside |
| Initial bundle | **647.12 kB** (+1.14 kB, the route entries and nav strings) against 680 kB; the till is its own 36 kB lazy chunk |

**Four things worth carrying forward.**

1. **The rule answer was a set of conditions, not a yes.** An abbreviated tax invoice is lawful only
   for a VAT-registered seller with the Tax Officer's permission, for a bill of at most Rs 10,000,
   when the buyer has not asked for a full one. Each condition lives where its fact lives: the
   permission is a location setting stating the rule, the limit is a Domain constant, the buyer is
   the sale's contact, and the registration is the organization's. The decision is **stored on the
   sale**, so a reprint carries the heading the original had.
2. **A count printed on paper is a server fact.** The 2072 procedure requires every reprint to say
   "copy of original" *and* how many times the bill has been printed. That rules out a browser
   counter, so an append-only `InvoicePrint` row is written per print, under a unique index.
3. **A total the cashier takes money against must be the server's total.** The till computes the
   bill before the server sees it, so its arithmetic is pinned to a shared table that the Domain
   suite also reads (`pos-bill-cases.json`). The arithmetic is done in `bigint` paisa, because a
   float `1.005` rounds the wrong way.
4. **A hold is not a document.** The vendor numbers a parked Retail cart as an approved Sales Order.
   Ours lives in the browser, keyed by organization × location × **session**, which also closes the
   vendor's cross-location cart leak (defect 8). Phase 64's `PosOrder` is where a server-side open
   order belongs.

Tests: Domain **830** (+33), Application.UnitTests **1446** (+21), Infrastructure.UnitTests 13,
Api.IntegrationTests **30**, Angular **687** (+55). `dotnet build`, all four .NET suites, `ng build`
and `ng test` are green.

The integration suite first failed 10 Testcontainers tests in their constructors with
`DockerEndpointAuthConfig`, the known symptom: Docker Desktop's engine was down. Once the engine was
up it passed 30/30 (13 min, including the first container pull). Nothing in that project changed.

---

## 1. What shipped

**Domain**

- `Invoice.AbbreviatedTaxInvoiceLimit` (10,000) and `IsAbbreviatedTaxInvoice`, set by
  `IssueAsAbbreviatedTaxInvoice()`, which refuses an unpaid bill, an ERP invoice and a bill over the
  limit.
- `InvoicePrint`: an append-only print log with `Record(invoice, printsSoFar, user, at)`. It refuses
  an ERP invoice, a draft and a voided sale. `IsCopy` is `PrintNumber > 1`.
- `PosLocationSettings.AbbreviatedTaxInvoiceEnabled`, a **required** parameter of `Update`. The
  compiler then found the three test callers phase 60's lesson predicts.
- `Domain/Common/AmountInWords`: rupees in lakh/crore/arab grouping, with paisa.

**Application**

- `CreatePosSaleCommandHandler.IssueAsAbbreviatedWhereAllowedAsync` (Decision A). The result gains
  `IsAbbreviatedTaxInvoice`.
- `Pos/Queries`:
  - `ListPosTills` is the launcher.
  - `GetPosTill` is the till's own read of its location.
  - `ListPosProducts` is the grid: search, exact code for a scanner, category, sellable here.
  - `ListPosSessionSales` lists a session's sales.
- `Pos/Commands/PrintPosReceipt` writes the print row and returns the receipt (Decision B).
- `Catalog/ProductPrices.ToExclusiveRate`, lifted out of `SuggestProductRateQueryHandler`, whose
  copy it replaces (Decision D).
- `GrantedPermissionReader.IsGrantedAtLocationAsync`, the boolean half of
  `EnsureGrantedAtLocationAsync`, which now calls it.

**Api.** These are under `/api/organizations/{id}/pos` (shapes in `e2e-recipes.md`). The settings
`PUT` gains `abbreviatedTaxInvoiceEnabled`.

**Angular**

- **Shell.** `App.tillMode` (`isTillUrl`) hides the nav rail, header and inset under
  `/organizations/:id/pos`, and the organization stays in scope.
- **Navigation.** `NavigationCatalog` gets a **Point of Sale** area with one leaf, *Till* → `/pos`,
  after Sales.
- `core/pos`:
  - `pos-bill.ts` holds the bill arithmetic (Decision E).
  - `pos-cart.ts` holds the cart, the holds and persistence (Decision C).
  - The till's models and service calls.
- `features/pos`:
  - `pos-launcher-page`: tills, your open session, and Start Session by amount or by note count.
  - `pos-till-page`: grid, scan, cart with line edit (unit, price, discount), bill discount,
    customer, order type, hold/recall, and a modal payment screen (keypad, quick cash,
    multi-tender, change, credit, the 422 confirmations).
  - `pos-session-page`: the X/Z report, Cash In/Out, close with a count, and the sales list with
    reprint.
  - `pos-receipt`: the 80 mm receipt and its print stylesheet.
  - `denomination-count`: counting a drawer note by note.
- *Configurations > Point of Sale*: the abbreviated-invoice switch, with Rule 18 stated beside it.
- The GL reports' drill-down: a `PosSession` row now links to its session page (phase 61 §5).

## 2. Scope decisions

**A. Which bills are abbreviated tax invoices: every condition the rule sets, decided at the sale,
stored on it.**

The rule:

- **VAT Rules 2053, Rule 18(1)**, as amended by the 21st Amendment (Jestha 15, 2076): a registered
  person selling goods **or services** by retail may, with the concerned **Tax Officer's
  permission**, issue an abbreviated tax invoice (Schedule 6) instead of the Rule 17 tax invoice
  (Schedule 5).
- **Rule 18(6)**: not for a transaction of more than **Rs 10,000**. The 2076 amendment raised this
  from Rs 5,000; the original 2053 text had Rs 500, which is why older summaries disagree.
- A recipient of an abbreviated invoice cannot deduct the input tax, and **a customer who asks for
  a full tax invoice must be given one**.

Sources:

- [PKF TRU Flash Alert 010-2019](https://pkf.trunco.com.np/files/publications/1657087904_010-2019%20TRU%20Flash%20Alert%20on%20amendments%20in%20VAT%20Rules,2076_20190718055050.pdf) (the 2076 amendment, Rules 18(1) and 18(6));
- [Value Added Tax Rules, 2053, English text](https://legaladvisorsnepal.com/value-added-tax-rules-2053-1996/) (Rules 17 and 18 as originally made);
- [Nepal Taxes, provisions relating to invoices](https://nepaltaxes.com/provisions-relating-to-invoices-guide-to-invoice-abbreviated-tax-invoice/).

How each condition is held:

| Condition | Where it lives |
|---|---|
| Seller is VAT-registered | `Organization.IsVatRegistered`. An unregistered seller's receipt is headed **Invoice**, never *Tax Invoice* |
| Tax Officer's permission | `PosLocationSettings.AbbreviatedTaxInvoiceEnabled`, off by default. The settings screen states the rule under the switch (phase 61's "state it where it is switched on") |
| Bill ≤ Rs 10,000 | `Invoice.AbbreviatedTaxInvoiceLimit`, refused by the aggregate, inclusive (the rule refuses "more than") |
| Buyer did not ask for a full invoice | The buyer is the walk-in. **Naming a customer at the till is how a cashier asks**, so a named customer always gets the full Tax Invoice, with name, address and PAN |

The decision is made after `Settle`, when the total is final, and **stored**
(`IsAbbreviatedTaxInvoice`), because a reprint must carry the original's heading even after the
setting changes.

The abbreviated receipt **omits the buyer block** and keeps the VAT line. Schedule 6 allows
"VAT inclusive" without a separate line, but showing more than required is harmless and keeps one
layout. The vendor's flag was inert, and its receipt said ESTIMATE BILL on a settled tax invoice
(defects 3 and 4). Ours prints the heading the bill is, in English and Nepali (कर बीजक, संक्षिप्त
कर बीजक, बीजक).

**B. Reprints: an append-only print log, counted on the server, marked on the paper.**

The rule is the IRD's **Procedure Related to Computerized Invoicing, 2072, §6**:

- software may print an invoice **once**;
- a reprint must carry a visible **"copy of original"**;
- the reprint must state **how many times the invoice has been printed**. The procedure's own
  example is that after four copies, the last says the invoice has been printed five times.

Sources: [Pioneer Law, the procedure's text](https://pioneerlaw.com/procedure-related-to-computerized-invoicing-2072-2015/);
[the requirements as an approved vendor restates them](https://discuss.frappe.io/t/finally-got-approval-for-e-billing-from-ird-nepal-erpnext-v13-28-saas/93072).

A number that must be on the paper and be right across tills and cashiers cannot be a browser's.
So:

- `POST /pos/sales/{id}/prints` writes an `InvoicePrint` row and returns the receipt with its
  `PrintNumber`.
- Print 1 is the original. Every later print is boxed **COPY OF ORIGINAL · printed N times**, at
  the top and the bottom.
- The unique index (organization, invoice, number) makes it a count. Two tills printing one bill at
  once cannot both be copy 2; the loser gets a 409 asking to print again, never a silent
  renumbering.

**A log, not a counter column.** The same procedure's materialised view wants who printed and when,
and phase 46 prefers an append-only fact to a figure overwritten in place.

**Counted on request, not on paper.** A browser cannot tell whether its print dialog was cancelled.
So a cancelled dialog still counts, and the next print says "copy". That errs the safe way: an
explicable extra copy marking, never an unmarked second original.

**Permission: `Sales.Invoice.View` at the sale's location** (`ILocationScopedDocument`). A receipt
is the invoice printed. It is not the session owner's, because a customer returning tomorrow for a
copy is served by whoever is at the till.

Refusals:

- an ERP invoice is a 409 ("print it from the invoice page");
- a voided sale is a 409;
- a missing one is a 404.

Auto-print follows the location's existing *Invoice* print toggle (phase 60).

**C. Holds and the cart: in this browser, keyed by organization × location × session.**

The kickoff asked where a hold lives before phase 64. **In the browser.** A Retail hold parks a cart
for minutes at the same counter. The vendor makes each one an **approved, numbered Sales Order**
(SO0004 in phase 59's service), a ledger-numbered document for a basket nobody bought. A minimal
server `PosOrder` now would design phase 64's aggregate twice. So a hold:

- reserves no stock, takes no number and posts nothing;
- is lost with the browser's storage, which is the honest weight of a parked basket.

How the cart survives a reload without the vendor's cross-location leak (defect 8):

- its key is `erp.pos.cart:{org}:{location}:{session}`, and a session is one cashier at one
  location;
- the cart is built only once the session is known;
- a new session **sweeps** the keys its earlier sessions at the same till left, so yesterday's holds
  do not come back;
- **closing forgets** the session's cart. The close screen warns "N held cart(s) in this browser
  will be discarded".

Every storage read and write is guarded, so a private window or a full quota gives a working cart
that does not survive a reload. Recalling a hold **parks the cart in progress** rather than
discarding it.

**D. The till reads under the cashier's key, not the Admin's.**

`GET /pos/locations/{id}/settings` is `Pos.Settings.Manage`, which is Admin-only, so a Member
cashier could not even load the till. Three reads were added under `Pos.Session.Operate`, each
carrying only what a till acts on:

- `GET /pos/tills`: tills at active locations whose mode the tenant is still entitled to, where the
  caller holds `Sales.Invoice.Create` (the rule `OpenPosSession` already enforces), with the
  caller's open session.
- `GET /pos/tills/{id}`:
  - the settings subset;
  - the payment modes a sale would accept (linked, active, with an account; Cash first);
  - the walk-in and the default warehouse;
  - the categories that hold something sellable;
  - **`canSellOnCredit`**, which is whether the caller holds `Sales.Invoice.Approve` there.
- `GET /pos/tills/{id}/products` sells what is sellable *here*: `AvailableForSale`, active, not a
  variant parent, and available at the location. Its filters:
  - `code` is a scanner's exact match on Barcode, Code or SKU;
  - `search` is the typed search;
  - `categoryId` narrows to one category.
  - Rates are **VAT-exclusive after the tenant's `ProductPriceBasis`**, in every unit. A VAT-inclusive
    catalogue's 60 is rung up at 53.10.
  - The ERP's "most recent selling price" mode is deliberately not applied: that is a per-customer
    ERP suggestion, and a counter sells at the catalogue price.

The price-basis conversion was **lifted into `ProductPrices`** rather than copied: two copies of one
conversion is how one drifts.

Customer search and the Cash In/Out account list reuse the ERP's own endpoints (`Contacts.Contact.View`,
`Accounting.Account.View`, both Member-held). A custom cashier role needs those to name a
customer or move cash (§ 5).

**E. The total on screen is the server's total, pinned to a shared table.**

The cashier takes notes against the screen's figure before the server has seen the sale. A one-paisa
disagreement would be change or credit on the walk-in, which phase 61 refuses. A server round trip
per keystroke was rejected for a counter that waits on the network. So `pos-bill.ts` mirrors
`InvoiceLine.CreatePos` and `ApplyRoundOff`:

- every line figure is rounded to the paisa, half away from zero;
- service charge is taken on the rounded amount;
- VAT is on amount plus service charge;
- the bill is rounded to the nearest rupee.

The arithmetic is exact in `bigint` on inputs scaled to millionths, because
`Math.round(1.005 * 100)` is 100. `pos-bill-cases.json` holds 14 cases, generated with Python's
`Decimal` and read by both suites (`PosBillSharedCasesTests`, `pos-bill.spec.ts`), phase 26b's
arrangement. Tenders are added in integer paisa.

**F. The till is three flat lazy routes, and the shell steps aside.**

The routes are `pos`, `pos/till/:locationId` and `pos/sessions/:sessionId`, all `loadComponent` in
`app.routes.ts`.

**Not `loadChildren`**, because:

- `NavigationCatalog`, the page-title strategy and History derive from the top-level
  `Router.config`, so a child tree would hide the till's routes from all three;
- `build-budget.spec.ts` pins every route as `loadComponent`.

The full-screen layout is one signal in `App`: `isTillUrl(url)` hides the nav rail, header and the
`shell-with-nav` inset. The organization stays in scope, so dates still follow the BS toggle.
`@defer`'s trigger is unchanged.

The area is **Point of Sale**, a **leaf** like Reports: one screen, the launcher, from which a
cashier starts.

**G. Printing: a print root, an unencapsulated stylesheet, `window.print()`.**

- Each page renders its screen in `.pos-screen-root` and the receipt again in `.pos-print-root`.
- The receipt's styles (`ViewEncapsulation.None`) show only the print root under `@media print`,
  with `@page { margin: 2mm }`. They leave the document with the component, which Angular removes
  on destroy.
- The paper is 72 mm wide, the printable width of an 80 mm roll. Paper size is the printer
  driver's, because `@page size` cannot say "80 mm by however long".
- The receipt carries:
  - the heading (A) in English and Nepali, and the seller's name, address and PAN;
  - the location, bill number, BS/AD date per the toggle, and time;
  - the buyer block on a full invoice;
  - the lines (quantity, unit, rate, any discount);
  - Gross and Discount (when discounted), Sub Total, **Service Charge**, Taxable, Non-taxable when
    any, VAT 13%, **Round Off** and Total, with the total in words;
  - tenders, change and credit;
  - session, cashier, and who printed it when.
- The vendor printed neither service charge nor round-off (defect 3).

**H. A Restaurant till is listed, not offered.** The launcher shows a Restaurant location with
"the restaurant till (tables, kitchen orders and bill splitting) is not built yet". A Retail grid
opening onto a dine-in location would be a different product pretending to be it, and phase 49's
rule is that a chosen gap owes a surface.

**I. Smaller decisions.**

- **Credit is explained before Pay.**
  - On the walk-in: "The walk-in customer cannot take credit."
  - Without Approve: the message names `Sales.Invoice.Approve`.
  - Otherwise the cashier ticks "Leave X on {customer}'s account" before Complete enables.
- **Change only comes out of cash.** A card or e-payment over the bill disables Complete, with the
  reason (phase 61 Decision C).
- **The 422s** (`StockAvailability`, `CreditLimit`) show their message with *Sell Anyway*, which
  re-sends with only that override.
- **Keyboard.**
  - The search box is focused on load; F2 returns to it and F9 opens payment.
  - The payment dialog is modal: focus is trapped, Escape closes it, and focus returns to Pay.
  - Enter on an empty amount completes a covered bill, so a cash sale is **scan, F9, Enter,
    Enter**.
- **Order type** shows the location's tabs (Retail mode has Retail and Delivery) as a radio group,
  and is sent as `orderType`.
- **Field errors** use phase 47's `FieldError` on the tender amount, the cash amount and account,
  the counted amount and the closing note. Each is marked, described and focused.
- **The X report** comes from `GET /pos/sessions/{id}`, so from `PosSalesReader`, phase 61's one
  reader. A closed session shows the same table as its Z report, with counted, over/short and the
  note.

## 3. Bugs found and fixed

All are this phase's own, caught before shipping.

1. **The close form announced "Short by 1,500.00" before anything was counted.** With note-by-note
   counting, an empty count summed to 0 rather than "not counted". `counted` is now null until a row
   exists. Seen in the browser pass.
2. **The launcher explained an empty list under a 403.** "No location runs a till you can open"
   appeared beneath *(Pos.Session.Operate)*, which is a second, wrong reason. A failed load now shows
   the refusal alone, with a spec.
3. **Phase 24's picker guard flagged the till.** Its predicate was the method *name*
   `.listProducts(`, so `PosService.listProducts` (a server-side projection that already excludes
   variant parents) tripped a rule about `CatalogService.listProducts`. The guard was **taught**, not
   exempted (phase 55): a file can call the catalogue's method only if it imports the catalogue's
   service. A second assertion pins that the allow-listed Products screen still matches, so the
   narrowed predicate is not vacuous.
4. **A script edit put two backspace characters into that guard's regex.** The Bash heredoc
   collapsed `\\b` to `\b`, which Python then read as a backspace. The symptom was the new
   non-vacuity assertion failing. It was rewritten from a file with the Write tool (gotcha below).

## 4. Evidence

**Rule research:** five sources, linked in Decisions A and B.

**Migration** (`dotnet ef database update`, dev DB): applied. The scaffold was reviewed by hand and
contains:

- `PosLocationSettings.AbbreviatedTaxInvoiceEnabled` and `Invoices.IsAbbreviatedTaxInvoice`
  (`bit NOT NULL DEFAULT 0`);
- `sales.InvoicePrints`, with the unique `IX_InvoicePrints_OrganizationId_InvoiceId_PrintNumber`
  and the FK index on `InvoiceId`.

**E2E** on a fresh organization, *P62 Retail Till 192336*: VAT-registered, POS Retail, HeadOffice
renamed *Thamel Counter* and set to Retail with a default warehouse. Settings: 10% service charge,
round-off, cash verification on, abbreviated permitted. Payment modes: Cash → Cash In Hand and
Card → Nabil Bank. Products: Chicken Momo (service, 200, service charge), Coke 250ml (goods, 60,
barcode `8901234567890`, a 24-piece crate at 1,300, opening stock 100 @ 40) and Staff Meal (not
available for sale). Customer: Acme Retail (PAN 301234567).

Master data was seeded through the API (every status code printed, all as expected). The phase's
own screens were driven in the browser pane (`erp-web-ssl`, cookie transplant), with `window.print`
stubbed so the pane is not blocked and so each print's content could be read back.

| Area | Checked | Result |
|---|---|---|
| Launcher | full-screen, no rail; Start Session counts 500 × 2 | `SES0001`, float 1,000; lands on the till |
| Grid | Staff Meal (not for sale) | absent from `GET .../products` and the grid |
| Scan | `8901234567890` + Enter in the search box | Coke added, box cleared |
| Sale 1 | 2 momo + 2 coke on screen | Sub 520, SC 40, VAT 72.80, Round Off 0.20, **Total 633**, as phase 59 |
| | Card 133 + Cash 1,000 | change **500**, sale 0001, auto-printed **once, unmarked** |
| | receipt heading | **Abbreviated Tax Invoice / संक्षिप्त कर बीजक**, no buyer block, separate Service Charge and Round Off |
| Sale 2 | Acme Retail, 1 momo, Card 100 | "walk-in cannot take credit" gone; Complete disabled until "Leave 149.00 on Acme Retail's account" ticked; sale 0002 |
| | receipt heading | **Tax Invoice / कर बीजक** with customer, address and buyer PAN |
| Reprint | *Print a Copy* on 0002; *Reprint* on 0001 from the session page | **COPY OF ORIGINAL · printed 2 times** on both; 0001 keeps its stored *Abbreviated* heading |
| Holds | hold a Coke cart, start a Momo cart, reload | both survive; the storage key is `erp.pos.cart:{org}:{loc}:{session}` |
| Drawer | X report | 2 sales, 882 total, Cash 1,000 / Card 233, change 500, credit 149, expected 1,500 |
| | Cash Out 100 to Vegetables (the drawer's own account is not offered) | expected 1,400 |
| | close counted 1000×1, 100×3, 50×1, 20×2 = 1,390 without a note | refused at the note field, "Short by 10.00" |
| | close with "Coins short" | Z report: expected 1,400, counted 1,390, **−10.00**; this browser's held cart removed |
| Unit on the wire | sale 0004, 1 crate, by keyboard | line `crt`, factor **24** frozen, COGS unit 40 |
| 403-not-404 | `GET .../pos/sessions/{nonexistent}/sales` as Admin | **404** "Session not found." |
| | same user on a role granting `Sales.Invoice.Create/View`, `Tenancy.BillingLocation.View`, `Tenancy.Subscription.View`: `GET /billing-locations` | **200** |
| | same user, same nonexistent session, and `GET /pos/tills` | **403 naming `Pos.Session.Operate`** |
| | the launcher in the browser | banner: *You do not have permission to perform this action (Pos.Session.Operate).* |
| | own role restored | via `sqlcmd` (phase 52's recipe), confirmed *Admin* |
| ERP side | *Configurations > Point of Sale* | the switch, with Rule 18 under it, wired by `aria-describedby` |
| Console | a fresh till load as Admin | no failed requests |

**SQL agrees with the screens:**

```
Code Channel OrderType Status   Session Customer      Abbrev GrandTotal RoundOff Tendered Change
0001 Pos     Retail    Approved SES0001 Cash Customer 1      633.00     .20      1133.00  500.00
0002 Pos     Retail    Approved SES0001 Acme Retail   0      249.00     .40      100.00   .00

entries per sale, each balanced: 0001 713/713 + 633/633; 0002 249/249 + 100/100
0001 tender entry: Dr Cash In Hand 500 (1,000 tendered - 500 change), Nabil Bank 133 | Cr AR 633
0002 tender entry: Dr Nabil Bank 100 | Cr AR 100          -> AR 149 left on Acme
SES0001: Dr Vegetables 100 / Cr Cash 100;  Dr Cash Over/Short 10 / Cr Cash 10
session row: Closed, float 1000 (500x2), expected 1400, counted 1390 (1000x1,100x3,50x1,20x2), -10, "Coins short"
prints: 0001 #1, #2; 0002 #1, #2
balances: Cash In Hand 390 (= 1,390 counted - 1,000 float) | Nabil Bank 233 | AR 149 | Over/Short 10
trial balance: Dr 1805.00 = Cr 1805.00      (after sales 0003-0004 in SES0002: 5879.00 = 5879.00)
three-view law (opening stock posts no GL): layers 3920.00 = movements 3920.00 = GL Inventory + 4000 opening = 3920.00
```

**Keyboard census (phase 40).**

- **The till, empty cart: 12 tab stops**, in order: search, All / Drinks / Food, the two tiles,
  Change Customer, Bill discount, then (wrapping) the skip link, Order type (one stop for the radio
  group), Session & Cash, Exit Till, back to search. Every one has a name. Hold, Held carts, Clear
  and Pay are disabled and correctly skipped.
- **With a line in the cart**, five more stops: One fewer / Quantity / One more / Edit / Remove,
  each naming the product. Edit carries `aria-expanded`; Enter expands it, and Unit, Price and
  Discount follow. ArrowDown on Unit re-prices the line to the crate's 1,300.
- **A keyboard-only sale**: scan, Enter, F9 (focus lands in the amount, prefilled 68.00), Enter
  (cash tender), Enter (complete) gives sale 0003. Focus moves to New Sale; Enter returns it to the
  search box. The live region announced "Added Coke 250ml…" and "Sale 0003 complete."
- **The payment dialog**: 21 controls. 30 × Tab and 30 × Shift+Tab gave 60 focus moves, all inside
  the dialog. Escape closes it and focus returns to *Pay (F9)*.
- Found and fixed by the census: the line's *Edit* was named "Edit" on every line. It now carries the
  product name, as the other line controls do.

**Tests that bite.** Both were proven by injecting a regression and restoring the file by hash, then
`touch`:

- dropping the walk-in condition fails `A_named_customer_always_gets_the_full_tax_invoice`;
- swapping `pos-bill.ts`'s half-away rounding for truncation fails 9 of the 17 shared-table specs.

Tests that read the money and the rules:

- `PosBillSharedCasesTests` (14 cases plus a non-vacuity check), `PosReceiptRulesTests` (the limit,
  the print count, amount in words);
- `PosTillTests`, 21: launcher filtering, the till's offered modes, credit, refusing a non-till,
  grid filtering, exact scan, VAT-inclusive pricing in two units, all five abbreviated-rule cases,
  print numbering, the receipt's lines, discount, void and ERP refusals, 404, and the session's
  sales;
- Angular `pos-bill.spec` (shared table), `pos-cart.spec` (11, including defect 8 and the sweep),
  `pos-till-page.spec` (13), `pos-launcher-page.spec` (5), `pos-session-page.spec` (6),
  `app.spec` (till mode).

## 5. Left open, and for later phases

- **The ERP's own invoice print does not follow either rule.** The QuestPDF print (phases 20d/27b)
  titles every invoice "Invoice", never "Tax Invoice", and neither counts nor marks a reprint. The
  2072 procedure applies to every tax invoice, not only the till's. Fixing it means the PDF path
  writes print rows. That is a GET that writes, and the email pipeline renders the same PDF (phase
  30), so it is its own decision.
- **Serial-tracked products at the till.** The cart sends no `serialNumbers`, so selling one is the
  server's 400. Batch-tracked products sell, because the batch is optional on an issue. A serial
  picker, or keeping such products off the grid, is owed before a tenant with serials uses the
  till.
- **The barcode lookup is unindexed.** It is an exact match on Barcode, Code or SKU over the
  tenant's products. Measure `(OrganizationId, Barcode)` on `tools/scale` before adding it (phase
  50: no unmeasured index).
- **The print race** relies on the unique index, which InMemory does not enforce. It was not raced
  against SQL Server here.
- **A narrowly scoped cashier role** needs `Contacts.Contact.View` to name a customer and
  `Accounting.Account.View` to move cash, because those lists are the ERP's.
- **A session of a tenant that lost its entitlement** can be closed through the API (phase 61), but
  the session page cannot load: `GET /pos/sessions/{id}` is feature-gated, and so is the route.
- **A restored cart keeps the prices it was built with.** A catalogue price change between a hold
  and its recall is not re-read.
- **No printed X/Z report**; the session page is a screen.
- Phase 61 §5 stands: an ERP credit note against a till sale returns no service charge (phase 63),
  and the full-tenant export has no service-charge or round-off column.
