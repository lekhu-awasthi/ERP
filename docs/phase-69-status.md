# Phase 69 — A credit note names its invoice: a picked one is capped, a typed one is older than the system

## TL;DR

**Every ERP credit note can now name the tax invoice it relates to, and a VAT-registered seller cannot approve
one that does not** (VAT Rules Rule 20(1)(e): the number and date of the tax invoice). A note converted from an
invoice already names it. A standalone note now names one of two ways: **an invoice picked from this system**,
which makes it a *price adjustment* (any lines, no stock movement, held to the invoice's customer, currency,
location and date), or **an invoice issued before the system**, typed as a number and a date that must be older
than the tenant's first invoice here. The ERP note also takes the **Reason** the till's refund already stored, and
the PDF prints "Against Invoice: … dated …" and the reason for every kind of note.

The plan found two bugs, both fixed with a failing test first: **every conversion template dropped the source's
currency** (a USD invoice's credit note arrived in NPR at rate 1), and **an edit of a converted draft skipped the
line caps** that Create enforced (credit notes and debit notes alike).

| | |
|---|---|
| Live read taken | **yes**, 2026-10-08, *Hamro Samaan* (1 trial day left), ERP side, read-only. The vendor **requires** an invoice on every note ("Invoice Ref# Name is required"), offers a picker of the customer's approved invoices that no note names yet, and picking one turns the note into a return of it. One Save was pressed with the Customer empty so the client's own validation answered; no request was sent. Appendix in `erp-module-scan.md` |
| Migration | `Phase69CreditNoteInvoiceReference`: `sales.CreditNotes.AgainstInvoiceId` (Restrict FK to Invoices, indexed), `AgainstInvoiceNumber` nvarchar(50), `AgainstInvoiceDate` date. All nullable; no backfill (a conversion names its invoice through `ReferrerId`) |
| New endpoint | `GET /credit-notes/creditable-invoices?contactId=&locationId=&search=&page=&pageSize=&excludingCreditNoteId=` (the picker) |
| Changed endpoints | Credit note create/update take `againstInvoiceId`, `againstInvoiceNumber`, `againstInvoiceDate`, `reason`; the detail returns them plus `relatedInvoice {id, code, date, grandTotal, currencyCode}`; the four conversion templates return `currencyCode`, `exchangeRate` |
| New permission keys | **none**: the picker rides `Sales.CreditNote.Create` (Decision G) |
| Changed screens | the credit-note form (an *Against invoice* fieldset, *Reason*, the invoice shown and linked once saved; currency locked while an invoice is picked or converted), and the four conversion targets (Invoice, Purchase Bill, Credit Note, Debit Note) now open in the source's currency |
| Initial bundle | **652.58 kB**, unchanged, against 680 kB |

**Four things worth carrying forward.**

1. **A statutory reference has two shapes, and each needs its own guard.** A picked invoice is capped by value:
   every non-void note against it (returns and adjustments, drafts included) never credits more than its total
   or more than its VAT. A typed one has nothing to cap against, so it must be **older than the first invoice
   here**; otherwise typing a number would be a way round the picker and the cap.
2. **Storing the second link only where the first is absent keeps one fact in one place.** A conversion names its
   invoice through `ReferrerId` and leaves `AgainstInvoiceId` null; `CreditNote.RelatedInvoiceId` reads either,
   and every query goes through one expression (`CreditNoteInvoiceReferences.RelatesTo`), so the void guard, the
   value cap and the picker count a price adjustment wherever they count a return.
3. **A comment saying a field "already" rides a flow is a claim, not a check.** Phase 35a's comments on four
   template DTOs said the conversion carried the currency "verbatim"; none did. The fix was a sweep whose N was
   derived (seven templates, three already right).
4. **Create was guarded and Update was not, for 60 phases.** A converted draft edited through the API could return
   five of two units. The re-check excludes the draft's own saved lines, which is also what the picker's
   `excludingCreditNoteId` does, so a draft is never counted against itself.

Tests: Domain **907** (+10), Application.UnitTests **1607** (+32), Infrastructure.UnitTests 13,
Api.IntegrationTests 43, Angular **769** (+7). `dotnet build`, all four .NET suites (Docker up, one at a time),
`ng build` and `ng test` are green.

---

## 1. What shipped

**Domain**

- `CreditNote.AgainstInvoiceId`, `AgainstInvoiceNumber`, `AgainstInvoiceDate` and
  `SetInvoiceReference(invoiceId, number, date)`: draft-only, at most one of a picked id and a typed pair, a typed
  number and date both or neither (trimmed, at most 50 characters), and never on a conversion.
- `IsConversionFromInvoice`, `RelatedInvoiceId` (the conversion's referrer, else the adjustment's invoice) and
  `NamesAnInvoice` (any of the three).
- `SetReason` for an ERP note (draft-only, optional, trimmed, 500 characters; a till refund's is set when it is
  made and refused here).
- `EnsureNamesInvoiceFor(sellerIsVatRegistered)` and `MissingInvoiceReferenceMessage`: the Approve backstop.

**Application**

- `Sales/CreditNoteInvoiceReferences`: `RelatesTo(invoiceId)` (an expression, never a static call inside one);
  `ApplyAsync` (refuses each mismatch with a 409, a typed date on or after the first invoice with a 400 naming
  `AgainstInvoiceDate`, a reference on a conversion with a 400 naming `AgainstInvoiceId`); `EnsureConversionMatchesAsync`
  (currency, value); `EnsureWithinInvoiceValueAsync` and `CreditedAsync`.
- `CreditNoteInvoiceReferenceRules`: the shared validator rules (expressions only), used by both validators.
- Create and Update: the reason, the reference and the conversion checks, after the lines. Update re-checks a
  converted draft's line caps, customer and discount, excluding its own saved lines.
- Approve: 409 with `MissingInvoiceReferenceMessage` on a VAT-registered organization when the note names nothing.
- Void Invoice: refused while a non-void note relates to the invoice **either way**.
- `ListCreditableInvoicesQuery` (+ validator, handler): the customer's approved ERP invoices at the caller's
  locations and the form's, newest first, key-paged, each with `GrandTotal`, `CreditedTotal`, `RemainingTotal`,
  `CurrencyCode`, `ExchangeRate`; `ExcludingCreditNoteId` for the draft being edited.
- `GetCreditNoteQuery`: `AgainstInvoice*`, `Reason`, `RelatedInvoice`.
- `PrintDocumentQueryHandler`: "Against Invoice: X dated Y" for a return, an adjustment or a typed invoice;
  "Reason: …" when there is one (till refunds included).
- The four conversion templates (Quotation → Invoice, Purchase Order → Purchase Bill, Invoice → Credit Note,
  Purchase Bill → Debit Note) carry `CurrencyCode` and `ExchangeRate`. Debit Note Create and Update refuse
  another currency than the bill's (`PurchasingValidation.EnsureDebitNoteInBillCurrencyAsync`), and Update
  re-checks a converted draft's line caps.

**Api**

- `CreditNoteRequest` carries the four new fields (on the request record, phase 27b's rule); the picker route.

**Web**

- Credit-note form, standalone draft: an *Against invoice* fieldset (legend, a hint saying a VAT-registered
  business names it before approving, two native radios: *An invoice in this system* / *One issued before this
  system*). The first shows *Find by number* and an *Invoice* select whose options read "0001 · 01-10-2026 · NPR
  2,260.00 · 2,147.00 left", a fully credited one disabled; picking sets and locks the currency and rate. The
  second shows *Invoice number* and *Invoice date* (`app-bs-date-input`), and saving one without the other is a
  field error on the number. Changing the customer or the location clears the pick.
- *Reason* (optional, 500) on every ERP note; a till refund's still shows in its *Refunded at the till* section.
- A saved, approved or converted note shows *Against invoice* read-only: the invoice linked, with its date, or the
  typed number and date "(issued before this system)", or "None named".
- Invoice, Purchase Bill, Credit Note and Debit Note forms opened from a conversion take the template's currency
  and rate; the credit note's currency control is locked on a conversion.

## 2. Scope decisions

**A. Who must name an invoice, and when: VAT-registered tenants, at Approve (the user's choice).** Rule 20 binds a
registered person, and VAT registration is the test phase 67 already uses for the heading. Approve is when the
note is numbered and issued, so a draft may be saved without it; the vendor refuses at Save, for every tenant.
A non-registered tenant may name one or not.

**B. A standalone note naming an invoice is a price adjustment (the user's choice).** Today's standalone note
(any lines, no stock, no COGS) keeps its meaning and gains the invoice it relates to. It must be an approved,
non-till invoice of the same customer, in the same currency and at the same billing location, dated no later than
the note. The value cap counts every non-void note relating to the invoice; the VAT cap stops a note giving back
output tax that was never charged. A return (Convert to Credit Note) is unchanged except that it now also counts
toward the value cap, so a price cut followed by a full return at the original price is refused. The vendor's
alternative — picking converts — was offered and declined: a rate reduction cannot be expressed as a return.

**C. An invoice issued before the system is typed, and must be older than the first invoice here (the user's
choice).** The number and date print exactly as a picked invoice's do. "Older than the first approved or voided
invoice" is what makes the escape hatch narrow; a tenant that has issued nothing yet may type any date.

**D. The ERP note takes an optional Reason, printed when present (the user's choice).** Rule 20(1)(f) asks for the
details of the credit and CBMS's sales-return payload carries `reason_for_return`; the vendor's ERP form has no
such field. The PDF printed no reason before, not even a till refund's; it does now.

**E. Storage: one new link, set only where the existing one is absent.** `AgainstInvoiceId` is null on a
conversion, whose invoice is its `ReferrerId`; no backfill, no second copy to drift. The alternatives were one
column populated for every note (redundant with `ReferrerId` on returns) or a note *kind* reinterpreting
`ReferrerId` (fifteen readers treat it as "a return").

**F. The quantity caps stay on returns.** An adjustment gives no quantity back, so it never counts against a
line's remaining quantity; it counts against the invoice's value. Location equality is enforced for adjustments
only; a conversion's location was never constrained and is not now (cross-branch returns stay carried).

**G. Permissions, derived: no new key.** The picker shows an invoice's number, date and total — exactly what the
note will print — to someone allowed to create that note for that customer, so it rides
`Sales.CreditNote.Create` rather than `Sales.Invoice.View` (a clerk can raise a note without browsing invoices).
It is a list, so a location-scoped caller gets fewer rows, never a 403 (`ILocationFilteredQuery`).

**H. The PDF says it once, for every kind.** "Against Invoice: <code> dated <date>" for a return, a price
adjustment or a typed invoice, through `RequestCalendar`; "Reason: …" under it.

**I. The conversion currency sweep: N derived.** Seven conversion templates exist; Delivery Note and GRN already
carried the currency (phase 58), Production Journal has none. Four did not, and their four target forms now take
it. The two reversals refuse another currency on the server; Invoice and Purchase Bill do not (a new sale or bill
may legitimately be raised in a different currency — the template is the default, not a rule).

**J. Out of scope, with reasons.**

- **The Debit Note's statutory reference.** Rule 20 binds the issuer of a note for a change in price; a purchase
  return's statutory note is the *supplier's* credit note. Our Debit Note is the buyer's record. Its two bugs
  (currency, edit caps) were fixed here because they are the same bugs.
- **The Sales Return Register.** IRD fixes its columns, and they match ours.
- **The full-tenant export.** It is a per-line analytics union of invoices and notes, not the statutory record;
  adding the invoice there joins on two columns per row. It joins phase 61's carried export columns.
- **CBMS sync** (outside the sequence; needs IRD credentials). The note now carries both fields CBMS asks for.

**K. What the vendor does** (the live read, in full in the scan appendix). Reference No is required on every note;
it is free text with a picker beside it, loaded from
`/invoices?items=true&referred=false&contact_id=…&status=Approved`; picking copies the invoice's items and sets
`referrer_id`. Its `referred=false` hides an invoice once **any** note names it, so INV0001, credited 169.50 of
339.00, could not be credited again; here an invoice stays pickable until its value is used up, and a fully
credited one is listed, disabled, so a user searching for it learns why.

## 3. Bugs found and fixed

1. **Every conversion template dropped the currency.** Quotation → Invoice, PO → Bill, Invoice → Credit Note and
   Bill → Debit Note templates had no `CurrencyCode`/`ExchangeRate`, and their target forms left the currency
   signals at whatever they held (the base, on a fresh page). A USD invoice's credit note therefore credited
   22.60 *rupees* where the invoice had debited 3,005.80. Red first: the test drove the client's conversion flow
   with the template still handing back NPR and failed on the new currency guard; green once the template carried
   the invoice's currency. The E2E credits 1,502.90 against 3,005.80 for one of two units.
2. **An edit of a converted draft skipped every cap.** `UpdateCreditNoteCommandHandler` (and its debit-note
   mirror) never re-ran the line caps, the customer check or the discount check that Create ran; the form's
   locked fields were the only guard. Red first: with the re-check disabled, editing a converted draft to three
   of a two-unit line saved without error. The first version of that test used a one-line invoice, where the new
   value cap refused the edit for its own reason; it now uses two lines, so only the per-line cap can refuse.
3. **A patch script flipped fourteen files from CRLF to LF.** Python's text-mode `open()` reads CRLF as LF, so the
   script's `assert '\r\n' not in d` passed on a CRLF file and `newline='\n'` wrote it back LF. Git's
   `core.autocrlf` meant no commit noise; the working copies were restored to CRLF and git's warning list went
   to zero. Detect a file's newline from its bytes.

## 4. Evidence

**Fresh organization** `9ef0d069-39f6-45b1-b3dd-eaba882fce57` (*Phase 69 Traders*, VAT-registered, multi-currency
with USD activated), seeded through the API (`e2e69.py`), every status printed: two customers, a service product,
NPR invoices 0001 (Acme, 2 × 1,000 + VAT = 2,260) and 0003 (Zenith), USD invoice 0002 (Acme, 2 × 10 at 133).

**The flow**, all checks passed:

- The picker for Acme lists 0001 and 0002 only, with 2,260.00 remaining and USD at 133.
- A price adjustment of 113 against 0001 with "  Agreed price cut after delivery " is created, **approved on the
  VAT tenant**, and reads back with the invoice named and the reason trimmed.
- A standalone draft naming nothing: Approve **409** citing Rule 20.
- Naming Zenith's invoice for Acme: **409** "…another customer…". An adjustment of 2,260 against 0001: **409**
  "…only 2147.00 of invoice 0001's 2260.00 is left to credit." An NPR adjustment against the USD invoice:
  **409**. Picked and typed together: **400**.
- A typed invoice OLD-0042 dated 2026-06-15: **201**, approved. One dated 2026-10-05 (after the first invoice):
  **400** naming `AgainstInvoiceDate`.
- The USD template carries USD at 133; the conversion sent in NPR is **409**; sent in USD it is created and
  approved.
- Voiding invoice 0001: **409** while the adjustment names it.

**In SQL**: `sales.CreditNotes` rows 0001 (`Erp`, no referrer, `AgainstInvoiceId` set, NPR, the trimmed reason),
the refused DRAFT (nothing named), 0002 (`OLD-0042`, `2026-06-15`), 0003 (`ReferrerType = Invoice`, USD,
133.000000). Receivable on the USD pair: the invoice debits **3,005.80**, the one-unit return credits **1,502.90**.

**The PDFs** (read with the Read tool): the adjustment prints "Against Invoice: 0001 dated 2026-10-01" and
"Reason: Agreed price cut after delivery" beside the buyer's PAN; the typed note prints "Against Invoice: OLD-0042
dated 2026-06-15" and no reason line.

**Browser pass** (Browser pane, `erp-web-ssl`, the same organization):

- New note, Acme chosen: the picker reads "0002 · 02-10-2026 · USD 22.60 · 11.30 left" and "0001 · 01-10-2026 ·
  NPR 2,260.00 · 2,147.00 left". Picking 0001 locked the currency at NPR; a line at 200 + VAT (226.00) and a reason
  saved, read back with 0001 still selected and **2,147.00 left** (the draft does not count against itself), and
  approved: "Against invoice 0001 dated 01-10-2026", the reason disabled.
- The refused draft: Approve shows the Rule 20 message in the page's alert banner. Typed mode, a number with no
  date: Save marks *Invoice number* `aria-invalid`, described by "Type both the invoice's number and its date.",
  with focus on it. With 2026-06-01 it saved and approved: "OLD-0099 dated 01-06-2026 (issued before this
  system)".
- Invoice 0002 → *Convert to Credit Note*: the new note opens in **USD at 133, currency locked**, with no picker.

**Keyboard census** (real key events, focus read after each): Reference → Tab → *An invoice in this system* with
the `#0a58ca` 2px ring at 2px offset (`:focus-visible`) → ArrowRight selects *One issued before this system* →
Tab *Invoice number* → Tab *Invoice date* (Chrome walks the date's segments) → Tab *Reason*; Shift+Tab back to
the radios, ArrowLeft, Tab *Find by number*, Tab *Invoice* (ringed), ArrowDown picks 0002 and the currency becomes
USD 133, locked. Every control in the fieldset is labelled; the fieldset's legend names the radio group and its
hint is its description. At **375 px** there is no horizontal scroll and the radios stack.

**Phase 67's carried look at phone width**: on an approved, printed note the "Printed 1 time; the next is a copy"
note wraps onto the row under *Print*, legible, no overflow, still tied to *Print* by `aria-describedby`.

**403-not-404**: `GET /credit-notes/creditable-invoices?contactId=<nonexistent>` is **200** (an empty list) as
Admin, then **403** "…(Sales.CreditNote.Create)." from a custom role granting only `Sales.CreditNote.View`,
beside a **200** on `GET /credit-notes` from the same user in the same run. A list answers "nothing" rather than
404, so the control is the Admin's 200 on the same nonexistent id. The role was restored to Admin in SQL.

## 5. Left open, and for later phases

- **Older notes name nothing.** Standalone notes approved before this phase print their Reference as before; there
  is no backfill, because nothing records which invoice they meant.
- **The vendor's server-side rule** for a free-text Reference that names no invoice was not tested (it would take a
  save).
- **Two drafts racing for one invoice's remainder** can both fit separately and exceed together; the same is true
  of the line caps since phase 6. Approve does not re-check.
- **Cross-branch returns** (phase 63 § 5) stay carried; an adjustment must be at its invoice's location.
- **The export's invoice column**, with phase 61's carried service-charge and round-off columns.
- The other carried items are unchanged: the packaging fee, discount and credit on a restaurant bill, per-part
  customers (phase 68 § 5); the print race on SQL Server (phase 67 § 5); phase 66 § 5; phase 63 § 5; phase 62 § 5;
  real printing templates.

## Writes made to the vendor tenant

None. On *Hamro Samaan* the add form was filled client-side (customer, a typed "INV") and abandoned; one Save was
pressed with the Customer empty, which the client's own validation stopped before any request. The list showed the
same two notes afterwards. No credentials were entered and no token was read or recorded.
