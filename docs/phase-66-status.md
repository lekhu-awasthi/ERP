# Phase 66 — POS reports and dashboard: one reader, and the line between a till figure and the register

## TL;DR

**The POS now has its reports and a dashboard, and every figure on them agrees with every other and with
the Sales Register.** The vendor printed three numbers for one day: 611 on its dashboard, 610.20 on its
Day Report and product reports, and 542.40 on its Sales Summary. Ours are read from two readers:
`PosSalesReader` (phase 61 K) for the tills' money and `TradeLineReader` for a product's. Every screen
prints the one line that separates a till figure from the register, the round-off. An E2E on a fresh
organization checked 36 figures against SQL, recomputed independently, and all 36 agree.

| | |
|---|---|
| Live read taken | **yes**, 2026-10-02, *Hamro Samaan* trial, **read-only** (`erp-module-scan.md`, "POS reports and dashboard, read live"). The user authorised writes; the auto-mode classifier refused the first one (a session to ring up and void a sale), so the vendor's treatment of a **void** was not observed |
| Migration | `Phase66PosReports`, scaffolded and **not** hand-edited: two seeded permission rows, nothing else |
| New endpoints | 9: the Day Report and the dashboard, the sessions list, the Order Report, the Payment Summary (four with an `.xlsx` export). Phase 61's `/pos/day-summary` became the Day Report. Shapes are in `e2e-recipes.md` |
| Changed endpoints | 12 take a `channel`: Sales by Item and by Customer, Sales Summary, Sales Master, Sales Register and System Audit, each with its export |
| New permission key | **1**: `Reports.PosPaymentSummary.View` (Admin-only). The Day Report, dashboard and sessions list ride `Pos.Session.ViewAll`, and the Order Report rides `Pos.Order.View` |
| New screens | POS Day Report, POS Payment Summary and POS Order Report (Reports > *Point of Sale*), POS Sessions (Sales), and the overview on the POS launcher. All are lazy chunks |
| Initial bundle | **652.58 kB** (+2.52 kB: route entries, nav-catalogue titles, one CSS rule) against 680 kB |

**Four things worth carrying forward.**

1. **The round-off is the one line between a till figure and the register.** The Sales Register lists
   supplies, and a round-off is not a supply (phase 61). So the register's total for the tills' documents
   is net sales **less the net round-off**, exactly. The vendor's reports omit the round-off and so disagree
   with its own dashboard. Ours print it as a line on the Day Report, the products panel and the export, so
   each figure reconciles on the page.
2. **A dashboard should be the reports, not a sixth aggregation.** The overview reads `PosSalesReader`
   (tiles, payments, series) and `TradeLineReader` with Channel = POS (products), the readers its reports
   read, and a test reads both sides. The vendor's "Other Products" row was total − top, clamped at zero. Ours
   computes nothing in the browser, and its panels end with the rows that make them add up (round-off;
   change and credit).
3. **The vendor's POS reports are its ERP reports read with `channel=POS`, so ours are too.** Product and
   Customer Sales, Sales Master, Sales Summary, the Sales Register and System Audit ("POS activity") gained a
   Channel filter rather than POS copies. Only the Day Report, the Payment Summary and the Order Report are
   new. Adding the filter exposed two reconciliation faults in the ERP reports (§ 3, items 1 and 3).
4. **A series must be cut from the same rows as the figure it illustrates.** The chart's points are the very
   per-document rows the summary added up, grouped by the document's own `Date` (the Nepal business day). On
   one day they are labelled by the Nepal hour of approval, a label only. So the bars always sum to the tile
   (phase 57's rule), and are drawn as bars from zero, where the vendor's spline drew a 611 day peaking near 400.

Tests: Domain 874, Application.UnitTests **1547** (+18), Infrastructure.UnitTests 13, Api.IntegrationTests 30,
Angular **750** (+23). `dotnet build`, all four .NET suites (Docker up, run one at a time), `ng build` and
`ng test` are green.

---

## 1. What shipped

**Application**

- `PosSalesReader.ForPeriodAsync` (the session, the day and now any period share one fetch and one piece of
  arithmetic), the series (`PosSalesPointDto`, by hour or by day), `Taxable`/`NonTaxable` on the sales and
  refund summaries (split by the Sales Register's own rule), and `NetRoundOff`, `NetCash` and `NetCredit`.
  `PosSalesReader.Payments` builds the payments breakdown.
- `GetPosDayReportQuery` (replacing phase 61's `GetPosDaySummaryQuery`), `GetPosDashboardQuery`,
  `ListPosSessionsQuery` (searchable), `PosOrderReportQuery` and `PosPaymentSummaryQuery`.
- `TradeLineReader`: service charge as a signed measure, and a sales channel. By Item, By Customer and Sales
  Summary gained a Service Charge column, so Total is net + service charge + VAT. Sales Master gained the
  channel, Order Type, Cashier and Payment Modes, and Sales / Returns / Net totals. The Sales Register gained the
  channel on both halves through `SalesReturnReader`. System Audit gained the channel, and its date bounds
  are now Nepal days.

**Web**

- Three report screens, the sessions list, the launcher's overview (`@defer (on viewport)`), and the shared
  `app-pos-period-filter` and `app-sales-channel-filter`. The six ERP report screens gained the channel filter
  and their new columns. A *Point of Sale* category was added to the Reports index.

## 2. Scope decisions

**A. The live read, and what it settled.** Read on 2026-10-02 from the *Hamro Samaan* trial
(`erp-module-scan.md`, "POS reports and dashboard, read live"). **Read-only**: the user authorised writes and
the auto-mode classifier refused the first one (opening a session to ring up and void one sale), so the
vendor's treatment of a **void** in its reports was not observed and is decided here on our own rule (C).

- **The vendor's POS reports are its ERP reports read through `channel=POS`.** The report ids are the
  ERP's (`report-sales-master`, `report-sales-by-item`, ...); only three are POS-only: the Day Report, the
  Payment Summary and the Order Report.
- **It shows several different figures for one day's sales.** On 28-09:
  - the dashboard's Total Sales is 611 (rounded, refund not deducted);
  - the Day Report, Product Sales and Customer Sales say 610.20 (there is no round-off anywhere);
  - Sales Summary says 542.40 (net of the refund, whatever its *Include Credit Note* toggle says);
  - Payments show Cash 349 on the Day Report (net of the refund) against the dashboard's 417 (gross).

  The chart is a `basis` spline that draws the 611 day peaking near 400. This is phase 59's defect 7, now on
  one screen.

**B. One rule for every figure: the tills' money comes from `PosSalesReader`, a product's from
`TradeLineReader`, and nothing else adds anything up.** Phase 61 Decision K made the session and the day one
reader. This phase extends the same reader to a period and a series. The identities below are each pinned by
a test that reads both sides (`PosReportsTests`), and each was checked against SQL in the E2E:

- Day Report = the reader over the period; the retail drawer's Z + the restaurant drawer's X = the day.
- **Sales Register total = net sales − net round-off.** The register lists supplies and a round-off is not
  one, so the two differ by exactly that line, which every POS screen prints. The test's numbers are chosen
  so the round-off does not cancel (−0.20 net).
- Sales by Item, Sales by Customer, Sales Summary and Sales Master (net) with *Channel: POS* = the register,
  service charge in its own column (D).
- The Payment Summary's rows sum, per type, to the reader's cash and credit, and in all to net sales.
- The dashboard's products + other + round-off = its Net Sales tile; its payments panel = the same; its
  chart sums to it.

**C. Refunds and voids, in each.** A refund is a credit note (phase 63). Every money figure here is **net of
refunds**, and every screen also shows refunds on their own line, so nothing is netted silently (the vendor
nets in some reports and not others). A **void takes a document out of every figure**, as if it had never
happened (phase 61 Decision K). The Day Report counts voided sales, refunds and orders on their own lines,
so a void is visible without being summed. Split bills are ordinary sales (each part an invoice). The Order
Report lists every bill of an order, voided ones marked. Service charge is a column everywhere. The round-off
is a line on every POS screen and never in a register (B).

**D. Service charge joins `TradeLineReader` as its own measure.** Phase 61 Decision J left product-level
readers without it: a service charge is income of its own account, not the momo's. That still holds, because
`NetAmount` stays the product's revenue. But the VAT figure already carried the VAT *on* the service charge, so
every total there (net + VAT) was neither the product's revenue nor the bill. A till sale's Sales Summary row
read the bill less its service charge. The vendor's Product and Customer Sales both print the column. So each
fact carries its line's service charge, and Total = net + service charge + VAT, which is the register's total.
The monthly crosstabs measure `NetAmount` and are unchanged. Sales Summary counts the service charge inside
the taxable bucket of its line, as the register does. Phase 26b's note that the column was "driven by a flag
this codebase does not model" had been stale since phase 60, and was removed.

**E. The Order Report is a report over `PosOrder`, not over Sales Orders.** The vendor's Order Report lists
its open tabs, which are Sales Orders, including a parked Retail cart (SO0004, 293.80, which its dashboard
counts as a "pending order"). Ours lists `PosOrder`s: open, settled and voided (phase 59 Decision E; holds are
browser-side). Each row shows:

- **order value**: the estimate at frozen rates, unrounded;
- **billed**: what its bills not voided came to, each rounded;
- **still to bill**: value less what the bills charged before rounding, which is exact because the last part
  of a line takes what is left (phase 65 E);
- every bill, voided ones marked.

The footer covers the whole filter: counts per state, billed (summed in SQL), and still to bill over the open
orders. It rides the POS Orders list's key and scope; the list stays the working view.

**F. The calendar.** The day is the document's `Date`, the till's Nepal business day (phase 61), on every POS
report. The dashboard and Day Report offer Today, Last 7 and Last 30 days (the vendor's presets) or any range up
to 366 days. The vendor's Day Report also takes a from/to **time**, so a shift can cross midnight. Ours does
not: a drawer that spans midnight puts its sales on two days, as phase 61 § 5 decided, and **the session's Z
report is the shift view**. The sessions list files a drawer under the day it opened.

**G. Permissions (derived per report, not defaulted).**

- **Day Report, dashboard, sessions list: `Pos.Session.ViewAll`** (Admin-only, existing). Phase 59 J and phase
  61 derived it for "the day report": these figures sum every cashier's drawer and say whose drawer was short.
  The launcher shows the overview only to a holder. A cashier without the key gets no overview at all, rather
  than a panel of refusals on the screen they start a shift from.
- **Payment Summary: `Reports.PosPaymentSummary.View`** (new, Admin-only). It is a flat per-transaction
  register naming the customer, the Sales Master's bar (phase 8b). It has its own key rather than `ViewAll`
  so an Admin can give an accountant the payments without the drawers. The E2E shows exactly that: a role
  holding only this key reads the Payment Summary and is refused the Day Report and the dashboard. Being a
  `Reports.` key, it takes the location filter and the report location scope (phase 35b).
- **Order Report: `Pos.Order.View`** (Admin+Member, existing). These are the POS Orders list's orders, read
  for totals, scoped to where the caller may view invoices.
- **The ERP reports keep their own keys**; POS activity is System Audit (`Reports.SystemAudit.View`).

**H. POS activity is System Audit with Channel = Point of Sale, plus the sessions list.** The audit trail
already records every till document action: sale, refund and order Create, the order's Update and Void, the
discards, and a sale's Void. The channel filter keeps rows whose document is a till sale or refund (read by
key, an `EXISTS` per row) or a restaurant order. A drawer's own events (open, cash in and out, close) write no
audit row (phase 61 M); the POS Sessions list and each session's page are their record. The audit page's
document-type filter gained *PosOrder*, and its row degrades to plain text (the order's page is the till's
working screen).

**I. Smaller decisions.**

- **The Payment Summary lists change as its own row** (negative, out of the sale's cash mode). The vendor
  nets it into the cash row (317 for 500 handed over), but the drawer saw both. Credit left on an account and
  credit taken back by a refund are rows of type *Credit*, whose account is the customer.
- **No Discount line on the Day Report.** On the till, "gross less charged" picks up the rounding residue of
  split parts and would read as a discount. Discount stays in Sales by Item, Customer and Summary.
- **The chart** is CSS bars from zero, negative hours below the line, aria-hidden, with a visually-hidden table
  of the series (phase 57's pattern).
- **Exports**: the Day Report as a "Particulars | Amount" sheet ending with the Sales Register line; the other
  three as tables with their footers; times on the Nepal clock.
- **Mark as Take Away and item transfer** (phase 65 § 5) were not scheduled here. The repricing question
  (does a parcelled dish lose its service charge, against "rates never move") is the user's, and was asked
  at the close of this phase.

## 3. Bugs found and fixed

1. **The ERP's Sales Summary, Sales by Item and Sales by Customer did not total a till sale** (pre-existing
   since phase 61): their Total was net + VAT, where the VAT included the VAT on a service charge that was not
   there. Found by the reconciliation test against the register. Fixed by D.
2. **Sales Master's footer added returns in as sales.** A credit note is listed positive with its type, and
   the footer summed every row. It now shows Sales, Returns and Net (the register's figure). `TotalAmount` is
   kept for earlier callers.
3. **The Sales Register could not be read for the tills alone**: an ERP invoice at the same head office
   shares the location, so a location filter cannot separate them. Found by the first run of the agreement
   test (1514.20 against 949.20). The register gained the channel on both halves (phase 44's rule).
4. **System Audit cut its days at UTC midnight**, so an action between 00:00 and 05:45 Nepal time was filed
   under the previous day. Bounds are now Nepal midnights (phase 48), pinned by a test at 00:30.
5. **Every responsive table with a hidden caption grew a 1px vertical scrollbar** (7 screens, phase 34a's
   captions). Bootstrap's `.visually-hidden` overhangs `overflow: auto` by its −1px margin; measured
   scrollHeight 198 against clientHeight 197. Fixed with one rule in `styles.scss`.
6. System Audit's type filter did not offer *PosOrder*, audited since phase 64.

## 4. Evidence

**E2E** on a fresh organization (*P66 Reports 223536*), seeded through the API. It has a Retail till at head
office and a Restaurant till at Thamel:

- **Retail**: 203 (cash 500, change 297); 316 (card 116 + cash 200; service charge on the momo, phase 64 H:
  Retail charges it); 497 to a named customer (card 100, 397 on credit); 68 voided; a 68 refund paid in cash;
  the drawer closed five short.
- **Restaurant**: table T1 billed by item, 316 then 317 (phase 65's running total); T2 left open (248.60 to
  bill); a take-away voided; the drawer left open.

Then `e2e66_verify.py` read every report through the API and recomputed each figure in SQL with no
application code between. **36 of 36 agree**, including:

| Figure | API | SQL |
|---|---:|---:|
| Day Report: sales / refunds / net | 1649.00 / 68.00 / 1581.00 | 1649.00 / 68.00 / 1581.00 |
| Day Report: net round-off; register line | −1.00; 1582.00 | −1.00; 1582.00 |
| Sales Register (POS) total | 1582.00 | 1582.00 |
| Sales by Item / Customer / Summary / Master (POS) | 1582.00 each | 1582.00 |
| Payment Summary: total; cash; credit | 1581.00; 968.00; 397.00 | 1581.00; 968.00; 397.00 |
| Retail Z + restaurant X | 1581.00 | = Day Report |
| Order Report billed | 633.00 | 633.00 (= Thamel's sales) |
| Dashboard: tile; products + round-off; payments; chart | 1581.00 each | 1581.00 |
| GL Rounding account (credit − debit) | −1.00 | net round-off |

Two earlier seed runs were abandoned: my script's tenders assumed no service charge at Retail, and one
command ran the flow twice. Those organizations (`08554198…`, `ffac6126…`) are not the evidence.

**403-not-404** (`e2e66_perm.py`, a throwaway user):

- `GET /reports/pos-payment-summary?…&locationId=<nonexistent>` is **404** "Billing location not found." as
  Admin, and **403 naming `Reports.PosPaymentSummary.View`** for an Accountant holding
  `Reports.SalesRegister.View`. The same user gets **200** on the Sales Register.
- Moved to a role holding only the new key: the Payment Summary is **200**, and the Day Report and dashboard
  are **403 naming `Pos.Session.ViewAll`**.

**Browser** (the Browser pane, the same organization):

- The overview's panels add up on screen: 1,243.00 + 339.00 − 1.00 = 1,581.00, and 1,265.00 + 216.00 − 297.00
  + 397.00 = 1,581.00.
- The Day Report, Payment Summary, Order Report and sessions list each match the table above.
- Sales by Item opened from the dashboard's link arrives with *Point of Sale* selected and totals 1,582.00.
- The Reports index has a *Point of Sale* heading.
- No console errors. The four new exports open as workbooks with the screen's rows.

**Keyboard census** (focus recorded on real Tab presses, compared with a census of the page's focusable
controls). Every control is reached in visual order on each screen:

| Screen | Controls reached |
|---|---|
| Day Report | 11 of 11 |
| Payment Summary | 20 of 20 |
| Order Report | 13 of 13 |
| POS Sessions | 14 of 14 |
| Launcher with overview | 18 of 18 |

Each wraps to the shell's skip link, with the one solid `#0a58ca` ring. No pointer-only targets. The chart is
aria-hidden and its 24-row table present. No screen reader was run (phase 40's honest limit).

## 5. Left open, and for later phases

- **Mark as Take Away and item transfer** wait on the user's answer to the repricing question (I). Also
  carried: discount and credit on a restaurant bill, per-part customers (phase 65 § 5).
- **The ERP invoice and credit-note PDFs** still title every invoice "Invoice" and neither count nor mark a
  reprint (carried since phase 62). Phase 63 put this "with phase 66's print and report surface or a phase of
  its own". It was **not** done here, and needs a phase of its own: making a GET print write print rows is a
  decision shared with the email pipeline.
- **A from/to time on the Day Report** (F): if a restaurant asks for a shift that crosses midnight as one
  report, it is a reader change (select by approval instant), not a screen change.
- **The vendor's Delivery Partner Statement**: delivery partners are outside the sequence.
- Carried unchanged: phase 61 § 5's export columns, phase 62 § 5, phase 63 § 5.

## Writes made to the vendor tenant

None. One write (starting a session at *POS Retail*) was attempted after the user authorised writes and was
refused by the auto-mode classifier.
