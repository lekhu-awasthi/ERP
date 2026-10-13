# Phase 90 — an expense's VAT read live, Annex 13 before VAT, the supplier's number in the purchase book, and one contact's settlements read through one contact

## TL;DR

**Phase 89 left two open questions: how the vendor files an expense's VAT, and whether the reads it called linear
in history should be narrowed.** This phase answered both by reading and measuring before building.

- **The expense's VAT, read live.** One expense was created and approved on the vendor's tenant through its own
  screens. The vendor files it exactly as phase 89 decided ours would:
  - taxable at 13% in the purchase book and on the return;
  - its No-VAT and 0-VAT lines both under exempt purchases.

  No change was needed there.
- **Annex 13 is before VAT now (the user).** The same read showed the vendor's Annex 13 counts every figure **before
  VAT**. Ours had added each line's VAT since phase 8e, so every figure was 13% high and the 1-lakh threshold was crossed
  early.
- **The Purchase Register shows the supplier's invoice number (the user).** The vendor's purchase book prints the
  **supplier's own invoice number** under बीजक नं. (a debit note shows its own). Ours now carries that as *Invoice No*,
  on screen and in the export, with our number kept in *Document No*.
- **One contact's settlements are read through that contact.** When a payment form, the FIFO Suggest or a till refund
  asks about one contact, `OutstandingDocumentReader` now narrows every settlement query through that contact's
  documents, as a subquery.
  - One customer's picker: **38,015 → 238 logical reads, 726 → 40 ms.**
  - The voucher read became one join for every caller, so the organization-wide ageing reports fell too:
    **153,964 → 88,985** (customers) and **108,213 → 42,969** (suppliers).
- **The VAT return's history read was measured and left alone.** It is inside the 2 s budget at the top of NFR-5.1's
  range (1.37 s median). Its reads do not depend on history at all: 39,992 for a year with none and for three years.
- **Found on the way:** both registers wrote a return's empty buckets as `-0` in the export (phase-26c bug #1 again,
  in the negation every return row has had since phase 19). Fixed.

| | |
|---|---|
| Live read taken | **yes**, abcagro1 (12 trial days left). Two writes, both made through the vendor's own screens: an expense and a debit note (see the end of this doc) |
| Migration | **none** |
| New permission keys | **none** |
| Changed endpoints | `GET /reports/purchase-register` rows gain **`invoiceNo`** (and `/export` an *Invoice No* column after *Document No*). `GET /reports/annex-thirteen` figures are **before VAT** |
| Changed screens | Purchase Register (an *Invoice No* column) |
| Changed print output | none |
| Initial bundle | **653.05 kB** (unchanged) against 680 kB |

**Three things worth carrying forward.**

1. **Measure where the reads go before narrowing anything.** Phase 89 named the VAT return's history read as the thing
   to narrow, and phase 88 named the settlement queries. Measuring both, with a statement-level breakdown, gave
   opposite answers:
   - **The return:** its reads are the same for a year with no history and for three years, because every line table
     is scanned either way. Narrowing its history would save CPU and nothing else, and it is inside budget.
   - **The picker:** 99.4% of its reads were four organization-wide settlement queries returning rows it then threw
     away. Narrowing them took 38,015 reads to 238.
2. **Narrow through the contact's documents as a query, never as a list.** `IQueryable<Guid>.Contains` is an `IN`
   subquery the optimiser seeks with the target index. A materialised list is phase 34c's OPENJSON parameter.
   - The old voucher read sent every approved voucher's id back to SQL as such a parameter.
   - It then loaded **every contact in the organization** to check one line's type.
   - Both are now one join, and that join is what lowered the organization-wide ageing reports.
3. **A figure designed from first principles was the wrong basis for a filed form.** Phase 8e made Annex 13 gross on the
   reasoning that it tracks "the consideration that changed hands". The vendor's annex is before VAT, and so is the
   income-tax filing it is attached to. Phase 89's lesson applies here too: a statutory report designed before the
   vendor's was opened is a hypothesis.

Tests:

| Suite | Count |
|---|---|
| Domain | 1032 |
| Application.UnitTests | **1817** (+4) |
| Infrastructure.UnitTests | 14 |
| Api.IntegrationTests | 76 |
| Angular | 881 (110 files) |

All four .NET suites ran one at a time with Docker up, and `ng build` and `ng test` ran alone. All are green.

---

## 0. Live read, 2026-10-13 (abcagro1)

**The expense** (`#/purchases/expenses/add`, saved and approved as EXP0001/HO/83-84). The form has:

- a supplier, a Supplier Invoice Reference No, the date and due date, and the currency;
- account lines, each with an Amount and a **Tax** select offering *No Vat*, *0 Vat* and *13% Vat*.

There is no capital or import control on the expense. The create posted each line's `vat_type` as `"13 VAT"`,
`"NO_VAT"` or `"0 VAT"`, plus `taxable_total`, `no_vat_total`, `zero_vat_total` and
`supplier_invoice_reference_no`. Lines entered: 1,000 at 13%, 200 No Vat, 300 0 Vat. The form showed:

- Taxable Total 1,000;
- Non-Taxable Total 500;
- VAT 130;
- Grand Total 1,630.

**Where it landed:**

- **VAT Summary, Asoj** (index 2): `taxable_purchase` 1,000, `taxable_purchase_vat` 130, `exempt_purchase` 662. That is
  the earlier bill's 162 plus the expense's 200 + 300, so No Vat and 0 Vat both count as exempt. The month became a
  credit of 38.08 (214.18 − 122.26 − 130), carried into every later month, as ours carries.
- **Purchase Register:** a row with `source: "Expense"`, total 1,500, exempt 500, taxable 1,000, VAT 130. Under बीजक नं.
  it printed **P90-EXP-1**, the supplier reference. The bill PB0001, which has no supplier reference, printed nothing
  there. The vendor's own number appears nowhere on the purchase register.
- **Annex 13** (threshold lowered to 100 to see it): the supplier's `service_purchase_others` was **1,662** = 162 +
  1,500. The expense counted **before VAT**, and as service even for its line booked to *Purchase Goods*. The customer's
  `service_sales` was 707.05 = 1,647.50 − 940.45, also before VAT (798.97 with it).

**The debit note** (created from PB0001's *Create Debit Note*, approved as DN0001/HO/83-84). The conversion pre-fills
and locks the note's Reference No with the bill's own number. On the Purchase Return Register, and as the debit-note
row of the Purchase Register, बीजक नं. prints the note's **own** number. The note is issued by the buyer, so this is
consistent with the rule.

## 1. What shipped

**Annex 13 is before VAT.**

- `AnnexThirteenReportQueryHandler` no longer reads a line's `VatAmount`. Each line folds its value alone to base at its
  document's rate.
- The threshold is measured against that pre-VAT total.
- The query's doc comment no longer claims gross.
- `Every_figure_and_the_threshold_are_before_VAT_as_the_reference_products_annex_is` uses the vendor's own figures: an
  expense of 1,000 @13% + 200 No VAT + 300 zero VAT gives 1,500, and sales of 1,647.50 less returns of 940.45 give
  707.05. A threshold of 1,500 includes the supplier and 1,501 does not. The test fails against HEAD's handler.

**The purchase book's invoice number.** `PurchaseRegisterRowDto.InvoiceNo` (after `DocumentCode`) is:

- the bill's or expense's `SupplierInvoiceReference` (null when none was entered);
- a debit note's own `Code`;
- a migrated row's `DocumentCode`, which its importer fills from the legacy register's *Bill No*.

`PurchaseBookReader` carries the reference, so the VAT return reads nothing new. The export gains *Invoice No* after
*Document No*. The screen gains the column, with `text-nowrap` so a number like `T-INV-42` never breaks at its hyphen,
and the footer's colspan goes from 6 to 7.

**One contact's settlements, read through one contact** (`OutstandingDocumentReader`). When `contactId` is given:

- **Allocations** (from payments and from vouchers) and **credit/debit-note reductions** are narrowed by
  `targetIds.Contains(...)`. `targetIds` is that contact's invoices (or bills) as an `IQueryable<Guid>`.
- **Tenders and the change given** are narrowed through the contact's invoices.
- The organization-wide callers (both ageing reports) pass no contact and are unchanged, as phase 42 measured and left
  them.

**The voucher read is one join, for every caller.** Approved vouchers ⋈ contact-tagged lines ⋈ the contact, filtered by
the report's contact type in SQL. This replaces three reads: every voucher; then their lines by a list of those ids;
then every contact's type.

**The test.** `OneContactSettlementTests` holds one contact's Invoice Age equal, row for row, to the organization-wide
Invoice Age filtered to that contact. It covers two worlds:

- **Two customers and two suppliers**, each with a part-paid document, a linked return and a contact-tagged voucher.
- **A till:**
  - the walk-in's sales with change, one refunded over the counter;
  - a named customer's sale on credit refunded back onto the account;
  - a paid sale refunded with a payout;
  - an ERP invoice.

Two injections were caught, and the file was restored by hash each time:

- narrowing through the wrong documents failed both tests;
- inverting only the tender narrowing failed the till test.

**Signed zero in the registers.** Both registers negated every bucket of a return row, so a zero bucket was `-0m` and
the export wrote `-0` (phase-26c bug #1). Each handler now uses a local `Negated` that leaves a zero unsigned. Two
`decimal.IsNegative` assertions failed under an injected plain negation.

**Measurement tooling.**

- `tools/scale/seed-phase90.sql` seeds the 34c tenant with 30,000 approved receipts each allocated to an invoice, 20,000
  tendered invoices and 7,143 contact-tagged vouchers.
- `tools/scale/probe-phase90-io.sh` gives logical reads per path, the phase 50 method.
- The before and after CSVs are committed beside them.

## 2. Scope decisions

**A. The candidate (the user):** phase 89 § 5's expense VAT, with phase 88 § 5's measurement alongside.

**B. The expense needs no change.** The vendor files it as phase 89 Decision E did: taxable non-capital local, with No
VAT and zero VAT both exempt. Ours has the same three rates (`VatRate.NoVat`, `ZeroVat`, `ThirteenPercentVat`), and the
two readers bucket them identically.

**C. Annex 13 before VAT (the user, recommended).** Two reasons:

- the vendor's annex is before VAT;
- Annex 13 is filed with the income-tax return, where a VAT-registered business's purchases and sales exclude VAT.

It lowers every figure on a VAT tenant, and a contact near the threshold may drop out. That is a correction.

**D. The supplier's number, with ours kept (the user, recommended).** The vendor prints only the supplier's number. We
add it as *Invoice No* and keep *Document No*:

- the row still needs a key a user can find the bill by;
- the export's earlier columns keep their meaning.

A debit note's Invoice No is its own number, as the vendor's is. The Purchase Return Register already showed the note's
own number and is unchanged.

**E. The VAT return's history read is not narrowed.** Measured on the 50,001-invoice tenant:

| Fiscal year | History | Median wall |
|---|---|---|
| 2083 | three years | 1.37–1.39 s |
| 2080 | none | 0.63–0.66 s |

The reads were 39,992 in both. The extra cost is rows and CPU, not reads, and the return is inside its 2 s budget at
the top of NFR-5.1's range. That range caps a tenant's documents, not its years, so a ten-year tenant with the same
volume reads the same rows.

An exact narrowing would have to fold each history line to base at its own rate. A SQL aggregate over history would
round differently from the registers, which would break phase 89's "a month equals the registers by construction".

**F. Narrow the picker, not the ageing reports.** The settlement queries stay organization-wide for the
organization-wide callers (phase 42's measurement and reasoning stand). They narrow only when one contact is named.

The narrowing is a subquery, never a list. With a contact, the candidate set is small, but a list would still be an
OPENJSON parameter. The subquery lets SQL Server seek `IX_PaymentAllocations_TargetDocumentType_TargetDocumentId` per
document.

**G. The voucher join is a correction for every caller, not only the narrowed one.** The old dictionary loaded every
contact (50,003 rows, 10,656 reads) to check the type of lines that mostly were not there. The join makes the same
check in SQL. Both ageing reports fell (decision F leaves the rest of their reads alone).

**H. The signed-zero fix was taken in passing.** It was found by this phase's export check. The fix is two lines, and
the gotcha it repeats is already in CLAUDE.md.

**I. No new keys.** The register and Annex 13 keep `Reports.PurchaseRegister.View` and `Reports.AnnexThirteen.View`.
The picker keeps `Sales.Invoice.View` / `Purchasing.PurchaseBill.View`.

## 3. Bugs found and fixed

1. **Annex 13 counted VAT.** Every figure was gross since phase 8e, 13% high on a taxable line, and the threshold was
   crossed early. Found by the live read.
2. **The purchase book named our number where the supplier's belongs.** Since phase 19. Found by the live read.
3. **A return's empty buckets were exported as `-0`** in both the Sales and Purchase Registers, since
   phase 19 built them. Found by this phase's export check.
4. **One contact's picker read the whole organization's settlements**, about 700 ms per open on the scale tenant. Since
   phase 88 made the pickers read the reader.

## 4. Evidence

**Scale probe** (`tools/scale/probe-phase90-p90-{before,after}.csv`). The tenant is 34c's 50,001 invoices, plus phase
50's cheques and this phase's seed. Statistics were refreshed before each pass, and every path answered 200.

| path | reads before | reads after | median before | median after |
|---|---:|---:|---:|---:|
| vat.fy2083 | 39,992 | 39,992 | 1,387 ms | 1,372 ms |
| vat.fy2080 | 39,992 | 39,992 | 655 ms | 625 ms |
| picker.customer | 38,015 | **238** | 726 ms | **40 ms** |
| picker.supplier | 11,157 | **274** | 158 ms | **34 ms** |
| suggest.customer | 38,015 | **238** | 753 ms | **50 ms** |
| ageing.customers | 153,964 | **88,985** | 1,995 ms | 1,756 ms |
| ageing.suppliers | 108,213 | **42,969** | 811 ms | 605 ms |

Where one customer's picker spent its 38,015 reads before:

- tenders, 11,260;
- the change-given scan, 10,788, for zero rows;
- every contact's type, 10,656;
- allocations, 4,824;
- the contact's own documents, about 30.

The VAT return's 39,992 is about 18,600 for invoice lines and 10,800 for invoices, plus the bill tables.

**Fresh-org E2E** (*Phase 90 E2E 10130049*, seeded through the API). The data: two customers and two suppliers; two
13% invoices, one returned in part; two receipts; a contact-tagged voucher; the vendor's expense; a bill with
supplier reference `T-INV-42` and its debit note. Every status was printed.

| Check | Result |
|---|---|
| Customer A's picker | 0001 at 630 (2,260 − 1,130 returned − 500 received) |
| A's Invoice Age read alone | equals A's rows in the organization-wide read (the invoice and the voucher's 250) |
| Suggest | 630 |
| Annex 13, threshold 0 | A 1,000, B 1,000, S 1,500, T 400, all before VAT |
| Annex 13, threshold 1,500 / 1,501 | lists S / does not |
| Register Invoice No | `T-INV-42`, `P90-EXP-1`, the debit note's `0001` |
| Export headers | *Date (BS), Date (AD), Type, Document No, Invoice No, Import Declaration No, …* |
| `-0` cells | none in either register's file |

SQL confirmed the stored values:

- the supplier references on `purchasing.PurchaseBills` and `.Expenses`;
- the expense's three `VatRate`s;
- both allocations Approved;
- the invoices' net and VAT.

**403-not-404.** A custom role holding only `Reports.PurchaseRegister.View` and `Purchasing.PurchaseBill.View` gave:

- Annex 13: **403 naming `Reports.AnnexThirteen.View`**, beside a 200 on the Purchase Register;
- the picker for a **nonexistent** contact: **403 naming `Sales.Invoice.View`** on Received, beside a 200 on Paid. As
  Admin, the same request was a 200 `[]`, so the 403 came from the behavior.

The membership was put back on Admin by `UPDATE tenancy.OrganizationMemberships`, and a 200 confirmed it.

**Browser and keyboard** (`erp-web-ssl`, cookie through `web/public`, file deleted at once):

- The Purchase Register shows *Invoice No* beside *Document No*. The debit note's empty buckets read 0.00, and the
  footer's Total spans the seven text columns.
- On Annex 13, the Threshold Amount field was focused (asserted), cleared with Ctrl+A, and 1500 typed, then Tab. The
  request sent `thresholdAmount=1500` and only Sagarmatha Services remained, at 1,500.00. Typing 1501 sent
  `thresholdAmount=1501` and the page read "No Contact's period activity meets the threshold amount."
- The console showed no errors.

**PDFs:** none changed.

## 5. Left open, and for later phases

- **A till refund reads the walk-in's settlements.** `PosRefundPlanner.OwedOnSaleAsync` names the sale's contact, which
  on a retail till is the walk-in, who owns every counter sale. Narrowing by contact barely helps it. Narrowing by the
  sale itself would, but measure on a till-heavy tenant first: the scale tenant has no till sales of its own.
- **Annex 13's opening and closing balance** stay phase 8e's approximation (`Contact.OpeningBalance` + period activity).
  The vendor's row carries a real-looking `closing_balance` (−1,792 for the supplier, unread).
- **Annex 13 classes an expense as service even for a goods account.** That is the vendor's behaviour and ours.
- **A tax advisor's word** is still open on phase 89's two items (the carry across the year's end; a domestic zero-rated
  supply), and now on the annex's basis. We follow the vendor and the income-tax reasoning.
- **The VAT return's history read** stays from the beginning of the books (Decision E). Revisit only if a tenant beyond
  NFR-5.1's range appears.
- **Carried unchanged:**
  - phase 87 § 5 / Decision G; phase 84 § 5's money receipt on the roll; phase 83 § 5; phase 81 § 5; phase 80 § 5;
  - phase 79 § 5; phase 77 § 5; phase 76 § 5; phase 75 § 5; phase 72 § 5; phase 71 § 5's other items; phase 70 § 5;
  - phase 69 § 5; phase 67 § 5; phase 66 § 5; phase 62 § 5;
  - phase 88 § 5's per-row *This Allocation* table and the unread foreign-currency allocation.

## Writes made to the vendor tenant

Two writes, both through the vendor's own screens with the user's standing permission. Neither was made by replaying
its headers.

1. **Expense EXP0001/HO/83-84** (13-10-2026, Ram Hari Sharma, supplier reference P90-EXP-1): 1,000 @13% + 200 No Vat +
   300 0 Vat on *Purchase Service* / *Purchase Goods*. Created and approved.
2. **Debit note DN0001/HO/83-84** (13-10-2026), converted from PB0001/HO/83-84, both lines, 162. Created and approved.

Every report generated during the read was a `POST /api/v1/erp/report`, which only reads. After the expense was
approved, every other non-GET was refused in the page until the debit note.
