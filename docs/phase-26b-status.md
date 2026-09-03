# Phase 26b status — Report catalog completion, Receivable/Payable and analytics

**TL;DR.** The Receivable, Payable, Sales Report and Purchase Report catalogue groups are complete:
**Customer Receivable Summary**, **Supplier Payable Summary**, **Invoice Age**, **Purchase Bill Age**
(closing FR-9.2), and **Sales/Purchase By Customer/Supplier**, **By Item**, their four **Monthly**
variants and the **Sales Summary Report** (closing FR-9.3). Thirteen reports, **seven handlers** --
each mirrored pair is one handler discriminated by a side the route hardcodes, following phase-9's
`ContactType` precedent. Nothing new is stored; the only migration is twenty-six permission-seed rows.

All ten previously-ungenerated screens were **read live** on the Moonbeam UAT tenant on 2026-09-03
before any DTO was designed. Three findings changed the plan: **age runs from the Due Date, not the
document date**; **a contact-tagged Journal Voucher really is an ageable document** (its Txn Type
filter names it); and **all four Monthly variants are keyed by a BS fiscal year, not a date range** --
so the roadmap's prediction that the server-side BS calendar would have one consumer was wrong, and
it has five.

`Domain/Common/BsCalendar` is that calendar: a verbatim port of phase-23's client converter,
including its month-length table, with `BsCalendarTests` re-pinning the same live-confirmed anchors
and round-tripping all **33,969** days of BS 2000-2092. Phase 27b consumes it next for BS dates in
PDFs and `.xlsx`.

**Thirteen new permission keys, eight Admin-only and five Admin+Member**, split on one line worth
naming: eight of these reports put a named contact next to a money figure, and five do not.

Two things were deliberately **not** built: the live Txn Type filter's Quick Payment/Receipt option
(phase-17 made that a `Payment`, not a document type -- there is nothing to age), and Sales Summary's
**Service Charge** column (a product-level flag this codebase does not model; omitted with a note
rather than zero-filled, as the roadmap directed).

Verified end to end against the real API and database: a fresh Organization seeded by curl with an
overdue and a current document on each side plus a contact-tagged JV and a contact opening balance;
all thirteen reports pulled; all thirteen `.xlsx` exports returning real workbooks; all thirteen
negative paths returning **403 naming their exact key**; and `sqlcmd` re-deriving every Invoice Age
figure from the raw document tables independently of the handler. Invoice Age's total balance
(7,510) equals Customer Receivable Summary's closing balance exactly -- the consistency the shared
`ContactLedgerReader` was chosen to guarantee.

Tests: Domain **311** (+62), Application.UnitTests **629** (+31), Api.IntegrationTests **18**
(unchanged), Angular **135** (+7). `dotnet build` / `dotnet test` / `ng build` / `ng test` /
`tsc --noEmit` all clean.

---

## Confirm-live pass (Moonbeam UAT tenant, 2026-09-03, read-only)

Ten report screens were opened and GENERATE clicked **before any DTO was designed**, per the
phase-8f rule. Eight of them had never been generated (`erp-module-scan.md`'s 2026-09-02 pass listed
them without running them); the two that had — Customer Receivable Summary and Invoice Age — were
re-read to confirm their supplier mirrors and to settle the footer-total question. Nothing was
saved; every screen is a read.

URL slugs confirmed: `customer-receivable`, `supplier-payable`, `invoice-ageing`,
`purchase-bill-ageing`, `sales-customer`, `sales-item`, `sales-customer-summary`,
`sales-item-summary`, `purchase-supplier`, `purchase-item`, `purchase-supplier-summary`,
`purchase-item-summary`, `sales-summary`. (`purchase-bill-ageing` was the one slug the 2026-09-02
list did not record; it follows `invoice-ageing`'s pattern.)

### Customer Receivable Summary / Supplier Payable Summary — an exact mirror pair

| | |
|---|---|
| **Filters** | Period (date range), Contact Group |
| **Subtitle** | `For the period 17-07-2026 to 03-09-2026` |
| **Columns** | Customer *(resp. Supplier)*, Contact Group, Closing Balance |
| **Footer** | a **Total** row over Closing Balance |

Both are one row per contact with a non-zero balance; Contact Group renders `-` when the contact is
in none. The pair is structurally identical, which is the phase-9 result again (Supplier Ageing
Summary / Supplier Statement mirrored Customer's confirmed shape exactly) — a third data point for
the "mirror the confirmed side" bet.

**One live inconsistency, deliberately not reproduced.** The two screens disagree with each other
on how a credit balance is rendered: Customer Receivable Summary prints `(5,000,000)` with thousands
separators, Supplier Payable Summary prints `-560000` raw — while *both* footer Totals use
parentheses. That is a defect in the reference product's own formatting, not a shape to copy. This
codebase carries the signed decimal plus `GlBalanceMarker`'s DR/CR marker (phase-26a) and renders
parentheses consistently on both.

### Invoice Age / Purchase Bill Age

| | Invoice Age | Purchase Bill Age |
|---|---|---|
| **Filters** | Period, Customer, Txn Type | Period, Txn Type, Supplier |
| **First column** | `Invoice Date` | `Date` |
| **Amount column** | `Invoice Amount` | `Amount` |
| **Txn Type options** | Opening Balance, Invoice, Journal Voucher, Quick Payment | Opening Balance, Purchase Bill, Expense, Journal Voucher, Quick Receipt |

Columns otherwise identical: *Date, Due Date, #No, Reference No, Customer/Supplier, Contact Group,
Amount, Paid, Balance, Status, Age Days*, plus a footer **Total** over Amount / Paid / Balance (the
Paid total printed `-` rather than `0`). The contact cell carries the code in parentheses —
`Ankit (0089)`. Status read `Overdue` on every row in range; `Age Days` renders `40 day(s)`.

**Three findings that decided the design:**

1. **Age runs from the Due Date, not the document date.** `INV0130/82-83` dated 25-06-2026, due
   25-07-2026, printed `40 day(s)` against a report end date of 03-09-2026 — which is
   03-09 − 25-07 exactly, not 03-09 − 25-06. `INV0196/82-83` (dated 17-07-**2025**, due 17-08-2026,
   `17 day(s)`) confirms it independently across a year boundary. This is the one place Invoice Age
   differs from Phase 9's Ageing Summary, which buckets from each bill's own Date because no
   Contact or document in this codebase carries a credit term.
2. **The period's From date does not filter.** Rows dated 25-06-2026, 03-06-2026 and 17-07-**2025**
   all appeared under a stated period of 17-07-2026 → 03-09-2026. Only the **To** date acts, as the
   as-of date — the same "age *all* historical unpaid bills as of one date" semantics
   `ContactAgeingSummaryQuery`'s `AsOfDate` already documents.
3. **A Journal Voucher posted to a contact really is an ageable document**, and the Txn Type list
   is the enumeration of what else is: an opening balance, the trade document, and a quick
   payment/receipt. Rows `JV0018/83-84`, `JV0025/83-84`, `JV0027/83-84`, `JV0029/83-84` carry
   amounts and ages like any invoice, with Due Date = document date (no credit term).

### Sales By Customer / Purchase By Supplier

| | |
|---|---|
| **Filters** | Period, Contact Group |
| **Columns** | Contact *(resp. Supplier)*, Contact Group, Amount, Discount, **Net Sales** / **Net Purchase**, Vat Amount, Total Amount |
| **Footer** | **Total** over all five money columns |

The contact cell here carries **no code**, unlike Invoice Age's. Zero renders `-`.

### Sales By Item / Purchase By Item

| | Sales By Item | Purchase By Item |
|---|---|---|
| **Filters** | Period, **Filter By item/category**, Product Category, Product | Period, Product Category, Product |
| **Columns** | Product *(name + code)*, Quantity, Amount, Discount, **Net Sales** / **Net Purchase**, Vat Amount, Total Amount |

Sales By Item carries a grouping-mode control the purchase side does not: **Filter By item/category**
offers exactly `Item` and `Category`, i.e. whether each row is a product or a product category.

**The footer Total row totals the five money columns and leaves Quantity blank** — six cells against
a seven-column header. That is phase-26a's "refuse to total a column whose values are not the same
unit of account" rule, arrived at independently by the reference product: quantities across
products with different units of measure do not add up.

### The four Monthly variants — a BS fiscal-year crosstab

**None of them takes a date range.** Every one is keyed by a **BS fiscal-year picker** reading
`2083 - 2084`, exactly like Sales Summary Report. This is the finding that most changed the phase's
shape: the roadmap predicted the server-side BS calendar would have one consumer, and it has five.

Column set, identical on all four:

> *row label* … then **Shrawan 2083, Bhadra 2083, Asoj 2083, `1st Quarter`, Kartik 2083, Mangsir
> 2083, Poush 2083, `2nd Quarter`, Magh 2083, Falgun 2083, Chaitra 2083, `3rd Quarter`, Baisakh
> 2084, Jestha 2084, Ashad 2084, `4th Quarter`, Total`*

— twelve BS months in **fiscal** order (Shrawan first, Asar last), a running **quarter subtotal
after every third month**, and a row Total. The fiscal year spans two BS years and the column
headers say so. Values are **Net Sales / Net Purchase**, not Total Amount: `Adhitya Bhandari` reads
45,000 in the crosstab and 45,000 in Sales By Customer's Net Sales column, against 50,850 Total
Amount.

Their row-label columns differ from each other, and the differences look like accidents rather than
design:

| Report | Extra filter | Row label | Code shown | PAN column |
|---|---|---|---|---|
| Sales By Customer (Monthly) | Contact Group | Customer | yes | **`PAN_no`** |
| Purchase By Supplier (Monthly) | Contact Group | Supplier | no | no |
| Sales By Item (Monthly) | *(none)* | Item | no | – |
| Purchase By Item (Monthly) | *(none)* | Item | yes | – |

**Month-name spelling.** These headers read **Asoj** and **Ashad** where phase-23's shipped
`BS_MONTH_NAMES` (also live-sourced, from the date picker) read **Aswin** and **Asar**. The
reference product is not self-consistent across its own screens. Phase 23's spellings stay, because
they are already rendering in every date control in this app and a second spelling of the same month
in the same product is worse than matching one screen.

### Sales Summary Report

| | |
|---|---|
| **Filters** | BS fiscal-year picker, **Select Mode** |
| **Subtitle** | `For fiscal year 2083 / 2084` |
| **Columns** | Date, Sub Total, Discount, **Service Charge**, Non Taxable Sales, Taxable Sales, VAT, Total |
| **Footer** | **none** — no Total row at all |

**Select Mode** is a two-option picker, `Date` and `Month`, behaving as a radio (checking one clears
the other) despite rendering as checkboxes:

- **Month** — one row per BS month, labelled `Shrawan, 2083`. Only months with activity appear (the
  live run returned 2 rows, not 12) — unlike the Monthly crosstabs, which always render all twelve
  columns.
- **Date** — one row per **day** with activity, newest first, labelled in the tenant's own date
  format (`02-09-2026`).

Negative rows appear in both modes (`(8,000)`, `(1,036.45)`), so the figures are **net of credit
notes**, not gross sales. **Service Charge printed `-` on every row of both modes**, on a tenant
with three years of data — consistent with it being a product-level flag (`service_charge_applicable`)
this tenant never sets, and with the roadmap's instruction to omit it here rather than fake it.

---

## Scope decisions

### Decision A — one handler per mirrored pair, seven handlers for thirteen reports

Phase 9 answered four report screens with two handlers by discriminating on `ContactType`, the way
`Payment` already discriminated Received-from-Paid on one aggregate. Every pair in this phase turned
out to be a true mirror when read live — same filters, same columns, one word different in a header
— so the same choice applies, and the count is:

| Handler | Answers | Discriminator |
|---|---|---|
| `ContactBalanceSummaryQuery` | Customer Receivable Summary, Supplier Payable Summary | `ContactType` |
| `DocumentAgeQuery` | Invoice Age, Purchase Bill Age | `ContactType` |
| `TradeByContactQuery` | Sales By Customer, Purchase By Supplier | `TradeSide` |
| `TradeByItemQuery` | Sales By Item, Purchase By Item | `TradeSide` |
| `TradeByContactMonthlyQuery` | the two contact Monthly variants | `TradeSide` |
| `TradeByItemMonthlyQuery` | the two item Monthly variants | `TradeSide` |
| `SalesSummaryReportQuery` | Sales Summary Report | *(no mirror exists)* |

**The side is hardcoded at the route, never bound from the query string.** That is what makes the
two permission keys of a pair real: `AuthorizationBehavior` reads `PermissionKey` off the request
the *route* constructed, so a caller granted `Reports.SalesByItem.View` cannot reach the purchase
side by flipping a parameter. Same choice `ContactsEndpoints.MapReportEndpoints` already made.

The four `Trade*` handlers live in a new `src/Application/Trade/` folder rather than under `Sales`
or `Purchasing`. Putting a handler that answers a purchase report under `Sales` (or duplicating the
folder to avoid saying so) would misdescribe it; `Trade` names what the pair actually is. Application
folders here are feature groupings, not enforced contexts — `Exports`, `Imports`, `Printing` and
`Identity` are the precedent.

### Decision B — a contact-tagged Journal Voucher is a ledger event, and this phase makes it one

**The question the kickoff asked**, and the live pass answered it before the design: Invoice Age's
Txn Type filter names *Journal Voucher* explicitly, and four `JV####/83-84` rows carried amounts and
ages beside the invoices.

`JournalVoucherLine.ContactId` has existed since phase 17 and its own doc comment says the line
"posts against a Contact's own AR/AP control account". **Nothing read it back.** So a JV posted to a
customer moved the general ledger without moving that customer's Contact Statement — which was
already wrong, and would have become visibly wrong the moment Invoice Age listed the voucher and the
Receivable Summary did not count it.

`ContactLedgerReader` therefore gained Journal Voucher events, which:

- makes **Customer Receivable Summary**, **Invoice Age**, **Contact Statement** and **Contact
  Overview** mutually consistent — the last two are pre-existing screens whose numbers change;
- rolls the voucher up to **one event per (voucher, contact)**, since a voucher may carry several
  lines against the same contact and the live report shows it once;
- **signs by side**: a net debit increases a customer's balance, a net credit increases a supplier's;
- filters tagged lines by the contact's own `Type`, so a supplier-tagged line can never appear in a
  customer ledger (there is a test for exactly that).

`DocumentAgeQueryHandler` additionally counts **JournalVoucher-sourced payment allocations**, which
phase-17 generalised `PaymentAllocation.SourceType` to allow and `ContactAgeingSummaryQueryHandler`
still does not — a limitation that handler's doc comment flags and this report does not inherit.

**This is a correction with a blast radius, and it is stated rather than slipped in.** Two shipped
reports change their output for any tenant that has ever tagged a JV with a contact. On a tenant that
has not, nothing changes.

### Decision C — Due Date is the document's own date wherever nothing stores one

Age runs from the Due Date; that was proved live twice, once across a year boundary. But **only
`Expense` carries a `DueDate` column in this codebase** — Invoice and PurchaseBill do not, and no
Contact carries a credit term to derive one from.

This is phase-9's wall again, where the live Ageing Summary's "Credit Term" column was dropped for
the same reason. The resolution is the same and deliberately partial: the Due Date column is **real
where the data is real** (Expense) and **equal to the document date everywhere else** — which is
exactly how the live report renders its own Journal Voucher and quick-document rows, so the column
is not a fiction, just less informative than the reference product's on two document types.

A stored `DueDate` on Invoice and PurchaseBill belongs with Credit Terms as a whole — the
`CreditTerm` lookup exists and nothing consumes it — and is named as a follow-up rather than
half-built here.

### Decision D — what is ageable, and the two live options this codebase cannot express

`AgeableDocumentType` is its own enum rather than a reuse of `DocumentType`, because the two sets
are not the same thing. The live Txn Type filters enumerate five options per side; this codebase can
express four of them:

| Live option | Here |
|---|---|
| Opening Balance | **Yes** — `Contact.OpeningBalance`, not `OpeningBalanceLine` (which is keyed by account and carries no contact at all). No number, no reference, no date, so it ages from the as-of date: age 0, status Current. |
| Invoice / Purchase Bill / Expense | Yes |
| Journal Voucher | Yes (Decision B) |
| **Quick Payment** / **Quick Receipt** | **No.** Phase-17 Decision #7 made Quick Payment/Receipt a thin variant of the existing `Payment` aggregate rather than a document type of its own, because Tigg's is a generic multi-line Accounts-table document this codebase's single-contact `Payment` cannot represent. There is no such document to age. An unallocated Payment is a *credit* against the contact — it already reduces the balance in Customer Receivable Summary — not an outstanding item with an age. Omitted with this note rather than faked. |

### Decision E — the BS calendar goes to the server, and five reports consume it

The roadmap scheduled a Domain `BsDate` converter here on the strength of Sales Summary alone. The
live pass found **all four Monthly variants are keyed by a BS fiscal-year picker too**, so the
converter has five consumers on arrival rather than one, and phase 27b's PDF/`.xlsx` work makes six.

`Domain/Common/BsCalendar` is a **verbatim port** of `web/src/app/shared/formatting/bs-date.ts`,
month-length table included, generated from that file rather than retyped. The two must agree
exactly — a fiscal-year boundary one day out on the server would file a sale under the wrong year in
a report whose own screen prints the right date beside it — so `BsCalendarTests` re-asserts the same
three families of anchor `bs-date.spec.ts` uses (live-confirmed AD/BS pairs, published Nepali New
Years, irregular-length months) and round-trips every one of the **33,969** days in range.

What is new on top of the port is the fiscal year: `FiscalYearStartMonth = 4` (Shrawan),
`FiscalYearMonths` returning the twelve months in fiscal order each with the **AD range** it covers
(because every date in this system is stored in AD), `FiscalYearOf`, and `SupportedFiscalYears`,
whose last entry is `LastYear - 1` since a fiscal year needs its *following* BS year in the table.
Tests pin the Shrawan-1 boundary from both sides and assert the twelve AD ranges tile the year with
no gap and no overlap.

**Month-name spelling stays phase-23's.** The live crosstab heads its columns **Asoj** and **Ashad**
where phase-23's shipped `BS_MONTH_NAMES` — also live-sourced, from the date picker — read **Aswin**
and **Asar**. The reference product is not self-consistent across its own screens; this app renders
one spelling everywhere, and it is the one already in every date control.

### Decision F — Service Charge is omitted, not zero-filled

Sales Summary's live column set carries **Service Charge**, driven by a product-level
`service_charge_applicable` flag this codebase does not model. It printed `-` on every row of both
modes even on the reference tenant.

The roadmap said omit it with a note, and the live evidence supports that over the alternative: a
column of hard zeroes reads as an answer ("this tenant charges no service fees") when the truth is
"this system cannot tell you". The DTO has no such field, the screen prints a one-line explanation
where the column would be, and the flag that would have to exist first is named here.

### Decision G — the permission split, and the line it falls on

Thirteen keys, derived per report rather than defaulted across the group. The line is simple:
**eight of these reports put a named contact next to a money figure, and five do not.**

**Admin-only (8):** Customer Receivable Summary, Supplier Payable Summary, Invoice Age, Purchase
Bill Age, Sales By Customer, Purchase By Supplier, Sales By Customer (Monthly), Purchase By Supplier
(Monthly).

- The two balance summaries take `CustomerAgeingSummaryView`'s answer, which phase-9 justified as
  "lists every Contact's identity next to their outstanding balance" — the same shape, in the same
  report group. Granting the *less* detailed sibling more widely than the more detailed one would be
  an arbitrary line.
- The two age reports disclose strictly more than those summaries do — every unpaid document with
  its number, reference and balance — so they cannot be less restricted.
- The two By-Contact reports are the Sales/Purchase Master Reports rolled up over the same rows, and
  both of those are Admin-only; the rollup keeps the commercially sensitive half (who the customers
  are and what each is worth).
- **Sales By Customer (Monthly) carries a PAN column**, live-confirmed — the single strongest
  Admin-only factor in this codebase (`TdsReportView`, `AnnexThirteenView`). Its purchase mirror
  takes the same class so an Admin reasons about the pair together.

**Admin+Member (5):** Sales By Item, Purchase By Item, both Item Monthly variants, Sales Summary
Report. None names a contact: the first four are one row per product or category with quantity and
value — the Inventory Position / Stock Ageing shape, Admin+Member since phase 19 — and Sales Summary
is the tenant's own totals per BS month or day with no contact, product or document number at all,
which is the VAT Summary Report's shape (Admin+Member since phase 8c).

### Decision H — two totals refused, and one report with no total at all

- **`TradeByItemDto` has no total quantity**, and the DTO expresses that by *not having the field* —
  a template cannot render a total that does not exist. Its rows are products in different units of
  measure, so their quantities are not the same unit of account. This is phase-26a's own refusal;
  the reference product reaches it independently, and its footer leaves that cell blank.
- **`SalesSummaryReportDto` has no totals whatsoever**, matching the live report, because a sum over
  "one row per month" and a sum over "one row per day" would mean different things in the two modes.
- Every other footer total **is** server-computed over the full filtered set, never a client-side
  reduce over the displayed page (phase-16c).

### Decision I — returns are negative facts, not separate rows

`TradeLineReader` contributes CreditNote/DebitNote lines **negated**, so every analytics figure is
net of returns. That is not an assumption: the live Sales Summary prints negative rows on days whose
returns exceeded their sales, which is only possible if returns are folded into the same measure.
It is the one place these reports diverge from the Sales/Purchase Master Reports, which keep returns
positive beside a Type column because they are a register rather than an analysis.

**The identity `Amount - Discount == NetAmount` holds on every row**, and it is pinned by the live
figures rather than assumed: a customer at Amount 50,000 / Discount 5,000 / Net Sales 45,000, and
the Sales Summary's Bhadra 2083 row at Sub Total 41,987.95 less Discount 4,950 equalling its two
sales buckets' 37,037.95. That reading is what fixed `Amount` as the **gross** line value rather
than the after-line-discount one, which is how a first pass had it.

---

## Manual E2E (real API, real SQL Server, real browser)

A fresh Organization (`Phase 26b Reports Co`) seeded entirely by curl against a running API: chart
of accounts, accounting defaults, warehouse, contact group, category, unit, two products (one
`NoVat`, one `ThirteenPercentVat`), one customer carrying a **1,500 opening balance**, one supplier.

Then, deliberately, **one overdue and one current document on each side** plus the two document
kinds this phase added:

| Document | Date | Figures |
|---|---|---|
| Invoice 0001 | −45 days | 10 x 500 less 10% line discount = **4,500** |
| Credit Note | −20 days | 2 units off Invoice 0001 = **900** |
| Invoice 0002 | today | 2 x 1,000 @ 13% VAT = **2,260** |
| Payment received | today | **600** allocated to Invoice 0002 |
| Purchase Bill 0001 | −45 days | **3,200** |
| Purchase Bill 0002 | today | **900** |
| Journal Voucher 0001 | −20 days | **750** debited to the customer's AR |

**What the reports returned**, and the arithmetic that checks them:

- **Customer Receivable Summary** — 7,510.00 DR
  = 1,500 opening + 4,500 − 900 + 2,260 − 600 + 750.
- **Invoice Age** — four rows: Invoice 0001 (4,500 / paid 900 / balance 3,600 / Overdue / 45 days),
  Journal Voucher 0001 (750 / Overdue / 20 days), Invoice 0002 (2,260 / paid 600 / balance 1,660 /
  **Current** / 0 days), Opening Balance (1,500 / Current / 0 days). Totals 9,010 / 1,500 / **7,510**.
- **The cross-check that matters:** Invoice Age's total balance **equals** Customer Receivable
  Summary's closing balance, to the cent. Two different code paths, one number — which is the whole
  reason both read `ContactLedgerReader`.
- **Supplier Payable Summary** 4,100 CR and **Purchase Bill Age** total balance 4,100, likewise equal.
- **Sales By Customer** — Amount 6,000, Discount 400, Net 5,600, VAT 260, Total 5,860, and
  6,000 − 400 = 5,600 exactly.
- **Sales By Item** — Consulting quantity **8** (10 sold less 2 returned), net 3,600; Taxable Support
  quantity 2, net 2,000, VAT 260. Grouped by Category instead: one `Services` row at 5,600.
- **Sales By Customer (Monthly)** — window 2026-07-17 → 2027-07-16, columns *Shrawan 2083 … Asar
  2084* with quarter subtotals interleaved; Shrawan **3,600** (the invoice less the credit note, both
  falling in that BS month), Bhadra **2,000**, 1st Quarter **5,600**, total **5,600**.
- **Sales Summary, Month mode** — Shrawan 2083 (sub 4,000, discount 400, non-taxable 3,600) and
  Bhadra 2083 (sub 2,000, taxable 2,000, VAT 260, total 2,260); only the two months with activity,
  and `sub − discount == non-taxable + taxable` on both.
- **Sales Summary, Date mode** — three rows newest first, including **2026-08-14 at −900**: the day
  whose only movement was a credit note. Negative rows are the live behaviour and the proof that
  returns are folded in rather than listed.

**All thirteen `.xlsx` exports** returned HTTP 200 with a `PK` zip magic and 6.6-7.1 KB of real
workbook, including the four crosstabs through the shared `ExportMonthlyCrosstab` writer.

**All thirteen negative paths** returned **403, not 404**, against a nonexistent organization id,
each naming its own key — `Reports.CustomerReceivableSummary.View`, `Reports.InvoiceAge.View`,
`Reports.SalesByCustomerMonthly.View`, and so on. 403-before-404 is what proves
`AuthorizationBehavior` fired ahead of the handler.

**`sqlcmd` re-derived Invoice Age from the raw tables** — `Invoices` / `InvoiceLines` netted against
`CreditNotes` by `ReferrerId` and `PaymentAllocations` by target, with the JV summed straight from
`JournalVoucherLines` — and returned 4,500/900/3,600 at 45 days, 2,260/600/1,660 at 0 days, 750 at
20 days, and the 1,500 opening balance. Every figure matches the report, computed by a path that
shares no code with it.

**Browser pass** (dev-cert + cookie transplant, the phase-25 Step 3 recipe): the Monthly crosstab
rendered its twelve BS months with quarter subtotals in the right places and header and body cells
aligned; Invoice Age rendered all four rows with the Journal Voucher and Opening Balance labelled
correctly and the footer at 9,010 / 1,500 / 7,510; Sales Summary rendered "For fiscal year 2083 /
2084", both pickers, the Service Charge note and no footer row. **No console errors.**

---

## Known limitations and follow-ups

1. **Invoice and PurchaseBill store no due date** (Decision C). Belongs with Credit Terms, whose
   lookup exists and is unconsumed.
2. **Quick Payment / Quick Receipt are not ageable documents here** (Decision D) — a consequence of
   phase-17 Decision #7, not a gap this phase introduced.
3. **Service Charge is absent from Sales Summary** (Decision F) — needs a product-level flag first.
4. **`ContactAgeingSummaryQueryHandler` (phase 9) still counts only Payment-sourced allocations**
   and still buckets from the document date. This phase's `DocumentAgeQueryHandler` does neither;
   the two reports can therefore disagree for a tenant using JV-sourced allocations. Aligning phase
   9's handler is a small, separate change deliberately not folded into a phase that already touches
   `ContactLedgerReader`.
5. **Dates in exports stay AD**, inheriting phase-23 Decision A's carried limitation — now also
   across this phase's thirteen new export routes. Scheduled for 27b, which is why `BsCalendar`
   landed in Domain rather than beside the query that first needed it.
6. **The four Monthly crosstabs page by row, not by column** — all twelve months always render.
   That matches the live layout; a tenant with thousands of contacts pages the rows.
