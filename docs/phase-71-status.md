# Phase 71 — Every bill's date in BS: a tax document's date is a statutory field, not a display preference

## TL;DR

**A bill now prints its dates in BS with the AD date beside it, whatever the viewer's calendar toggle says**, on paper,
in the emailed PDF and at the till. Before this phase none of our bills did unless the viewer had set the toggle to BS,
and the emailed PDF never did. The user stated the rule on 2026-10-08. The user's decisions (2026-10-09):

- **BS with AD beside it**: `2083-04-16 BS (2026-08-01 AD)` on the PDF; `16-04-2083 BS` over `(01-08-2026 AD)` on the
  80 mm receipt.
- **Both statutory dates**: the *transaction date* and the *invoice issue date* (VAT Rules Schedule 5; the e-billing
  procedure's Annex 6). The issue date is the Nepal day the bill was approved, when it got its number. An abbreviated
  tax invoice keeps Schedule 6's single date.
- **"A bill" is a tax document**: Invoice, Credit Note and Debit Note (VAT Rules 17, 18 and 20), plus the till's sale
  and refund receipts. Every other printable type, every screen and every error message still follows the toggle.
- **The statutory registers' `.xlsx` exports** (Sales, Purchase, Sales Return, Purchase Return and the two migrated
  registers) carry a `Date (BS)` column beside `Date (AD)`, always. Their on-screen grids follow the toggle.

**The rule's source, read first (§ 0):** the statutory forms require the two dates but **name no calendar**. Only Rule 26
(the tax period is a BS month) mentions Bikram Sambat. Practitioners state the BS convention without citing anything.
So this is a product decision that agrees with the law, made knowingly, not a quoted clause.

**The vendor makes the calendar a per-template switch (AD | BS, and "Both" for library templates) that defaults to
AD**, read live on its tenant's last day. Phase 28's "the printed invoice shows BS" was that tenant's setting. We
diverge on purpose: a tax document has no switch. The printed "BS"/"AD" labels are the surface the divergence owes.

| | |
|---|---|
| Live read taken | **yes**, read-only, 2026-10-09, *Hamro Samaan*'s last trial day (§ "Live read") |
| Migration | **none**. The issue date is `ApprovedAt`, which all three document types already store |
| Changed endpoints | none in shape. The invoice, credit-note and debit-note PDFs (`GET`/`POST /print/...`, and the email job's attachment through the same handler) print the new dates; the six register exports gain a column |
| New permission keys | **none** (Decision I) |
| Changed screens | the till's sale and refund receipts (80 mm). The PDF is server-rendered |
| Initial bundle | **652.58 kB**, unchanged, against 680 kB |

**Four things worth carrying forward.**

1. **Find the rule's text before building on it, and say so when it is a convention.** The user's rule was right about
   practice and silent in the statute. The research also turned up a real gap nobody had asked about: the forms want
   *two* dates, and our bills printed one.
2. **One choke point per medium, and a guard that names it.** Every date the print handler writes goes through its one
   `FormatDate`, and the receipts' every `nepaliDate` names `'bill'`. Both guards fail the build on a stray call, and
   the C# one caught a formatting slip in this phase's own sweep.
3. **Look at the paper.** The E2E's PDF text and the browser pass each found a wrap that no assertion on a string
   could: a credit note's "(2026-08-01" / "AD)" in a half-width header column, and a receipt breaking *after a hyphen*,
   which a no-break space does not prevent.
4. **The Edit tool rewrites escape sequences.** A ` ` written into an `Edit` became a raw invisible character in
   the file, and a trailing space in a bulk replace was trimmed. Write a patch script through `Write` when a change
   carries escapes or significant trailing spaces.

Tests: Domain 946, Application.UnitTests **1652** (+14), Infrastructure.UnitTests 13, Api.IntegrationTests **46** (+3),
Angular **793** (+8). `dotnet build`, all four .NET suites (Docker up, one at a time), `ng build` and `ng test` are green.

---

## 0. The rule's source, read first

The kickoff asked for the rule's source before building. **The primary texts require the dates but name no
calendar:**

| Source | What it says about the date |
|---|---|
| VAT Rules 2053, **Schedule 5** (Rule 17 tax invoice form), Law Commission text p. 62 | Two fields: कारोवार मिति (transaction date) and विजक जारी भएको मिति (invoice issue date), each a blank `/  /`. No calendar named |
| VAT Rules 2053, **Schedule 5ख** (Rule 17(1क)), p. 65 | The same two fields |
| VAT Rules 2053, **Schedule 6** (Rule 18 abbreviated tax invoice), p. 66 | One मिति. No calendar named |
| VAT Rules 2053, Rule 20 | A credit or debit note carries its date of issue. No calendar named |
| VAT Rules 2053, **Rule 26** | The tax period is one month "as per Bikram Era": the **only** BS reference found |
| IRD **Electronic Billing Procedure 2074** (4th amendment), Annex 5 | Materialised-view field `Bill_Date`. No calendar named |
| Same procedure, Annex 6 (front-end invoice formats: Tax Invoice, Abbreviated Tax Invoice, Invoice) | "Transactions Date" **and** "Invoice Issue Date" on all three. No calendar named |
| IRD Computerized Invoicing Procedure 2072 | The invoice format follows the VAT Act and Rules. No calendar named |

**Practitioner guides state the convention outright, and cite nothing for it.** The user found two:
- [Union Nepal, "VAT bill in Nepal"](https://www.unionnepal.com/vat-bill-in-nepal) lists the Bill Date as the
  "date of transaction in BS (Bikram Sambat) format (e.g. 2081/12/15)" among the mandatory fields, and the VAT
  register's date "in BS format". Its only legal reference is the VAT Act 2052 in general. It never mentions AD.
- [MISAC, "IRD VAT compliance"](https://mis.ac/articles/blog/ird-vat-compliance-nepal.php) gives the tax invoice's
  "transaction date in Bikram Sambat", and a sales register and purchase register each with a BS date. It cites no
  section, rule or directive.

So the convention is consistent across practice: bills **and the registers behind the return** are dated in BS. It
rests on convention and on the BS tax period (Rule 26: a bill's date must place it in a BS month), **not on a quoted
clause**. The user decided knowing this. The research also found a real statutory gap: **the forms ask for two dates
and our bills printed one.**

Sources:
- [VAT Rules 2053, Law Commission text](https://nepalre.com.np/public/uploads/download/64b6c0d7da380c66cfbb7cc4d88f4b2a.pdf) (Schedules 5, 5ख and 6, pp. 62–66)
- [Electronic Billing Procedure 2074, 4th amendment](https://bakertilly.com.np/tax-portal/notices-and-circulars/electronic-billing-procedure-2074-4th-amendment) (Annexes 5 and 6, pp. 15–17)
- [Procedure Related to Computerized Invoicing 2072](https://pioneerlaw.com/procedure-related-to-computerized-invoicing-2072-2015/)
- [VAT Rules 2053, English text](https://legaladvisorsnepal.com/value-added-tax-rules-2053-1996/) (Rules 17, 18, 20 and 26)

## Live read, 2026-10-09 (*Hamro Samaan*, its last trial day), read only

The user signed in, and the ERP was entered through the organisation list's *Open Tigg* hand-off (captured
`window.open` URL, not recorded).

1. **The viewer's calendar was AD** (the invoice list shows `28-09-2026`).
2. **The printed invoice is AD.** The print preview of `INV0001/HO/83-84` shows *Invoice Date* `24-09-2026`,
   *Transaction Date* `24-09-2026` and *Due Date* `24-09-2026`. **The vendor prints both statutory dates.** It
   still heads an approved invoice *ESTIMATE BILL* with *Copy of Original (1)*, phase 59's defects 3 and 4.
3. **The print request names no calendar.** The `collection-report-html` claims carry `is_date_bs` only on
   statement date ranges, so the bill's calendar comes from a stored setting.
4. **That setting is per template.** Configurations > Printing Templates > Sales Invoice offers six layouts:
   Standard (the default), Modern, Traditional, Minimal, Retail and Classic. Each layout's editor has a **Date
   System** section with **AD | BS**, and **AD was selected** on the default template. No session has ever written
   a template (no phase 59–70 doc records one), so **AD is the vendor's default on a new tenant**.
5. **The bundle offers a third value, "Both"**, only for a template newly added from the library
   (`isNewTemplate ? [AD, BS, Both] : [AD, BS]`, chunk `36.a984690d`).
6. The editor's preview uses fixed sample data, so switching the radio changes nothing on screen. The editor was
   left by navigating away, without Save.

---

## 1. What shipped

**Server: one formatter, one choke point.**
- `Application/Common/Formatting/BillDate`: `Format(date)` gives `2083-04-16 BS (2026-08-01 AD)`, and `2040-01-01 AD`
  outside the BS table (still labelled, never a guessed BS date). `Bs(date)` and `Ad(date)` are the halves.
- `TaxDocuments.Applies(type)` (beside `CountedPrints`): Invoice, Credit Note, Debit Note.
- `PrintDocumentQueryHandler.FormatDate(request, date)`: `BillDate` for a tax document, `RequestCalendar` for any other
  type. Every one of the handler's eight date calls goes through it.
- `PrintableDocumentDto` gained `DateLabel` ("Date"; "Transaction Date" on a tax document) and `IssueDateText` (the
  Nepal day of `ApprovedAt`; null on a draft and on an abbreviated tax invoice). `DocumentPdfRenderer` prints
  `{DateLabel}: …` and, under it, `Issue Date: …`.
- A tax document's footer drops the "Dates shown in Bikram Sambat (BS)" disclosure: each date is labelled inline.
- A credit note's invoice is now two header fields, **Against Invoice** (the number) and **Invoice Date** (Decision G).
- The email job renders its attachment through the same handler, so the emailed PDF is fixed by the same change.

**Registers.** `ReportSpreadsheetExporter`'s Sales, Purchase, Sales Return and Purchase Return Register exports (and,
through the same methods, the two migrated registers) write `Date (BS)` and `Date (AD)` as text columns. Each total row
moved right with its column. The Sales Register's Export Declaration Date is one `BS (AD)` cell.

**Till.** `NepaliDatePipe` gained a third argument, `calendar: 'preference' | 'bill'`, and a `'time'` mode.
`toBillDisplay` renders `16-04-2083 BS` and `(01-08-2026 AD)` on two lines, each date tied to its label by a no-break
space. The sale receipt prints Date, Issue Date (a full tax invoice or a non-VAT seller's invoice) and Time (the time
alone). The refund receipt prints Date, Issue Date, Bill Date and Time. Both print "Printed … by …" in the bill
calendar. The receipt's label cells do not wrap, and its value cells are `white-space: pre-line`.

**Guards.**
- `BillDateSweepGuardTests` (C#): `RequestCalendar.Format` appears once in the print handler, inside `FormatDate`, and
  `FormatDate(request, ` appears at least eight times; each statutory register export uses the two date columns and
  never a bare `(object?)r.Date`.
- `pos-receipt-dates.spec.ts` (Angular): both receipts with the toggle at AD and at BS; the abbreviated invoice's one
  date; and a source guard that every `nepaliDate` in the two receipts names `'bill'`, or is a time alone.

## 2. Scope decisions

**A. A bill's date is a statutory field, and the statute does not name the calendar.** The user's rule stands on
practice (§ 0), so it is recorded as a product decision that agrees with the law. **We diverge from the vendor**, whose
bill follows a per-template AD/BS switch that defaults to AD: a tax document here has no switch at all. Phase 49's rule
(a chosen divergence owes a surface) is met by the paper itself, where every date says "BS" and "AD".

**B. BS first, AD beside it (the user).** The vendor's "Both" is the same choice. The AD half serves a reader who does
not use BS, such as an export buyer. Each medium keeps its own date order: the PDF uses `RequestCalendar`'s sortable
`yyyy-MM-dd`, like every server-rendered date; the receipt uses the screen's `dd-MM-yyyy`, like everything else at the
till.

**C. Both statutory dates (the user).** The transaction date is the document's `Date`. The issue date is the Nepal day
of `ApprovedAt` (`NepalTime.LocalDate`): approval is when the bill gets its number, the same moment the vendor's
"Invoice Issue Date" marks. A draft has not been issued and prints no issue date. **An abbreviated tax invoice keeps one
date** on both the receipt and the PDF, because Schedule 6 has one. A till sale is created approved, so its two dates
are always the same day; the full receipt prints both anyway, because the form lists both.

**D. "A bill" is a tax document (the user).** Invoice, Credit Note and Debit Note (VAT Rules 17, 18 and 20) on paper
and by email, and the till's sale and refund receipts. Every other printable type keeps following the toggle,
including Payment (a money receipt), Quotation and the restaurant estimate. So do screens, the credit-note
picker's error messages (they are text on a screen, not a bill), and the email body's `$[DATE]$` token. That token
stays AD as phase 30 confirmed against the vendor; the bill is the attachment, and it is fixed.

**E. Registers: two columns in the export; the grids keep the toggle (the user).** The `.xlsx` is the record a return is
filed from, and its BS column is the one practitioners name. Both columns are text in `yyyy-MM-dd`, so each still sorts.
A date outside the BS table leaves the BS cell empty rather than guessing. Annex 5, Annex 13 and the VAT Summary are
period rollups, not per-bill registers, and were not changed (§ 5).

**F. One choke point per medium, guarded.** Threading a "bill" flag through every builder would have left eight places
to forget it. `FormatDate` decides by document type in one line, and the guard counts the calls.

**G. The invoice a credit note names is two fields, number and date.** The first E2E read `Against Invoice: 0001 dated
2083-04-16 BS (2026-08-01` / `AD)` out of the PDF: the header's half-width column wrapped inside the parenthesis, and a
real number (`INV0001/HO/83-84`) would wrap sooner. Rule 20 asks for the number and the date, and two fields each fit
on a line.

**H. The receipt prints a bill date as two lines, BS over AD.** The first browser pass wrapped "Issue Date" onto two
lines and broke the value after a hyphen (`(09-10-` / `2026 AD)`). A no-break space keeps a date beside its label but
cannot stop a break after a hyphen. So the pipe puts a line break between the halves, and the receipt's value cells
honour it (`pre-line`). Where white space collapses, such as the "Printed …" footer, the break reads as a space. The
Time row became the time alone, because a date in the toggle's calendar beside two bill dates would contradict them.

**I. No new permission keys.** Nothing new is reachable: the same documents print through the same routes under the
same View keys, and the register exports keep their report keys.

## 3. Bugs found and fixed

1. **A credit note's invoice date wrapped inside its parenthesis in the PDF** (Decision G). Found by reading the E2E
   PDF's text; every handler test passed, because the DTO's string was right.
2. **The receipt wrapped its label and broke a date after a hyphen** (Decision H). Found in the browser pass; the
   component spec passed, because `textContent` has no line width.
3. **A bulk `Edit` replace trimmed the trailing space** in `FormatDate(request, `, leaving `FormatDate(request,x)` at
   eight call sites. It compiled. The new guard, which counts `FormatDate(request, `, failed on its first run.
4. **The `Edit` tool turned ` ` escapes into raw no-break characters** in the pipe and the spec: invisible in an
   editor, and a test asserting on one is unreadable. Fixed with a byte-level patch through a script written by
   `Write`, which keeps escapes as typed.

## 4. Evidence

**Suites** (Docker up, one at a time): Domain 946, Application.UnitTests 1652, Infrastructure.UnitTests 13,
Api.IntegrationTests 46, Angular 793 (98 files). `ng build` green; initial bundle 652.58 kB.

**The guards bite.**
- C#: the formatting slip in Bug 3 failed `The_print_handler_formats_a_date_only_inside_FormatDate`.
- Angular: `pos-refund-receipt.ts` was backed up by hash, and its Bill Date changed to `| nepaliDate` (no `'bill'`).
  Three tests failed: the refund receipt at AD (`expected '01-08-2026'`), the same at BS (`expected '16-04-2083'`), and
  the source guard naming the file. The backup was restored and the hash matched (`1A49D7E6…5580`).

**E2E, ERP half** (fresh organization `ad1b7b9f-8199-4f13-a7eb-630dac3db962`, VAT-registered; seeded through the API).
- A service invoice dated 2026-08-01 was approved on 2026-10-09; a price-adjustment credit note dated 2026-08-05 names
  it; a standalone debit note is dated 2026-08-03.
- The invoice PDF printed with **no `X-Calendar` header** (the toggle at AD), read back with PyMuPDF:
  `Transaction Date: 2083-04-16 BS (2026-08-01 AD)`, `Issue Date: 2083-06-23 BS (2026-10-09 AD)` and
  `Printed by … on 2083-06-23 BS (2026-10-09 AD) 14:29`.
- The same with `X-Calendar: BS`: identical dates, and no "Dates shown in Bikram Sambat" footer.
- Credit note: `Transaction Date: 2083-04-20 BS (2026-08-05 AD)` and `Invoice Date: 2083-04-16 BS (2026-08-01 AD)` on
  one line. Debit note: `Transaction Date: 2083-04-18 BS (2026-08-03 AD)` and an Issue Date.
- The Sales Register `.xlsx` (2026-08-01 to 2026-08-31, toggle at AD) has `Date (BS)` and `Date (AD)` headers and the
  invoice's row as `2083-04-16` / `2026-08-01`.
- **In SQL:** the three bills' `Date` values are 2026-08-01, 2026-08-05 and 2026-08-03, and their `ApprovedAt` values
  are 2026-10-09 08:44 UTC, whose Nepal day (UTC+05:45) is 2026-10-09 = 2083-06-23 BS, the printed issue date.
  `sales.InvoicePrints` holds 1 and 2 (`Pdf`); `sales.CreditNotePrints` holds 1 and 2 (`Pdf`). The 403 below wrote
  nothing.

**403-not-404.** `POST /print/Invoice/{nonexistent}` is **404** as Admin. After the same user was moved to a custom
role holding only `Sales.CreditNote.View`, it is **403 naming `Sales.Invoice.View`**, beside a **200** on
`POST /print/CreditNote/{id}` in the same run. The membership was moved back to Admin in SQL.

**E2E, till half** (fresh organization `c63d4040-c656-4b44-8cb3-762992b444e4`, POS Retail, abbreviated invoices
enabled). One session holds a named-customer sale (0001, Tax Invoice), a walk-in sale (0002, Abbreviated Tax Invoice)
and a refund of one item of 0001 (credit note 0001), each printed once.

**Browser pass** (`erp-web-ssl`, cookie transplanted from a script without the token entering the transcript; the
session page showed `opened 09-10-2026 14:32`, so the toggle was at AD).
- **Keyboard:** focus started on sale 0002's Reprint; Tab reached Refund 0002, the bill link 0001, then Reprint 0001,
  with a visible focus ring; Enter reprinted it (count 2). The receipt read `Date | 23-06-2083 BS (09-10-2026 AD)`,
  `Issue Date | …same…`, `Time | 14:32`, with "COPY OF ORIGINAL · printed 2 times" top and bottom.
- **The paper:** after Decision H, the refund receipt prints each of Date, Issue Date and Bill Date as `23-06-2083 BS`
  over `(09-10-2026 AD)`, labels on one line.
- **Abbreviated:** sale 0002's receipt prints a single Date and no Issue Date.
- **Network:** every request after the reload was 200. The console's one 404 was this session's first, failed attempt
  to fetch the temporary cookie file.

## 5. Left open, and for later phases

- **A tax advisor's word on the calendar.** The statute names none (§ 0). If IRD ever specifies one, `BillDate` and
  `toBillDisplay` are the two places to change.
- **Printing templates** (the roadmap item): no per-type "Date System" may switch BS off on a tax document. The vendor's
  switch is exactly what we chose not to copy.
- **The money receipt** (a Payment's PDF) is not in the tax-document set and follows the toggle. If the user counts it
  as a bill, it is one line in `TaxDocuments`.
- **Annex 5, Annex 13 and the VAT Summary exports**, and the on-screen register grids, follow the toggle (Decision E).
- **The email body's `$[DATE]$`** stays AD (phase 30's vendor-confirmed choice). The attachment is BS.
- **The PDF does not use no-break spaces.** Its header fields fit on one line after Decision G. A longer field would
  need the same treatment the receipt got.
- Carried unchanged: discount and credit on a restaurant bill, per-part customers, phase 70 § 5, phase 69 § 5,
  phase 67 § 5, phase 66 § 5, phase 63 § 5, phase 62 § 5.

## Writes made to the vendor tenant

None. The template editor's Date System radio was clicked to BS and the editor abandoned without saving.
