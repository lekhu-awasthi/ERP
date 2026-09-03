# Phase 26c status — Report catalog completion: inventory, tax, system, analytics

**TL;DR.** The Reports catalogue is complete. Nine reports shipped — **Inventory Position**,
**Inventory Movement**, **Inventory Ledger**, **Inventory Master Report**, **Sales Return
Register**, **Purchase Return Register**, **Net Trading Assets**, **Exceptional Report** and **User
Log** — plus the `.xlsx` export the three manufacturing reports have lacked since phase 25. Twelve
new exports in all.

**The phase's key question went the opposite way from the plan.** The roadmap asked whether the main
Sales/Purchase Registers must now *exclude* credit and debit notes once separate return registers
exist, and the 2026-09-02 catalogue pass had inferred that they must. Generating both reports over
the same period on the live tenant on 2026-09-03 showed otherwise: **the same twelve credit notes
appear in both**, parenthesised (negative) in the Sales Register and positive in the Sales Return
Register, with the main register's footer Total arithmetically net of them. Phase 19's folding was
correct parity, not a simplification. **`SalesRegisterQueryHandler` and
`PurchaseRegisterQueryHandler` are unchanged in behaviour** — the only change is that their
note-side magnitudes now come from the same readers the new registers use, so the two can no longer
drift apart.

**The Purchase Return Register is not the Sales Return Register's mirror**, which the roadmap also
predicted. It is the *Purchase* Register's mirror: seven money columns carrying that register's
Capital-versus-Others and Local-versus-Import split, against the sales side's four. So the pair is
two handlers, and 26b's "one handler discriminated by the side the route hardcodes" pattern
deliberately does not apply.

**One new stored entity, `UserLoginEvent`** — the only thing this phase persists. It carries no
`OrganizationId` (signing in happens before an organization is chosen, and a failed attempt has none
even in principle); the *report* is scoped by joining to `OrganizationMembership` instead. It is
written from `AuthEndpoints`, not from `LoginCommandHandler`, and the failure it most exists for is
written on a path that throws.

**Two shared readers carry the phase.** `Inventory.Reports.StockFactReader` produces the
Opening/In/Out/Balance fact set that Inventory Position, Inventory Movement, the Inventory Ledger's
bracket rows and Net Trading Assets' Inventory Items row all read, so those four agree *by
construction*. `SalesReturnReader`/`PurchaseReturnReader` do the same for the four register screens.
That is phase-26b's `ContactLedgerReader` rule applied twice more.

**Nine new permission keys, three Admin-only and six Admin+Member.** Eighteen permission-seed rows
and one new table are the whole migration.

Verified end to end against the real API and database: a fresh Organization seeded by curl with
stock moving **both** directions plus one Credit Note and one Debit Note; all nine reports pulled
with real data; all nine `.xlsx` exports returning real workbooks (and the three manufacturing export
routes binding); all nine negative paths returning **403 naming their exact key** against a
nonexistent id; and `sqlcmd` confirming the inventory balance three ways — the FIFO layers
(`Σ QuantityRemaining × UnitCost` = 123 units / 1,330.00), the movement reconstruction the reports
actually compute (123 / 1,330.00), and the reports themselves (123 / 1,330.00).

Tests: Domain **316** (+5), Application.UnitTests **678** (+49), Api.IntegrationTests **18**
(unchanged), Angular **155** (+20). `dotnet build` / `dotnet test` / `ng build` / `ng test` /
`tsc --noEmit` all clean.

---

## Confirm-live pass (Moonbeam UAT tenant, 2026-09-03, read-only)

All nine screens were generated before any DTO was designed, per the phase-8f rule. Period used
throughout: 17-07-2026 to 03-09-2026 (the tenant's "This Fiscal Year to Date"). Nothing was saved;
only GENERATE and filter-drawer toggles were clicked.

A note on getting there: the automation browser's accessibility tree is nearly empty on this
product (custom `div`s, no roles), and `find` matches nothing. Clicking has to go through
coordinates read from `getBoundingClientRect`. **The GENERATE control's DOM text is "Generate", not
"GENERATE"** — the capitals come from CSS `text-transform`, so a case-sensitive query returns zero
elements. That is phase-23's component-test gotcha met from the other side, and it cost several
attempts.

### The key finding — the main registers keep their notes

- **Sales Return Register** listed CN0001..CN0011 plus `132462` as **positive** rows, 11 rows,
  footer Total **93,831,004,682,895.66**.
- **Sales Register**, generated immediately afterwards over the same period, listed **the same
  twelve credit notes as parenthesised (negative) rows** interleaved with the invoices, footer Total
  **(93,831,004,637,827.06)**. Its positive invoice rows sum to 45,068.60, and
  45,068.60 − 93,831,004,682,895.66 = −93,831,004,637,827.06 exactly.

The Sales Register's drawer also carries a View Option **"Include Credit Note In Calculation"**. It
was toggled from unchecked to checked and APPLY FILTERS pressed: the rendered rows and every total
were **identical in both states** on this tenant. Recorded as observed; not modelled.

### Sales Return Register — `sales-return-register`

Filters: Period. Drawer: Period + View Options → **Group By Bill** (checked). Two-tier Devanagari
header: बीजक (मिति / बीजक नम्बर / खरिदकर्ताको नाम / खरिदकर्ताको स्थायी लेखा नम्बर), जम्मा फिर्ता,
स्थानीय कर छुटको फिर्ता मूल्य, and करयोग्य फिर्ता split into मूल्य and कर. One row per approved
Credit Note, values positive, footer **Total**, zero rendered `-`, paged 100.

### Purchase Return Register — `purchase-return-register`

Header groups: बीजक / प्रज्ञापनपत्र नम्बर (मिति, बीजक नं., प्रज्ञापनपत्र नं., आपूर्तिकर्ताको नाम,
आपूर्तिकर्ताको स्थायी लेखा नम्बर), जम्मा फिर्ता मूल्य, कर छुट हुने वस्तु वा सेवाको फिर्ता / पैठारी
मूल्य, करयोग्य फिर्ता (पूंजीगत बाहेक) मूल्य+कर, करयोग्य पैठारी फिर्ता (पूंजीगत बाहेक) मूल्य+कर,
पूंजीगत करयोग्य फिर्ता / पैठारी मूल्य+कर. Ten rows, positive, footer Total. The प्रज्ञापनपत्र नं.
column was empty on every row.

### Inventory Position — `inventory-summary`

Top bar: Period, Product Category, Product. Drawer: Period; Products (Category, Product); **Show
Columns** → Group by Warehouse, Display Warehouse in Column; **View Options** → Item with Positive
Balance only / Item with Negative Balance only / Show All; **Reporting Tags** (six categories).

Columns: Code/Goods (`Name (Code)`), Category, Qty, UOM, Rate, Amount. One row per product. Footer
Total over Qty and Amount only. Paged 100 (`1 - 100 / 314`).

**Critical semantic:** a product with a *negative* Qty prints `-` in **both** Rate and Amount. Value
is carried only for a positive balance.

### Inventory Movement — `inventory-moment`

Top bar: Period, Product Category, Product, Warehouse. Four column groups × three: Opening, In, Out,
Balance, each Quantity/Rate/Value. One row per product.

**Balance quantity is Opening+In−Out** (verified on three rows), but **Balance value is not**:
`Bluffo pebble hc 8` showed In 13 @ 43.154 = 561 with no Out, yet Balance 13 @ 23 = **299**. So the
live Balance value is read from the FIFO remaining layers independently of the movement values —
which means **Balance is Inventory Position's Qty/Rate/Amount**, the same figures.

### Inventory Ledger — `inventory-moment-summary`

Top bar: Period, **Product (required, multi-select)**, Warehouse. Refuses to generate without a
product ("Please select a product"). Sectioned per product. Columns: Date, Type, Contact, Warehouse,
#No, then In / Out / Balance as Qty-Rate-Amount triples. An **Opening Balance** row dated the period
start and a **Closing Balance** row dated the period end bracket the movements; the pager counts only
the movement rows (`0 - 0 / 0` for a product with none). Detail General Ledger's shape, for stock.

### Inventory Master Report — `inventory-materialized`

Top bar: Period, Contact, Product, Txn Type. Nineteen columns: Entry Date, Contact, Type, Warehouse,
Account, Entry No, Reference No, Code/Product Name, Product Category, Quantity, UOM, Rate, Amount,
Item Discount, Transaction Discount, Net Amount, Vat Amount, Total Amount, Additional Cost.
Descending by date.

One row per **document line**, not per stock movement — service lines with no warehouse and no stock
effect appear too. **Quantity is signed by stock direction**: Invoice negative, Purchase Bill
positive, **Credit Note positive**, Debit Note negative, Production Journal both. Types observed:
Invoice, Purchase Bill, Credit Note, Debit Note, Inventory Adjustment, Production Journal. `Account`
showed the product's mapped account ("Sales Goods", "Purchase Goods", "Sales Service", "Purchase
Service") and was blank on Production Journal and Inventory Adjustment rows.

### Net Trading Assets — `net-trading-assets`

Top bar: Period, **Compare**, **Exclude Advance**. Columns Particulars / Balance. Every leaf carries
an expand triangle. Rows and figures:

```
Receivables                         20,010,977,946
  Receivables from Customers            6,878,340.24
  Advance to Suppliers              20,004,099,605.76
Payables                        93,831,490,052,796.44
  Payable to Suppliers             1,252,274,194.09
  Advance from Customers      93,830,237,778,602.34
Inventory Items               (931,290,922,025,915.5)
Net Trading Assets          (1,025,102,401,100,766)
```

Both identities check to the last decimal: each parent is the sum of its two children, and
**Net Trading Assets = Receivables − Payables + Inventory Items**.

### Exceptional Report — `exceptional-report`

Filter: Period only. Columns Particulars / Balance. Twelve fixed rows, each a magnitude with a DR/CR
marker — **except the two inventory rows, which carry no marker at all**:

```
Inactive Accounts with Outstanding Balances    93,830,105,610,611.8 CR
Minor Account Balance Exception                                153 DR
Expense Accounts with Credit Balances                   97,877,858 CR
Income Accounts with Debit Balances         93,831,004,637,827.06 DR
Asset Accounts with Credit Balances                      312,667.5 CR
Liability Accounts with Debit Balances                     6,352.3 DR
Customers with Credit Balances                      131,972,015.55 CR
Bank and Cash Accounts with Negative Balances   20,126,171,067.25 CR
Suppliers with Debit Balances                   20,004,099,508.26 DR
Inactive Inventory Items with Balances               8,538,879.5      <- no marker
Negative Inventory Balances                             62,848.569    <- no marker
Non-actionable Account Balances                                0 CR
```

Note that "Income Accounts with Debit Balances" equals the Sales Register's own net total to the
cent, which is consistent with these being GL balances as of the period end.

### User Log — `user-log`

Top bar: Period, User. Columns Full Name, Email, Date (`03-09-2026 01:05:32 PM`), Device (the OS:
`Windows 10`, `Intel Mac OS X 10_15_7`, `Android 10`), IP Address, Description, Device Info (browser
+ version: `Chrome 152.0.0.0`, `Safari 1.44121.4`, `Firefox 154.0`, `Edge 152.0.0.0`). Descending by
timestamp. Description values seen: **Login Success**, **Logout Success** (`Login Fail` is recorded
in the 2026-09-02 catalogue pass; none fell inside this window). Full Name falls back to the email
when the user has no name set.

---

## Decisions

### Decision A — the main registers keep their notes, and the readers make that safe

The roadmap called this "the phase's key correctness question", and it was answered by generating
the two reports back to back rather than by reasoning about them. The answer is that the product
shows a return **twice**, in opposite signs: the return register states returns on their own, the
main register states sales net of them. Both are correct and both are wanted.

That could have been left alone — the new registers are additive, and nothing forced a change. It
was not left alone, because two reports over the same documents computing the same magnitudes in two
places is precisely the setup phase-26b named: agreement has to be a design property. So
`SalesReturnReader` and `PurchaseReturnReader` were extracted, and **both the old register handlers
and the new ones read them**. Behaviour is unchanged — the 39 pre-existing register tests passed
untouched through the refactor — but the magnitudes are now one computation.

`PurchaseReturnReader` carries the harder half: a `DebitNoteLine` has no `ExpenditureClassification`
and no `IsImport` of its own, so both are resolved from the source Purchase Bill's matching line by
`(PurchaseBillId, ProductId, Rate, VatRate)` — the join `AnnexThirteenReportQueryHandler` already
uses. A standalone debit note with no referrer falls back to Others/local, which is what the register
did before the extraction.

The live "Include Credit Note In Calculation" toggle is **not modelled**. It was exercised in both
states and changed nothing observable, so there was nothing to reproduce; guessing at a meaning would
have been worse than recording the observation.

### Decision B — `StockFactReader` derives everything from `StockMovement`, not from the FIFO layers

The FIFO layer table cannot answer a dated question. `StockLedgerEntry.QuantityRemaining` is
decremented **in place** as later documents consume a layer, so it only ever describes stock as it
stands right now. A report whose header says "for the period … to 30 Bhadra" and whose Balance column
silently answered "as of today" would be wrong in the one case a reader most needs it — reopening a
closed period.

`StockMovement` is append-only and carries the consuming document's own weighted-average unit cost,
so Opening + In − Out reconstructs both quantity and value at any date. At today's date the two
agree, which is what CLAUDE.md's "a live inventory value comes from `QuantityRemaining × UnitCost`"
gotcha is really asserting — and the E2E proves it, computing 123 units / 1,330.00 three independent
ways.

The one place the arithmetic deliberately stops: **when Balance quantity is zero or negative, Balance
value is reported as zero.** There is no cost to carry for goods that are not there, and the live
report agrees (every negative row printed `-` in both Rate and Amount).

**That branch is unreachable in this codebase today, and is kept on purpose.**
`StockLedgerService.ConsumeAsync` *throws* a 409 when a document would consume more than the layers
hold, so no approval path can drive a balance below zero — where the reference product's "Negative
Item Balance" setting offers Reject / Warn / Do Nothing and its own tenant runs warn-and-allow, which
is why its Inventory Position has hundreds of negative rows and ours can have none. A test pins the
throw, and says in its name that it is the reason the guard is unreachable, so the guard is not
quietly deleted as dead code before the setting that needs it is built.

### Decision C — four inventory reports, not one parameterised screen, and the phase-7 queries stay

`ProductStockPositionQuery` and `InventoryLedgerQuery` (phase 7) are untouched. They answer the
Inventory **module**: no date range, no valuation, opening hardcoded to zero, gated by
`InventoryLedgerView` alongside the product screens. The four new ones are **reports**: dated, valued,
category-filterable, paginated, exportable, separately permissioned. Growing the old queries a period
and a valuation to serve both would have changed what four shipped screens return, and a report page
is the thing being added.

They are four screens rather than one because their filters genuinely differ — the Ledger *requires* a
product and the others do not, the Master takes Contact and Txn Type and no warehouse — but they are
one reader, which is where the duplication actually mattered.

### Decision D — Inventory Master covers six document types, not eight

Invoice, CreditNote, PurchaseBill, DebitNote, InventoryAdjustment and ProductionJournal — every type
the live report's own rows exhibited. `WarehouseTransfer` and `OpeningStock` also move stock and are
deliberately absent: both are internal repositionings with no counterparty, no rate and no tax, so
every one of the money columns this report exists for would be blank, and a transfer would appear
twice (once per leg) as a pair netting to nothing. Neither appeared in the live output. Recorded as a
confirm-live follow-up rather than guessed at.

Its **sign convention is stock direction**, deliberately the opposite of `TradeLineReader`'s
return-negating convention: an invoice takes stock out and a credit note puts it back, whatever
either does to revenue. The two must not share a loader, and do not. A test asserts all four signs.

The **Warehouse** column is read from the stock movements the line produced rather than from the
document header, because CreditNote and DebitNote have no `WarehouseId` of their own (a credit note
is stocked back at its source invoice's warehouse) and a service line produces no movement at all and
must show a blank cell.

**Additional Cost ships always empty.** The live Purchase Bill's Additional Cost section (Cost Terms
× Product × Value/Quantity allocation) is not modelled here — phase 20c built the `CostTerm` lookup
and nothing consumes it yet. The column is carried rather than dropped so the shape matches, with the
gap stated on the screen.

### Decision E — the Exceptional Report is one parameterised sweep, and the twelfth row says it is not modelled

Twelve rows, **three queries**: one pass over the chart of accounts joined to GL balances answers
every account row as a predicate, one pass of `ContactLedgerReader` per side answers both contact
rows, one pass of `StockFactReader` answers both stock rows. Twelve independent queries would
multiply the round trips by four and — worse — let two rows that read the same accounts disagree.

Two details of the live report are honoured rather than smoothed over. **The two inventory rows carry
no DR/CR marker**, because a quantity and a stock valuation do not sit on a side of the ledger.
And **"Non-actionable Account Balances" is flagged un-modelled**: it describes an account a user
cannot post to or correct, which this chart of accounts has no concept of — every `Account` here is
postable. It ships as a real row returning zero with `IsModelled = false`, and the screen and the
`.xlsx` say why. That is one step past phase-26b's Service Charge precedent: there the column was
omitted because it had siblings to carry the meaning, here the row's absence would silently change a
twelve-row report's identity.

`MinorBalanceThreshold` is a **declared** constant (1.00), not a reproduction: the live report gives
no threshold, so the number is stated in the code with its reasoning rather than hidden in a
predicate.

### Decision F — `UserLoginEvent` has no `OrganizationId`, and is written from the endpoints

Signing in is an application-level act, not a tenant one — a user authenticates first and only then
picks an organization, and a failed attempt has no organization to belong to even in principle.
Making the row tenant-scoped would mean inventing a tenant for the rows that matter most.
`UserLogQueryHandler` scopes the **report** instead, by `OrganizationMembership`, in two parts:
events whose `UserId` is a member, **plus** events with no user id whose attempted email matches a
member's. The second half is what makes an attack on a colleague's address visible to their Admin;
an attempt against an address belonging to nobody here is deliberately invisible, because it is not
this tenant's business and showing it would leak the existence of other tenants' users. The E2E
proves both halves: a failed attempt against the member's own address appears, one against
`intruder@nowhere.test` does not.

It is written from `AuthEndpoints` rather than inside `LoginCommandHandler` for three reasons, any
one of which would be enough. (1) The failed-login row has to be written on a path that **throws** —
`AuthenticationFailedException` is how a bad password is reported — and a handler that saved a row
then threw would be making its own failure path transactional in a way nothing else here is.
(2) Logout has no handler at all; it is a cookie deletion in the endpoint. (3) IP address and
User-Agent live on `HttpContext`, which the Application layer deliberately cannot see.

**Recording is never allowed to break authentication.** A login that succeeded has succeeded whatever
happened to the audit row, so `RecordAsync` swallows and logs. That is the opposite of the call
`AuditBehavior` makes, and deliberately: this row sits on the unauthenticated edge, where a write
failure must not become a way to deny someone their session. (It also means the change cannot break
`Api.IntegrationTests`' login flows even if that host's schema lagged.)

A failed login is recorded for **any** failure the command reports — wrong password, unknown address,
unverified email. From a log reader's point of view they are one event, and distinguishing them in
the report would disclose which addresses exist.

### Decision G — the user-agent parser is a small ordered pattern set, and order is the whole algorithm

`UserAgentReader` reproduces the live report's two columns: the OS verbatim from the header
(underscores and all — `Intel Mac OS X 10_15_7` is what the product prints) and browser + version.
It is deliberately not a user-agent database: a login log needs to say "a Chrome on Windows, from
this address" well enough for a human to spot the session they do not recognise, and a dependency
that must be kept current to stay accurate is a poor trade. Anything unrecognised returns null and
the report renders a blank cell.

**Order is the whole algorithm.** Every Chromium browser also claims "Chrome", and every one of them
also claims "Safari", so Edge and Opera must be tested before Chrome and Chrome before Safari.
Getting that wrong still compiles and still returns a plausible answer, so three tests pin the three
orderings — and the E2E proved it live by signing in with a real Edge agent and reading back
`Windows 10` / `Edge 152.0.0.0`.

Parsing happens at **write** time, so there is one parser to test and a plain read on the way out;
the raw header is kept so a future reading can be re-derived.

### Decision H — Net Trading Assets compares as an as-of report, and does not duplicate its own detail

Every figure is a closing balance, so despite the "for the period" header nothing here is a period
measure — a receivable is what is owed on a date. Phase-26a's `ComparePeriod` already settled that an
as-of report compares against the same calendar date one year earlier, and the window used is echoed
on the response so the screen and the `.xlsx` label the column with a real date rather than the word
"prior".

The live report's leaves all drill down to per-contact and per-item detail. That detail already
exists here as three shipped reports — Customer Receivable Summary, Supplier Payable Summary,
Inventory Position — which read the same readers and therefore agree with these totals by
construction. Duplicating them inside this response would be a second way to ask the same question;
the screen links to them instead.

### Decision I — permission keys

**Nine keys, three Admin-only and six Admin+Member.**

Admin-only:

- **`UserLogView`** — the strongest case in the codebase, and not for the usual reason. This
  discloses, per colleague, the IP address they signed in from, the device and browser they used, and
  the minute they did it, plus the addresses that failed. That is surveillance-grade data about
  people rather than commercial data about the business.
- **`InventoryMasterView`** — the flat per-line fact table across every stock-affecting document,
  carrying the contact on each row beside its rate, discounts and margin-revealing cost. Strictly more
  disclosure than `SalesMasterReportView` and `PurchaseMasterReportView` together, both already
  Admin-only.
- **`SalesReturnRegisterView` / `PurchaseReturnRegisterView`** are *not* in this set — see below.

Admin+Member:

- **`InventoryPositionView` / `InventoryMovementView` / `InventoryLedgerReportView`** — quantity, rate
  and value per product, no contact anywhere. The `StockAgeingView` shape, Admin+Member since phase 19,
  and the working data a stock operator needs hourly. The Ledger is the same kardex the product detail
  page already links to under `InventoryLedgerView`.
- **`NetTradingAssetsView` / `ExceptionalReportView`** — four-row and twelve-row whole-tenant rollups.
  No contact, no product, no document number; the Trial Balance's own shape.
- **`SalesReturnRegisterView` / `PurchaseReturnRegisterView`** take their parent register's class.
  `SalesRegisterView` and `PurchaseRegisterView` have been Admin+Member since phase 19, and each
  return register is a strict **subset** of its parent's rows — the same notes, confirmed live in
  both. A subset cannot warrant more protection than the superset a Member already holds; making them
  Admin-only would restrict data those users can already read, exactly the inconsistency phase 14's
  matrix editor makes visible. They do expose a counterparty PAN, but so does the parent register, and
  that is where the decision was already taken.

---

## Bugs found and fixed

**1. Decimal negative zero leaked into Net Trading Assets' Compare column.**
`ContactSidesAsync` accumulated the credit side as `else { credit += -balance; }`. That is
arithmetically equivalent to the guarded form but **not** equivalent in `decimal`: `-0m` keeps its
sign bit, so a contact sitting at exactly zero produced a negative zero that survived into
`(double)value` and rendered as **`-0`** in the `.xlsx` and would have rendered `-0.00` on screen.
Caught by reading the generated workbook's raw cell XML during the E2E, not by any test — every
assertion compares `decimal` values, and `-0m == 0m` is true. Fixed by contributing only when the
balance is strictly non-zero.

**2. The E2E's own seeding, twice, silently.** Recorded because both are traps for the next session's
seed script rather than product bugs: `POST /api/organizations` returns `organizationId`, not `id`;
and the accounting and inventory GL defaults are **one** `PUT /accounting-defaults` endpoint taking
all eleven accounts, not two. Both failures were invisible because the seed piped approvals to
`/dev/null` — the approvals returned 409 ("Default Inventory account is not configured") and the
first report simply came back empty. The script now prints every approval's status code.

---

## Browser pass

All nine screens were opened against the seeded organization over HTTPS, using phase-25's recipe
(`.claude/launch.json`'s **`erp-web-ssl`** profile plus transplanting curl's `erp_auth` token via
`document.cookie` — no password typed into the login form). `/api/auth/me` returning 200 from the
pane confirmed the session before anything else was read.

What the pass confirmed beyond "it renders":

- **Inventory Movement's four-group header** works as a two-tier `<thead>`, and widening the period
  from the default month to 2026-05-01 moved the figures out of Opening and into In/Out exactly as the
  API returns them: Opening 0, In 158.000 @ 10.633 = 1,680.00, Out 35.000 @ 10.000 = 350.00, Balance
  123.000 @ 10.813 = 1,330.00.
- **Inventory Ledger's required-product empty state** ("Select a product to see its ledger", both
  Export buttons disabled) renders before a product is chosen, and after choosing one the Opening
  Balance and Closing Balance rows bracket five movement rows with a correct running balance and a
  resolved Contact and Warehouse on every row — including the two notes, which have no warehouse of
  their own.
- **Inventory Master's signed quantities** render as `-5.000` and `-30.000` — read back out of the DOM
  rather than off the screenshot, because at report zoom a decimal point and a thousands comma are
  indistinguishable and `-5,000` would have been a very different number.
- **The Exceptional Report's two details** both survive to the screen: the two inventory rows show
  `—` in the DR/CR column, and the twelfth row carries its "not modelled" badge above the explanatory
  note. The deactivated-product anomaly seeded during the E2E shows as 1,330.00 against "Inactive
  Inventory Items with Balances", so the predicate is visibly firing rather than merely returning zero.
- **Net Trading Assets** shows the indented hierarchy, a Compare column headed "Balance as at
  03-09-2025" — the real date, never the word "prior" — and, after bug 1's fix, **no `-0`** anywhere.
- **User Log** renders its three outcomes in three colours (green Login Success, grey Logout Success,
  red Login Fail), with the Edge session showing `Windows 10` / `Edge 152.0.0.0` and the failed attempt
  showing the email in the Full Name column.

## Carried forward

New this phase:

- **`WarehouseTransfer` and `OpeningStock` in Inventory Master** — deliberately out (Decision D);
  worth a live re-check of the report's Txn Type filter to confirm the reference product excludes
  them too.
- **The Sales Register's "Include Credit Note In Calculation" toggle** — observed, inert on the live
  tenant, not modelled.
- **Additional Cost** — the Inventory Master column is always empty until the Purchase Bill's
  additional-cost allocation is modelled (the `CostTerm` lookup exists since 20c, unconsumed).
- **Negative stock is unreachable** until the "Negative Item Balance" setting (Reject / Warn / Do
  Nothing) is built; `StockFactReader`'s zero-value guard is waiting for it.
- **Inventory Position's Reporting Tags filter and its Group-by-Warehouse display options** were seen
  live and not built; the report is per-product across warehouses with a warehouse *filter*.

Inherited and still open: BS dates in server-rendered PDFs and `.xlsx` (phase-23 Decision A — now
inherited by this phase's twelve new export routes too; 27b closes it with `BsCalendar`); phase 25's
named follow-ups; phase-26a's two; and phase-26b's four.
