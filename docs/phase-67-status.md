# Phase 67 — The ERP invoice and credit note on paper: the heading the bill is, and every copy counted

## TL;DR

**The ERP's own PDF of an invoice or a credit note now follows the two statutory rules the till has
followed since phase 62.** It is headed with what the bill is: Tax Invoice / कर बीजक, Abbreviated Tax
Invoice / संक्षिप्त कर बीजक, Invoice / बीजक, or Credit Note / क्रेडिट नोट. Every copy is counted on the
server, and every copy after the first is boxed *COPY OF ORIGINAL · printed N times* at the top and the
bottom. One bill has one count across the till, the ERP's Print button and an emailed PDF.

| | |
|---|---|
| Live read taken | **yes**, 2026-10-03, *Hamro Samaan* trial (ERP side). Two previews, then two prints, with the user's authorisation: the only write was the vendor's `print_count` on one invoice, 0 to 2 (`erp-module-scan.md`, "ERP invoice and credit-note print, read live") |
| Scope settled with the user | Phase 67 is this; **Mark as Take Away and item transfer are phase 68**, with the repricing question answered (roadmap) |
| Migration | `Phase67CountedPrints`, scaffolded and **not** hand-edited: `Medium nvarchar(20) NOT NULL DEFAULT 'TillReceipt'` on `sales.InvoicePrints` and `sales.CreditNotePrints`. The default is true of every existing row |
| New endpoint | `POST /print/{Invoice\|CreditNote}/{id}`: the counted PDF. The GET on those two types is now a **409** |
| New permission keys | **none**. The POST rides the document's own View key at its location, as the GET does |
| Changed screens | Invoice and credit note detail: "Printed N times; the next is a copy" beside Print, as the button's description |
| New bundled asset | Noto Sans Devanagari Regular and Bold (SIL OFL, licence beside them), embedded in the Api assembly for the PDF's Nepali headings |
| Initial bundle | **652.58 kB**, unchanged, against 680 kB |

**Four things worth carrying forward.**

1. **Every copy that leaves counts, whatever the medium.** The user chose this for email (Decision B): the
   first copy out is the original whichever way it went. So an emailed original and a paper original cannot
   both exist unmarked. The rows record the medium (till receipt, PDF, email) and nothing reads it.
2. **A GET does not write, and a GET that does not write cannot print these two types.** The counted print
   is a POST. The generic GET refuses an invoice or a credit note (409) rather than hand out an uncounted,
   unmarked copy. The other fifteen printable types are unchanged.
3. **One rule, one place.** The heading moved into the Domain (`InvoiceHeadings.For`) and the till's receipt
   and the PDF both call it. The print rows are the till's own tables, so one count per bill is a property of
   the schema, not of a reconciliation.
4. **The vendor counts its prints and marks none of them.** Its reprint said ESTIMATE BILL with no copy mark,
   on a VAT-registered tenant. It ties the heading to IRD enablement, which the user declined to follow
   (Decision A).

Tests: Domain **882** (+8), Application.UnitTests **1565** (+18), Infrastructure.UnitTests 13,
Api.IntegrationTests **39** (+9), Angular **755** (+5). `dotnet build`, all four .NET suites (Docker up, one
at a time), `ng build` and `ng test` are green.

---

## 1. What shipped

**Domain.**

- `InvoiceHeading` + `InvoiceHeadings` (`Domain/Sales/InvoiceHeading.cs`): the heading rule moved from the
  till's receipt command, with the English and Nepali words for each heading.
- `PrintMedium` (`TillReceipt` = 0, `Pdf`, `Email`).
- `InvoicePrint.Record` / `CreditNotePrint.Record` take a **required** medium. Only the till receipt is still
  till-only; a PDF or an email of an ERP document is recorded. A draft is refused with its own message.

**Application.**

- `IssueDocumentPrintCommand` (+ validator, handler) under `Printing/Commands/IssueDocumentPrint`. It writes
  the row, commits, then renders through `PrintDocumentQuery` with that row as its `Issue`. A lost race
  detaches its row and is a 409.
- `PrintDocumentQuery` takes an optional `DocumentPrintIssue`. Without one, an invoice or a credit note is a
  409 (`CountedPrints.Applies`).
- `PrintableDocumentDto` gains `TitleNepali` and `PrintedCopy` (number, printer, Nepal wall-clock time).
- The invoice builder titles by `InvoiceHeadings`; the credit note builder adds **Against Invoice: <number>
  dated <date>** when the note was raised from an invoice.
- `InvoiceDetailDto.PrintCount` and `CreditNoteDetailDto.PrintCount`, both required. `PosRefund` lost its
  default to make room, which the compiler swept: one construction site each.
- The till's two print handlers call `InvoiceHeadings` and record `PrintMedium.TillReceipt`; `PosReceiptTitle`
  is gone (same member names, so the JSON is unchanged).

**Api.**

- `POST /api/organizations/{id}/print/{documentType}/{documentId}` sends the command with `PrintMedium.Pdf`.
- `MediatorDocumentPdfRenderer` sends the command with `PrintMedium.Email` for the two counted types, so the
  email job's attachment is a counted copy.
- `DocumentPdfRenderer`: the Nepali heading under the English one (the only text naming the Devanagari
  family), the boxed copy mark in the header and the footer, and "Printed by X on <date> <time>".
- `PdfFonts`: registers the two embedded TTFs once, on first render.

**Web.**

- `PrintingService.printDocument` chooses the verb: POST for `COUNTED_PRINT_TYPES`, GET for the rest. No page
  changed its call. It also parses a blob error body back into ProblemDetails (§ 3, bug 1).
- The invoice and credit-note detail pages show the count beside Print (`aria-describedby` on the button) and
  bump it after a successful print.

**Tests.**

- `IssueDocumentPrintTests` (Application, 18): the heading in both languages and both registrations, the
  heading rule's four cases, original then copy, email counted, till + ERP one count for a sale and for a
  refund, the GET refused, draft / void / missing, the validator, the credit note's invoice link (and none
  for a standalone note), the detail DTOs' counts.
- `CountedPrintTests` (Domain, 8).
- `Phase67CountedPrintPdfTests` (Api, 9): the PDF's **text**, read back through `TestSupport/PdfText`. That
  covers the heading, the mark exactly twice, the printer, an uncounted document unchanged, every Nepali
  heading rendered under `CheckIfAllTextGlyphsAreAvailable`, and the email path routing.
- `printing.service.spec.ts` (Angular, 5).

## 2. Scope decisions

**A. The heading: VAT registration, not the vendor's IRD flag (the user's choice).**

Phase 62's rule, unchanged, now on the ERP PDF as well:

- A VAT-registered organization's invoice is a *Tax Invoice* (कर बीजक).
- It is an *Abbreviated Tax Invoice* (संक्षिप्त कर बीजक) when the till stored that at the sale.
- An unregistered organization's invoice is an *Invoice* (बीजक).
- A credit note is a *Credit Note* (क्रेडिट नोट), whatever the seller.

The vendor heads an approved ERP invoice *ESTIMATE BILL* on a tenant that is VAT-registered but not
IRD-enabled. That leaves a registered seller with nothing to hand a customer who needs input-tax credit.
Asked, the user chose to keep phase 62's rule everywhere. The rule now lives in the Domain, so the till and
the PDF cannot disagree.

**B. What counts: every copy that leaves, by any medium (the user's choice for email).**

The 2072 procedure (§6) allows one original and requires every reprint to say "copy of original" and how
many times it has been printed. The kickoff's question was whether an emailed PDF is a reprint. Three options
were put to the user:

- count the email;
- do not count it, but mark it once a paper original exists;
- the vendor's rule: Attach PDF is disabled until the invoice has been printed, on an IRD tenant.

The user chose to count it. The first copy out, paper or email, is the original, and every later one is
marked. The send job claims each `EmailSendLog` exactly once and a resend is a new row, so one send writes
at most one print row.

A send whose SMTP leg fails after the render has still counted, which errs the safe way: phase 62's
"counted on request". The row's `Medium` records how each copy left. Nothing reads it; it exists for the
procedure's print record.

**C. A GET does not write, so these two types print with a POST.**

A GET that wrote print rows would be unsafe under every cache, prefetcher and retry. A GET that did not
write would hand out an uncounted, unmarked copy. So for an invoice or a credit note:

- `PrintDocumentQuery` refuses (409) unless it carries a `DocumentPrintIssue`;
- only `IssueDocumentPrintCommand` supplies one, after committing the row;
- the Angular service chooses the verb, so no page can reach the uncounted route.

The other fifteen printable types are unchanged.

**D. One bill, one count.**

The ERP writes the till's own tables (`InvoicePrints`, `CreditNotePrints`) under the same unique
(organization, document, number) index. A till sale printed at the till and twice from the invoice page is
1, 2, 3. The Domain no longer refuses an ERP document; it refuses only a **till receipt** for one, because a
receipt is a layout, not a rule.

**E. Drafts and voids do not print.**

- A draft has no number until it is approved: 409, "Invoice is a draft…". The UI already showed Print on
  approved documents only; the API printed drafts until now.
- A voided document is no longer one to hand anyone: 409, as at the till.

**F. The credit note names its invoice.**

VAT Rule 20(1) asks a credit note for the number **and date** of the tax invoice it relates to. The vendor
prints the number only. A note raised from an invoice (`ReferrerType = Invoice`) prints *Against Invoice:
0001 dated 2026-10-03*. A till refund is one of these. A standalone note prints its Reference as before (§ 5).

**G. Who printed it, and when.**

The counted PDF's footer says "Printed by <name> on <Nepal date> <HH:mm>". The procedure's print record wants
it, and the vendor prints the same footer. The date goes through `RequestCalendar`, so it is BS when the
caller asked for BS.

**H. Permissions: no new key.**

The POST rides the document's own View key at its location (`ILocationScopedDocument`), exactly as the GET
did. A print is the document read onto paper. The E2E proves the route is gated, and gated per type (§ 4).

**I. Nepali needs a font, so one is bundled.**

QuestPDF's default font has no Devanagari. Noto Sans Devanagari Regular and Bold (SIL OFL) were downloaded
with the user's approval from the Noto project. They are embedded in the Api assembly
(`Printing/Fonts`, with `OFL.txt`) and registered once.

- Only the Nepali line names the family, so no other document's appearance changed.
- The copy mark stays English, as the till receipt has printed it since phase 62.
- The tests turn on `CheckIfAllTextGlyphsAreAvailable`, so a missing glyph throws rather than printing a box.

**J. The count is shown where Print is.** "Printed N times; the next is a copy" sits beside the button and is
its accessible description. Whoever prints knows before clicking that the paper will be marked. It is plain
text, not a tab stop.

## 3. Bugs found and fixed

1. **Every print refusal read "Could not print. Please try again."** A blob request's error body is a
   `Blob`, so `extractErrorMessage` found no ProblemDetails in it. That was true since phase 20d, and it would
   have hidden this phase's refusals: a draft, a void, and "printed somewhere else at the same moment, print it
   again". `PrintingService` now parses a blob error back into JSON, for every printed type. Pinned in
   `printing.service.spec.ts`.
2. **The test PDF reader garbled every glyph** on its first run. A range pattern applied to a `bfchar` block
   read two consecutive one-glyph lines as one range. Each section is now parsed only inside its own
   `begin…end`. A test-tool bug, recorded because the same reader is in the E2E.
3. **"Every copy of a invoice is counted"**, the GET's refusal, found reading the E2E's output: the article
   came from a lowercased label. It now says "this invoice".

## 4. Evidence

**Fresh organization** *P67 Counted Prints 093517* (VAT-registered, Retail till at head office), seeded through
the API (`e2e67_seed.py`): every status code printed, all 2xx. Then the flow (`e2e67_flow.py`) and the
permission proof (`e2e67_perm.py`).

**Prints, as `sales.*Prints` holds them after the run:**

| Document | Rows (number, medium) | Read from the PDF |
|---|---|---|
| ERP invoice 0001 | 1 Pdf, 2 Pdf, 3 **Email**, 4 Pdf (the Biller) | #1: TAX INVOICE कर बीजक, no mark. #2: **COPY OF ORIGINAL · printed 2 times**. #4: printed 4 times, "Printed by Hari Biller" |
| ERP credit note 0001 | 1 Pdf, 2 Pdf, 3 Pdf (the browser's Enter) | CREDIT NOTE, **Against Invoice: 0001 dated 2026-10-03**; #2 marked top and bottom |
| Till sale 0003 | 1 **TillReceipt**, 2 Pdf, 3 Pdf | The till receipt said print 1, TaxInvoice; the ERP's prints were copies 2 and 3 |
| Till refund (credit note 0002) | 1 **TillReceipt**, 2 Pdf, 3 Pdf | Against Invoice: 0003; copies 2 and 3 |
| Draft invoice, voided invoice 0002 | none | 409 each |

The email was sent (`EmailSendLogs.Status = Sent`), and its render wrote row 3 (`Email`) on the invoice.

The credit note's PDF was also looked at, not only parsed: क्रेडिट नोट is correctly shaped. The text
reader returns the vowel sign in glyph order, so a Devanagari assertion on extracted text is weak, and the
API test asserts the font instead.

**Refusals:**

| Request | Status |
|---|---|
| `GET /print/Invoice/{0001}` | 409, "printed with POST /print/Invoice/{id}" |
| `GET /print/CreditNote/{0001}` | 409 |
| `POST` on the draft | 409, "a draft. It has no number until it is approved" |
| `POST` on the voided invoice | 409, "has been voided" |
| `POST` on a nonexistent invoice | 404 |
| `POST /print/Quotation/…` | 400 naming `DocumentType` |

**Permission proof** (a throwaway user, so the run's own login was never moved):

| User and request | Status |
|---|---|
| Admin, `POST /print/Invoice/{nonexistent}` | **404** "Invoice not found." |
| *Quoter* (`Sales.Quotation.View` + `Tenancy.Subscription.View`), same request | **403** naming `Sales.Invoice.View` |
| *Quoter*, `GET /quotations` | 200 |
| Moved to *Biller* (`Sales.Invoice.View` alone), the real invoice | 200, copy 4 |
| *Biller*, the credit note | 403 naming `Sales.CreditNote.View` |

**Browser pass** (`erp-web-ssl`, cookie transplant):

- The credit note page showed "Printed 2 times; the next is a copy" as the Print button's
  `aria-describedby`.
- Print is in the tab order between the breadcrumb and Send Email; the note is not a stop.
- Focused and pressed with a real **Enter**, it printed. The note became "Printed 3 times", focus stayed on
  Print, and no error showed. SQL gained row 3.
- The invoice page showed "Printed 4 times".
- Screenshots timed out with the pane hidden, so the evidence is page text and SQL.

**Suites:**

| Suite | Result |
|---|---|
| Domain | 882 |
| Application.UnitTests | 1565 |
| Infrastructure.UnitTests | 13 |
| Api.IntegrationTests | 39 (Docker 28.0.4 up, run alone) |
| Angular | 755 |
| `ng build` | 652.58 kB |

## 5. Left open, and for later phases

- **A standalone ERP credit note names no invoice.** Rule 20(1) asks for one. The form could require an
  invoice reference, or a standalone note could be refused on a VAT-registered tenant. That is a product
  decision.
- **The print race** on both tables still relies on the unique index, which InMemory does not enforce. It
  was not raced against SQL Server here, as in phases 62 and 63.
- **The emailed PDF's own text was not read back.** The dev environment sends through SMTP (to a reserved
  `.test` address) rather than the file drop. The count row is in SQL and the routing is pinned by a test.
- **IRD enablement** stays unmodelled by choice (Decision A). If a tenant ever needs the vendor's behaviour,
  it is a setting on top of `InvoiceHeadings`, not a second rule.
- **The note beside Print at phone width** was not looked at, because screenshots timed out with the pane hidden.
- Carried unchanged: phase 62 § 5 (serials at the till, the barcode index), phase 63 § 5, phase 66 § 5.

## Writes made to the vendor tenant

Two prints of INV0001/HO/83-84 on *Hamro Samaan* (`print_count` 0 to 2), with the user's authorisation, to
see which request counts and what a reprint says. Nothing else.
