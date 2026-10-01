# Phase 61 — POS sale engine: a till sale is an Invoice with two entries, and the drawer posts

## TL;DR

**A till can now sell, take payment, move cash and close, and every rupee reaches the ledger.** A
sale is an ordinary Invoice created and approved in one command. Its service charge is computed per
line from persisted inputs, it is rounded to the rupee, and its tenders post as a **second GL entry
on the same invoice**. Everything that asks what a customer owes now sees the till's settlement:
ageing, the contact ledger, credit control and allocation suggestions. A `PosSession` is one
cashier's drawer:

- the float is counted note by note;
- Cash In/Out names an account and posts;
- the close freezes expected cash and posts over/short.

The session total and the day total come from one reader.

| | |
|---|---|
| Live read taken | **no**. Phase 59 wrote a whole service through the vendor's till. The user offered the live tenant mid-phase, and nothing here needed it (Decision B notes the one fact it could re-confirm) |
| Rule question settled first | **yes**: phase 59 §6 q1, from the statutes. The answer was bigger than the question (Decision A), and the user chose how to build it |
| Migration | `Phase61PosSaleEngine`, scaffolded and **not** hand-edited: additive columns whose defaults (`Erp`, `0`) are true of every existing row, three tables, four seed rows |
| New tables | `pos.PosSessions`, `pos.PosCashMovements`, `sales.InvoiceTenders` |
| New permission keys | **2**: `Pos.Session.Operate` (Admin+Member), `Pos.Session.ViewAll` (Admin-only) |
| New `DocumentType` member | `PosSession`, classified as not-a-document in all three sweep lists |
| Readers swept | **14** changed (plus the Sales Master export) to see service charge, round-off or tenders; 4 product-level readers deliberately left alone (Decision J) |
| Bugs found in passing | **1** pre-existing (the GL panel's entry order), plus 1 of this phase's own caught before shipping (§ 3) |

**Four things worth carrying forward.**

1. **Settle a rule question from the rules, and expect the answer to move the question.** "Is
   service charge in the VAT base?" was settled (yes, VAT Act 2052 §13). Settling it also found that
   Nepal's Supreme Court made a mandatory service charge unlawful in January 2023. That is a product
   decision, and it went to the user, who chose "build it, and say so where it is switched on".
2. **A second door onto an approved document breaks every guard that assumed one.** The metered,
   location-marker and stock-book guards all failed on `CreatePosSaleCommand` or `PosSession`. Each
   was taught a named, reasoned "second door" or "source" entry rather than given an exemption
   (phase 55's rule).
3. **Two entries per document is now the normal case, and nothing new had to be written for it.**
   `SourceDocumentGlEntries` nets every entry, so Void reverses the sale and its tenders together.
   The voided sale's accounts net to zero in SQL.
4. **Phase 60's own evidence had already falsified its rounding assumption.** It modelled
   `round_amount` as "round to the rupee, observed 632.80 → 633". The scan's next row has
   316.40 → 317, which is a ceiling. This phase rounds to the nearest rupee on purpose (Decision B).

Tests: Domain **797** (+20), Application.UnitTests **1425** (+28), Infrastructure.UnitTests 13,
Api.IntegrationTests **30** (run this phase, Docker up), Angular 632. `dotnet build`, all four .NET
suites, `ng build` (645.98 kB initial, unchanged) and `ng test` are green.

---

## 1. What shipped

**Domain**

- `Invoice` gains:
  - `Channel` (`SalesChannel.Erp | Pos`), `PosSessionId`, and `OrderType`, which reuses `PosTab`
    because the vendor's `order_type` takes exactly the tab's values;
  - `RoundOff` (signed), `ChangeAmount`, and the child `Tenders`;
  - three methods: `CreatePosSale`, `AddPosLine` and `ApplyRoundOff`;
  - `Settle(tenders, change)`, with derived `TenderedAmount`, `SettledAmount` and `CreditAmount`.
  `GrandTotal` is now `Σ(Amount + ServiceCharge + VAT) + RoundOff`, which is unchanged for every
  ERP invoice. `AddLine` and `SetExport(true)` refuse a till sale, and `AddPosLine` and `Settle`
  refuse an ERP invoice.
- `InvoiceLine` gains `ServiceChargeRate` and `ServiceChargeAmount` (persisted), plus the derived
  `TaxableAmount` and `LineTotal`. `InvoiceLine.CreatePos` rounds every money figure to the paisa
  as it is made and charges VAT on the amount plus its service charge.
- `InvoiceTender`: payment mode, **frozen** kind and account, and amount (whole paisa, refused not
  rounded).
- `Domain/Pos`:
  - `PosSession`: `Open`, `RecordActivity`, `RecordCashMovement` and `Close`, with a rowversion;
  - `PosCashMovement`, `PosSessionStatus` and `PosCashMovementDirection`;
  - the `CashCount` value object, which is checked against the drawer's denominations and stored
    as `1000x2,500x1`.
- `DocumentType.PosSession`, classified in `DocumentMechanisms.NotApplicableReasons` and
  `StockBooks.NotStockMovingReasons`.

**Application**

- `Sales/Posting/InvoiceApprovalPosting`, the approve core extracted from
  `ApproveInvoiceCommandHandler`. It draws the number, approves, relieves stock and posts the sale
  entry, and the ERP Approve and the till's sale both call it.
  - `InvoicePostingRule` gains a service-charge credit and a signed rounding leg.
  - `InvoicePostingInput` takes them as **init properties**, not more trailing parameters (phase 60).
- `Sales/Posting/InvoiceTenderPostingRule`, the second entry. It debits each tender's account, nets
  the change into the drawer's line, and credits AR with what was settled.
- `Pos/`:
  - `PosAccountResolver`, which resolves location → tenant default → 409 naming the account;
  - `PosTill`, which checks that the location runs a till, its entitlement and its drawer account;
  - `PosValidationRules`;
  - `Sessions/PosSalesReader`, **the** reader; `PosSessionView`; `PosSessionAccess`, which covers
    "own session" for writes and "own, or ViewAll" for reads.
- Commands: `OpenPosSession`, `RecordPosCashMovement`, `ClosePosSession` and `CreatePosSale`.
- Queries: `GetPosSession` (the X/Z figure), `GetMyOpenPosSession` (the till's first question), and
  `GetPosDaySummary` (`ViewAll`).
- `GrantedPermissionReader.EnsureGrantedAtLocationAsync`, the pipeline's location-aware check for a
  handler re-check.
- `VoidInvoiceCommandHandler` refuses a till sale whose session is closed.
- Readers swept for the till's figures: see Decision J.

**Api.** These are under `/api/organizations/{id}/pos` (shapes in `e2e-recipes.md`):

- `GET /locations/{locationId}/sessions/mine` (204 when none)
- `POST /sessions`
- `GET /sessions/{id}`
- `POST /sessions/{id}/cash-movements`
- `POST /sessions/{id}/close`
- `POST /sales`
- `GET /day-summary?date=&locationId=`

The Sales Master `.xlsx` gains a Service Charge column, which moves its total index from 19 to 20.

**Angular**

- The invoice detail page shows a till sale's own figures: Service Charge and Round Off rows, the
  server's grand total, and a *Paid at the till* panel (session, order type, tenders, change,
  credit). Its client-side total knows nothing of either leg.
- Sales Master gains a Service Charge column.
- *Configurations > Point of Sale* states the 2023 ruling beside the service-charge switch
  (Decision A).

## 2. Scope decisions

**A. Service charge: inside the VAT base, built, and the law stated where it is switched on.**
Phase 59 §6 q1 asked two things, and both were settled from sources, not from the vendor.

- *VAT base: yes.* Under the VAT Act 2052 §13, the value of a taxable supply is the whole
  consideration charged for it. The pre-2023 practice was 13% VAT on the service-charge-inclusive
  amount (1.10 × 1.13; the vendor's 248.60 for a 200 momo).
- *Its own statutory treatment: none any more.*
  - On **2023-01-25** the Supreme Court constitutional bench (*Forum for Protection of Consumer
    Rights v. OPMCM*, Writ No. 0022) declared **Labour Act 2074 §87(3)** and **Labour Rules 2075
    r.82** void. It held that a mandatory service charge may not be added to any bill.
  - The staff-distribution scheme (70–72% to workers) went with them.
  - Separately, the Kathmandu District Court (2021-08-21, *Madan Dhungana v. Green Valley Resort*)
    and the Consumer Protection Act 2075 hold a restaurant to its menu price.
  - Sources: [Kathmandu Post, 2023-01-27](https://kathmandupost.com/money/2023/01/27/no-more-service-charge-anywhere-top-court-says),
    [Kathmandu Post, 2023-01-06](https://kathmandupost.com/money/2023/01/06/restaurants-adding-vat-on-menu-price-to-face-fines),
    [case summary](https://sushilparajuli.com/service-charges-in-restaurant-bills-in-nepal/),
    [ShareHub Nepal, 2025-05-02](https://sharehubnepal.com/news/45958-artha-sarokar-service-charge-abolishment-harms-hotel-workers-nepal)
    (the ban still in force).

That made it a product decision, so it was **asked, not assumed**. The four options were:

1. build it and warn;
2. build it silently;
3. refuse it with a 409 for now;
4. retire phase 60's setting.

The user chose **build and warn**. So:

- the charge is computed per line, inside the VAT base;
- it is credited to the location's account, then the tenant default, then a 409;
- it stays off by default;
- the settings screen states the ruling where the switch is.

The till computes what the tenant configures. It does not decide whether that is lawful for the
business, and it no longer lets the tenant switch it on without being told.

**B. Round-off is to the nearest rupee, half away from zero, outside the VAT base, and owed.**

- *Why not the vendor's rule.* Phase 60 recorded one observation (632.80 → 633) and modelled a
  flag. The same scan section records 316.40 → **317** (split 1: GL "Sales Goods (coke 60 +
  round-off 0.60)") and 293.80 → 294. That is a ceiling, and the vendor rounds every bill up. We
  round to the nearest rupee:
  - it is the neutral rule, and the only one where rounding is not a margin;
  - a ceiling adds up to 0.99 to a bill the Consumer Protection Act holds to its price.
  This is a chosen divergence. Its surface is the separate Round Off line on the invoice page now,
  and on the receipt in phase 62.
- *Owed.* The round-off is part of `GrandTotal` and of the receivable, as the vendor has it
  (INV0003 left 194 receivable out of 294). Every reader of what a customer owes includes it.
- *Not a supply.* The vendor's VAT is unchanged by rounding (72.80 on 632.80 and on 633). So the
  statutory registers (Sales Register, VAT Summary, Annex 5, Annex 13) carry the service charge and
  **not** the round-off, and their Total stays Exempt + Taxable + VAT.
- *Posted* to the location's round-off account, then the tenant's Rounding default, then a 409. It
  is a credit when it adds to the bill and a debit when it takes off.

**C. Change is always cash, always out of the drawer, and only on a bill paid in full.**
`Invoice.Settle` refuses three things:

- change larger than the cash tendered: a card is not over-swiped for cash back;
- tenders less change exceeding the bill;
- change on a bill that leaves anything on credit, which would lend customers their own change.

The tender entry nets the change into the drawer's own line. A 500 note for a 317 bill is 317 into
the drawer.

**D. A session's drawer is one GL account: the Cash-kind payment mode's account, fixed at open.**

- A location offering **no** Cash mode with an account cannot open a session ("no drawer"). Neither
  can one whose Cash modes post to **two** different accounts.
- A sale refuses a Cash tender whose mode's account is not the session's drawer account. That
  covers a mode re-pointed mid-shift.

Every cash tender, change and cash movement therefore moves one account, and the over/short posts
against the same account the count was compared with. **The opening float posts nothing.** It is a
count of cash already in the drawer's account, left there by the last close or a cash transfer.
Cash arriving from elsewhere mid-shift is a Cash In naming its source.

**E. A till sale is base currency only.** The sale command has no currency fields:

- the till's denominations, change and rounding to the rupee are all rupees;
- a foreign-currency note at a counter is a currency exchange, not a tender.

`InvoiceApprovalPosting` still folds every leg through `ExchangeRates.ToBase`, so the code path is
right if that ever changes. For a till sale the fold is the identity.

**F. Permissions: a paid sale needs Invoice Create; credit needs Approve too; the drawer is organization-wide.**

- `CreatePosSaleCommand` carries **`Sales.Invoice.Create`**, scopable to a branch by phase 32b
  through the sale's `LocationId`. A fully paid sale needs nothing more, because the session is the
  control over money at a counter and its count is the cashier's accountability.
- **Part of the bill left on credit also needs `Sales.Invoice.Approve` at that location.** This is
  re-checked in the handler (`EnsureGrantedAtLocationAsync`, the `AttachmentAccess` shape) because
  it depends on the tenders. A receivable is exactly what Approve exists to review. A default Member
  can therefore ring cash and card sales but not credit ones. That is the right default, and an
  Admin can grant Approve per branch.
- `Pos.Session.Operate` (Admin+Member) opens, moves cash in, closes, and reads **your own** session.
  `Pos.Session.ViewAll` (Admin-only) reads anyone's session and the day summary.
- **Operate is not location-scopable.** Phase 32b scopes `Module.<DocumentType>.Verb` keys over
  location-bearing documents, and a session is neither. The branch boundary sits where it matters
  instead: **opening a drawer requires `Sales.Invoice.Create` at that location**, so a cashier
  scoped to one branch cannot open a drawer, and move cash through the ledger, at another.
- Only a session's owner sells into it, moves its cash or closes it (403 otherwise). A manager
  force-close is not built (§ 5).

**G. Credit is the remainder, never the walk-in, and checked on its own size.** `CreditAmount` is
derived and is never a tender row. More than zero is refused (409) for `Contact.IsWalkInCustomer`,
which fixes vendor defect 5. For a named customer, phase 31's credit control runs on the **credit
part only**, because the tendered part never reaches the customer's balance. It uses phase 31's
422-then-override shape, with its own flag beside the stock warning's.

**H. A till sale is voidable only while its session is open.** While open, the reversal takes the
cash back out of the drawer's account, the cashier hands it back, and the session's expected cash
drops with it.

After the close, the count already held the sale's cash and the over/short was posted against a
figure that included it. A void then would rewrite a closed Z report and leave the ledger short of
cash nobody removed. It is a 409 naming the session, and the way out is a credit note in today's
session (phase 63). A void also touches the session's rowversion, so a void and a close are serial.

**I. Tenders are allocations of the invoice against itself, in every reader of what is owed.**

- `OutstandingDocumentReader` adds, per approved invoice, what its tenders settled (tendered less
  change) to `Paid`. A fully paid sale is outstanding nowhere, and a part-paid one is outstanding
  by exactly its credit, the same figure its two entries leave on AR.
- `ContactLedgerReader` emits the settlement as a credit event under the same number and date: the
  invoice paying itself, as the vendor's ledger shows it. Without it, the walk-in's balance would
  grow with every cash sale, and a named customer's credit check would count bills paid at the
  counter.
- `GetDefaultPaymentAllocations` suggests `CreditAmount`, not `GrandTotal`. They are the same
  figure for an ERP invoice.

Tested on Invoice Age, the Contact Statement and the GL together. The tests were proven to bite by
disabling the tender settlement, which made two fail; the file was then restored by hash and
`touch`ed.

**J. The sweep: what changed, and what deliberately did not.**

Changed so service charge and/or round-off are counted:

- totals: `OutstandingDocumentReader`, `ContactLedgerReader`, `GetDefaultPaymentAllocations`,
  `DailyTransactionSummaryContentBuilder`, `RecentTransactions`, `TransactionList`,
  `EmailMergeValueReader`, `GetInvoice` and the print pipeline (Service Charge and Round Off summary
  lines);
- taxable bases, with service charge only: VAT Summary, Sales Register, Annex 5 and Annex 13;
- Sales Master, as a column (Service Charge, with Total = Net + Service Charge + VAT).

Unchanged, on purpose: the four **product-level** readers (`TradeLineReader`, Inventory Master,
Product Profitability, Ratio Analysis). They report a product's revenue, and a service charge is
income of its own account, not the momo's. Their VAT figure does include the VAT on the line's
service charge, because that is the VAT the line was charged.

The full-tenant export is **left open** (§ 5).

**K. One reader for every till figure.** `PosSalesReader.ForSessionAsync` and `ForDayAsync` differ
only in which approved POS invoices they select, and share every line of arithmetic.

- Expected cash is `OpeningFloat + Σ cash movements + (cash tendered − change)`. It is one formula,
  used by the live view and by the close.
- `The_day_total_equals_the_session_total` reads both and compares all eleven figures and the
  tender list, across cash, card, split, credit and voided sales. That fixes defect 7.
- The E2E's X report and day summary agree on all eleven too.

**L. Every till line is whole paisa when it is made.** An ERP line keeps four decimals and is
settled later by a payment of any amount. A till sale is settled on the spot. If its lines summed to
632.8045, a 632.80 tender would leave 0.0045 on the walk-in's credit, which Decision G refuses.
Rounding per line also makes a printed bill add up line by line.

**M. Smaller decisions.**

- *Available For Sale* is enforced at the till (409 naming the products). Phase 60 Decision E
  found it had been read by nothing since phase 3.
- Session codes are `SES0001`, from the tenant's counter. There is no numbering screen, so no
  configuration leaks.
- The session commands write **no audit row**. `AuditBehavior` records Create/Update/Approve/Void
  verbs only, so the marker would have been present-and-ignored (§ 3). The session row and each
  movement's `CreatedByUserId`/`CreatedAt` are the record.
- **Closing a session needs no POS entitlement.** It is phase 60's switching-off rule: a tenant that
  loses the plan mid-shift can still count and close its cash. Every other POS request is gated.
- A session row is touched by every sale and void and carries a rowversion, so a sale and a
  concurrent close cannot both commit.
- A sale's date is the Nepal date at the moment of sale. It is not bindable from a request body.
  The cash movement and the close stamp theirs the same way, and all three are lock-date sensitive.
- Warehouse: the request's value, then the location's default warehouse, then a 409.
- The day summary is `ViewAll`. A session list is not built; that is phase 66's report surface.

## 3. Bugs found and fixed

1. **The invoice's GL panel listed entries in arbitrary order** (pre-existing for any document with
   two entries, such as phase 37's cost catch-up). It showed a till sale's settlement above the sale
   itself. `GetInvoiceQuery` now orders entries by `PostedAt`. Seen in the browser pass.
2. *(This phase's own, caught before shipping.)* `IAuditableRequest` on the three session commands
   would have been present-and-ignored. `AuditBehavior.ResolveAction` matches only the
   Create/Update/Approve/Void/Extract prefixes, so Open, Record and Close write nothing. The marker
   was removed and `DocumentType.PosSession`'s comment no longer claims an audit feed.

## 4. Evidence

**Rule research** (Decision A): four sources, linked above.

**Migration** (`dotnet ef database update`, dev DB): applied. The scaffold was reviewed by hand and
contains:

- additive `Invoices` columns: `Channel` default `'Erp'`, `RoundOff`/`ChangeAmount` default 0,
  `OrderType`/`PosSessionId` nullable;
- additive `InvoiceLines` columns: `ServiceChargeRate`/`ServiceChargeAmount` default 0;
- three tables;
- the filtered unique index `IX_PosSessions_OrganizationId_BillingLocationId_UserId WHERE [Status] = 'Open'`;
- four `RolePermissions` rows (`Pos.Session.Operate` Admin 1 / Member 1, `Pos.Session.ViewAll`
  Admin 1 / Member 0).

**E2E** on a fresh organization, *P61 Momo Till 085635* (POS Retail). Master data was seeded through
the API. Every status code was printed and matched, except one expectation of the script's own
(`PUT /billing-locations` answers 204).

| Area | Checked | Result |
|---|---|---|
| Session | `GET .../sessions/mine` before opening | **204** |
| | open by amount where cash verification is on | **400** |
| | open with `500 × 2` | 200, `SES0001`, float 1000 |
| | second session at the same till | **409** |
| Sales | 2 momo + 2 coke, cash 1000, change 367 | 200: 633 (SC 40, round-off +0.20) |
| | momo + coke, card 16 + cash 300 | 200: 316 (round-off **−0.40**) |
| | credit on the walk-in | **409** |
| | named customer, card 100 | 200: 249, **credit 149** |
| | card 300 with change 51 | **400** |
| | coke cash 68, then Void (session open) | 200, 200 |
| Drawer | Cash Out 100 to *Vegetables* | 200, expected cash 1833 |
| | close counted 1824 without a note | **400** ("short by 9.00") |
| | close with a note | 200: expected 1833, counted 1824, difference −9 |
| | Void a sale of the closed session | **409** |
| | sale into the closed session | **409** |
| One reader | X report vs day summary | **all 11 figures equal**, tenders equal (Cash 1300, Card 116) |
| 403-not-404 | `GET`/`close` a nonexistent session as Admin | **404** "Session not found." |
| | same user moved onto a role granting only `Tenancy.BillingLocation.View`: `GET /billing-locations` | **200** |
| | same user, same nonexistent id: `GET` and `close` | **403 naming `Pos.Session.Operate`** |
| | same user: day summary | **403 naming `Pos.Session.ViewAll`** |
| | own role restored | via `sqlcmd` (phase 52's recipe) |

**SQL agrees with the API:**

```
Code  Channel OrderType Status   Session RoundOff Change   GrandTotal Tendered
0001  Pos     Retail    Approved SES0001  .20     367.00   633.00     1000.00
0002  Pos     Retail    Approved SES0001 -.40       .00    316.00      316.00
0003  Pos     Retail    Approved SES0001  .40       .00    249.00      100.00
0004  Pos     Retail    Void     SES0001  .20       .00     68.00       68.00

entries per invoice: 0001 713/713 + 633/633; 0002 356.40/356.40 + 316/316;
0003 249/249 + 100/100; 0004 108/108 + 68/68 + 108/108 (reversal) -- all balanced
0001 sale entry: Dr AR 633, COGS 80 | Cr Sales 520, SC Income 40, VAT 72.80, Rounding 0.20, Inventory 80
0001 tender entry: Dr Cash In Hand 633 | Cr AR 633
voided 0004, net per account across its 3 entries: AR 0, Cash 0, COGS 0, Inventory 0, Rounding 0, Sales 0, VAT 0
SES0001 entries (at the till's location): Dr Vegetables 100 / Cr Cash 100;  Dr Cash Over Short 9 / Cr Cash 9
session row: Closed, float 1000 (500x2), expected 1833, counted 1824 (1000x1,500x1,100x3,20x1,2x2), -9, "Coins short"
balances: AR 149 | Cash In Hand 824 (= 1824 counted - 1000 float) | Nabil Bank 116 | Cash Over Short 9
          SC Income -80 | Rounding -0.20 | VAT -137.80 | Sales -980 | COGS 120 | Inventory 680
trial balance: Dr 3560.40 = Cr 3560.40
three-view law: layers 680.00 = movements 680.00 = GL Inventory 680.00
```

**Sales Master through the API**: momo lines carry `serviceCharge` 40 and 20, and each row's total
is Net + SC + VAT. The report total of 1,197.80 is the lines'. The three bills' round-offs
(+0.20 − 0.40 + 0.40) are in no line, as Decision B intends.

**Browser pass** (erp-web-ssl, cookie transplant):

- invoice 0003 shows Service Charge 20.00, Taxable 220.00, VAT 28.60, Round Off 0.40, Grand
  Total 249.00, and *Paid at the till: Session SES0001 · Retail, Card 100.00, Left on credit 149.00*;
- the POS settings page shows the ruling under the switch, wired by `aria-describedby`;
- no console errors.

**Tests that read the money:**

- `PosSaleEngineTests`, 23: both entries balanced; nearest-rupee rounding; account fallback and its
  409; change rules; walk-in credit refusal; every receivable reader agreeing with the GL; Approve
  needed for credit; void reversing both; void refused after close; cash out and close posting;
  ownership and ViewAll; **day total = session total**; open refusals; Available For Sale; drawer
  account; detail DTO;
- `InvoiceTenderPostingRuleTests`, 3;
- `PosSaleTests` (Domain), 20, on phase 59's numbers.

The three-view law is asserted after sale and after void (`StockConservation`).

## 5. Left open, and for later phases

- **An ERP credit note against a till sale** (*Convert to Credit Note* on the invoice page) returns
  goods and their VAT, not the service charge. Its lines carry none. Returns at the till are
  **phase 63**, which owns this.
- **The full-tenant export's Sales Documents category** is line-level `Amount`/`VAT`. It has no
  service-charge column and no round-off. Adding a column changes a shared row shape used by the
  purchase categories too, so it is recorded rather than half-done.
- **Phase 59 §6 q2 and q3 are still open**: the abbreviated-tax-invoice conditions and what a reprint
  must print. Phase 62's receipt depends on both.
- The vendor's `round_amount` setting is still unread semantically (a flag or an increment). The
  ceiling evidence above only covers its default. The live tenant can settle it if it ever matters.
- **No session list and no force-close.** An abandoned session blocks only its owner's next session
  at that till. Phase 66's reports are the natural home for a list. A manager force-close needs its
  own rule about whose count it is.
- A session that spans midnight puts its sales on two days. That is correct for the day report, and
  the session total then equals the sum of two day totals, not one.
- An **expired** tenant cannot close a session, because `SubscriptionExpiryBehavior` gates every
  `ILockDateSensitive` command. That is consistent with "documents read-only", and noted because a
  drawer is not a document.
- Ledger drill-down from a `PosSession` GL row has nowhere to go until the till has a session page
  (phase 62).
- Phase 65's split must move quantities and never rates. The persisted `ServiceChargeRate` per line
  is what lets it.
