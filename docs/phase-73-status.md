# Phase 73 — Printing templates: three fixed layouts, show/hide toggles, the custom fields a bill prints

## TL;DR

**A printing template now changes the page.** Since phase 20d a template was a name and a default flag, and every
document printed one shared layout with "Template: name" in the footer to prove the wiring. Now a template picks one
of **three fixed layouts** (Standard, Traditional, Minimal), **hides** what it does not want printed, chooses **which
custom fields** print, adds **a footer line** and can use **compact margins**. The Printing Templates page has a
layout editor with a **preview** of the unsaved settings over sample data. The user's decisions (2026-10-09), each
the recommended option:

- **Three layouts** (Decision A): *Standard* is the frame as it was, *Traditional* rules every block (the familiar
  Nepali bill), *Minimal* is flush left with no logo. Each serves all seventeen printable types, because the
  renderer never switches on `DocumentType` (phase 27b).
- **Show/hide only, no renamed labels** (Decision B). On a tax document, the seller's address and PAN and the buyer's
  address **cannot be hidden** (VAT Rules Schedule 5). The title, number, both dates, buyer's PAN and copy mark are
  not toggles at all.
- **One template per type**, organization-wide (Decision C). The vendor's per-location choice is a recorded
  divergence.
- **No date-system option anywhere** (Decision D). Tax documents stay BS with AD beside it (phase 71).

**Read live first** (Hamro Samaan, its last trial day, read only, 89 GETs and nothing refused, § Live read): the
vendor's layouts are **drawing code over one settings shape**, every leaf `{display, label}`. Custom fields are one
checkbox per field. The selection is per type, channel and location. Its tax heading, PAN and dates are free labels
a tenant can rename or hide.

| | |
|---|---|
| Live read taken | **yes**, read-only, 2026-10-09 (§ Live read) |
| Migration | `Phase73PrintingTemplateLayout`: five columns on `configuration.PrintingTemplates`. **Hand-edited**: the scaffold defaulted `Layout` to `''`, which `Enum.Parse` refuses on read; it is `'Standard'` |
| New endpoints | 3 under `/{org}/configuration/printing-templates`: `PUT /{id}/layout`, `POST /preview` (a PDF; writes nothing), `GET /elements` |
| Changed print output | every document: a **Unit** column, the **HS Code** column when a line has one, **Amount in Words** (NPR only); the "Template: name" footer line is gone |
| New permission keys | **none**: `Configuration.PrintingTemplate.Manage` saves, `.View` previews and reads the element table |
| Changed screens | Configurations > Printing Templates: a *Layout* button per row opens the editor |
| Initial bundle | **652.58 kB**, unchanged (the configuration page is lazy) |

**Four things worth carrying forward.**

1. **A template stores what it hides, never what it shows.** An empty set means everything. That made every new
   column's default true of every existing row, so no backfill was needed. It also means an organization with no
   template prints through `PrintingTemplateSettings.Default`, the same values.
2. **One pure function applies a template.** `PrintTemplateApplier` runs on the finished DTO for a real print and for
   the preview, so the preview cannot disagree with the bill. It also **re-checks** `PrintElements.CanHide`, so a row
   written by any other door still prints the seller's PAN.
3. **The browser draws its checkboxes from the server's table** (`GET /elements`), never from a copy. The screen
   cannot offer to hide what the save would refuse (phase 39: one rule in one place).
4. **Adding columns to a table changes every column's width.** The unit and HS code columns made money wrap
   ("11,000.0 / 0") in ruled cells. Only looking at the PDF showed it; the text extraction read it as two lines and no
   test failed.

Tests: Domain **978** (+19), Application.UnitTests **1682** (+17), Infrastructure.UnitTests 13, Api.IntegrationTests
**52** (+6), Angular **818** (+7, 100 files). `dotnet build`, all four .NET suites (Docker up, one at a time),
`ng build` and `ng test` are green.

---

## Live read, 2026-10-09 (*Hamro Samaan*, its last trial day), read only

The browser pane was still signed in to the vendor's portal and the tenant still opened ("0 days remaining"). The ERP
was entered through *Open Tigg*'s hand-off, captured (`window.open` replaced) and followed in the same tab; the hand-off
URL's token was never printed. Before anything was clicked the page's `XMLHttpRequest` and `fetch` were wrapped to log
every request and **refuse anything that was not a GET**: **89 API requests, all GETs, none refused.** Configurations >
Printing Templates and four layout editors were opened; nothing was saved (the editor was left by navigating away).

1. **A template is a fixed layout plus one settings object.** `GET /printing-templates?source_collection=Invoice` returns
   the tenant's copies of the library layouts. Each carries `template_fixed_name` ("Invoice Standard"),
   `thumbnail_name`, `fields` (the editor sections it offers) and `current_configuration` / `default_configuration`
   (identical on an untouched copy). **The layouts differ in rendering code, not in data**: Standard, Modern,
   Traditional, Minimal and Classic share one configuration shape key for key; only the sections offered differ.
2. **The configuration**, every leaf `{display, custom_label, label}`:
   - `organization`: name, address, phone, email, PAN, website, *location name*, *location address* (the last two off
     by default), with a `_sorts` order;
   - `contact` ("Customer Detail:"): label, name, address, phone, email, PAN, with `_sorts`;
   - `document_detail`: document title ("Tax Invoice"), entry id ("Invoice No:"), entry date ("Transaction Date:"), due
     date, reference, payment mode;
   - `item_line_table`: line no, item code (**off** by default), HS code, item name, item description, quantity, UOM,
     rate, discount, tax, amount;
   - `total`: subtotal, discount, non-taxable total, taxable amount, VAT, net total, **amount in words**;
   - `signature_line`: prepared by / approved by, each with an optional printed name;
   - `footer`: notes, terms and conditions, page number, free footer content;
   - `printing_info`: printed by, print date, print time, print copies, header on every page;
   - `formatting`: sizes for organization name / client name / title, table header background, alternate-row shading;
   - `logo`: display, position (left/right), size (S/M/L); `margin`: four margins in cm and a compact mode;
   - `positioning` (**Standard only**): which block sits in each quadrant (top-left organization, top-right logo,
     bottom-left contact, bottom-right document detail);
   - `date_system`: **AD | BS** (phase 71 § Live read; the Classic preview prints both, `22-09-2023 / 05-06-2080`);
   - `custom_fields`: `null`, or a list of `{source_id, display}` — **one checkbox per active custom field of that
     document type**, no label of its own (read from the editor chunk; this tenant has no custom fields, so the
     section says "No any custom field").
3. **The previews** (server-rendered HTML from fixed sample data, `mock-collection-report-html`):
   - *Standard*: organization top-left in bold, logo top-right, customer block bottom-left, document details
     right-aligned bottom-right, shaded table header with alternate-row shading;
   - *Modern*: lighter type, rules under the header blocks, details as a two-column label/value list;
   - *Traditional*: **every block ruled** — organization box, customer | details split box, gridded line table, totals
     in a ruled column beside amount-in-words;
   - *Minimal*: Standard without the logo section or shading;
   - *Classic*: **centred** organization header (our current shape), "Bill To:", custom fields as one row between the
     header and the table (`country: Nepal`), gridded table, totals ruled beside Notes;
   - *Retail*: an 80 mm receipt (`spacing.paper_width: 80`), its own shape (three footer lines, *truncate item name*,
     table no, change).
4. **Which types get which layouts** (both channels, ERP and POS, hold a copy each):

   | Types | Layouts offered |
   |---|---|
   | Sales Invoice, Credit Note | Standard, Modern, Traditional, Minimal, Retail, Classic |
   | Sales Order | Standard, Modern, Traditional, Minimal, Retail |
   | Quotation, Purchase Order, Purchase Bill, Debit Note | Standard, Modern, Traditional, Minimal |
   | Customer/Supplier Payment, Quick Receipt/Payment, Expense, Journal Voucher, Cash Transfer | Standard only (toggles) |
   | Production Order, Production Journal, Bill of Material | Standard only, four sections (no formatting/logo/margins) |
   | Delivery Note, GRN, Warehouse Transfer | one user-defined "A4 (Custom)" template, and *Create Blank New Custom Template* (an HTML designer) |

5. **One template is selected per type, per channel, per location.** `GET /printing-template-select?location_id=…`
   returns `{source, channel, printing_template_id}` rows; the gallery has a *Location* picker defaulting to
   "Default" (the all-zero id). Clicking a tile selects it (the check moves); the pencil opens the editor; a
   non-selected copy can be deleted.
6. **The tax-document heading is a free label** (`document_title.label: "Tax Invoice"`), as is every other label: the
   vendor lets a tenant rename or hide the statutory title, PAN and dates. Combined with its AD default (phase 71) and
   *ESTIMATE BILL* heading (phase 59 defect 3), the vendor's template is not where the statute is kept.

---

## 1. What shipped

**Domain.**
- `PrintLayout` (Standard 0, Traditional, Minimal).
- `PrintElement`, the seventeen things a template may hide, stored by **name**.
- `PrintElements`: which elements each type prints (`For`), which it may not hide (`Locked`, the tax documents' three
  Schedule 5 items), and `CanHide`. `TaxDocumentTypes` moved here, and phase 71's `TaxDocuments.Applies` reads it, so
  "the bills dated in BS" and "the bills whose seller cannot be hidden" are one list.
- `PrintingTemplate` gains `Layout`, `HiddenElements`, `PrintedCustomFieldIds`, `FooterText` (500 characters) and
  `CompactMargins`, through one mutator, `UpdateLayout`. It refuses an element the type does not print or may not
  hide. Moving a template to another type keeps only what the new type may hide, and drops its custom fields.
- `PrintingTemplateSettings` is the detached settings, with `Default` for an organization that has no template.

**Application.**
- `UpdatePrintingTemplateLayoutCommand` (Manage key). Its own command, so the existing Update callers cannot reset a
  layout they never heard of. Every member is nullable, so a missing one is a 400 naming it.
- `PreviewPrintingTemplateQuery` (View key): the unsaved settings over a fixed sample under the organization's real
  header. Its number is `SAMPLE`, and it never renders a real document, because a real invoice may only print as a
  counted copy (phase 67).
- `ListPrintElementsQuery` (View key): the element table for the editor.
- `PrintingTemplateChecks` (shared by the save and the preview). It refuses a locked or unprinted element, and a custom
  field that is not this organization's, not active, or not for this type, each as a **400 naming the field**.
- The print handler:
  - reads the type's default template;
  - drops the product's code from its label when `ItemCode` is hidden;
  - skips reading the logo blob when the logo is hidden;
  - prints the line's **unit** (the line's own, or the product's primary unit);
  - prints the **HS code** column only when a line has one;
  - leaves the quantity total blank when lines use different units;
  - prints the grand total **in words** (rupees only);
  - prints the chosen **custom fields** in the template's order, skipping one left blank;
  - then runs `PrintTemplateApplier`.

**Api.**
- `DocumentPdfRenderer` draws three layouts from a `Style` (ruled, flush left, font size, margin, spacing), still
  without a `DocumentType`.
- It adds the custom-field line, amount in words, the signature-line switch and the footer line.
- The "Template: name" line is gone.

**Web.**
- `PrintingTemplateLayoutEditor`:
  - layout radios, each with its description;
  - grouped checkboxes for what prints, where checked means printed and a locked item is checked and disabled with
    "(required on a tax document)";
  - this type's active custom fields, sent in the definitions' order;
  - the footer line with a live character count and `aria-invalid` past 500;
  - compact margins;
  - Save, and a Preview that shows the PDF in a titled `<iframe>`.
- The page opens one editor per template, keyed by id, so it always starts from what is saved.
- Focus moves to the editor's heading when it opens, and back to the row's *Layout* button when it closes.

## 2. Scope decisions (the user's, 2026-10-09, each the recommended option)

- **Decision A — three fixed A4 layouts.**
  - *Standard* is today's centred frame, unchanged.
  - *Traditional* rules every block: the familiar Nepali bill.
  - *Minimal* has no logo and no shading, is left-aligned, and is tight.
  - Any layout serves all seventeen printable types, because the renderer draws `Sections` and never switches on
    `DocumentType` (phase 27b).
  - Modern and Classic are near-Standard and not built. *Retail* (80 mm) is the till's job, which the browser already
    prints (phase 62).
- **Decision B — show/hide only, no renamed labels.**
  - A template hides elements; it never relabels one.
  - On a tax document, the seller's address and PAN and the buyer's address cannot be hidden (VAT Rules Schedule 5,
    as phase 62 read it). The title, number, both dates, the buyer's PAN, the copy mark and who printed a counted copy
    are not elements at all.
  - Custom fields print by choice, one checkbox per field that applies to the type, as the vendor does.
  - A free footer line, logo on/off and compact margins complete it.
- **Decision C — one template per type**, organization-wide, as before. The vendor's per-location selection is a
  recorded divergence.
- **Decision D — no date-system option anywhere.** Non-tax documents keep following the viewer's toggle; tax documents
  stay BS with AD beside it (phase 71 Decision A).

Decided in the session, not asked:

- **Decision E — nothing hidden by default, including what this phase added.**
  - A tenant that never opens the editor now gets a Unit column, the HS code where a line has one, and the amount in
    words. These are what a reader of a Nepali bill expects. The unit was the gap phase 52 left: a quantity entered in
    boxes printed as a bare number.
  - The vendor's default hides the item code. Ours keeps it, because ours has always printed it.
  - The alternative, "new elements start hidden", needed a second set (shown-optional) or a backfilled default list.
- **Decision F — amount in words in rupees only.** `AmountInWords` knows one currency. A foreign-currency document
  prints none rather than a wrong currency's words.
- **Decision G — the "Template: name" footer goes.** Phase 20d printed it to prove the template was read. The page now
  visibly changes instead, and a customer's bill should not carry configuration noise.
- **Decision H — the preview is sample data, posted, not saved.**
  - The vendor's preview is fixed sample data too.
  - A real document cannot be the sample, because an invoice may only print as a counted copy.
  - It is a POST because the settings are a body; it writes nothing and needs only the View key.
- **Decision I — a custom field prints only when it has a value on that document.** It also stops printing once
  switched off, or once it no longer applies to the type.

## 3. Bugs found and fixed

1. **The scaffolded `Layout` default was `''`.** The column stores the enum by name, as `DocumentType` beside it does,
   and `Enum.Parse("")` throws. Every existing template row, and so every print of a type with a template, would have
   failed on read. The migration is hand-edited to `'Standard'`. This is phase 46's "a plausible scaffolded default"
   again: InMemory never reads a converted column back from SQL, so no handler test could see it.
2. **Money wrapped in ruled cells.** Adding Unit and HS Code to the product table at equal relative widths left
   `11,000.00` two lines tall in Traditional. Product is now 3.4 and Rate/Amount 1.4/1.5 against 0.8 for the short
   columns. The extracted text read `11,000.0` / `0` and no test asserts line breaks; only the rendered page showed it.
3. **A right-aligned quantity ran into the left-aligned unit** ("2Pcs") in the unruled layouts. Unruled cells got
   3 pt each side. Then **the total-row rule broke into segments**, because the padding sat outside the border; the
   rule now goes on before the padding.
4. **The editor opened above the list with focus left below it.** The keyboard pass found it: a keyboard user pressed
   *Layout* and nothing visibly happened. Focus now moves to the editor's heading, and back on close.
5. **The refusals read "a Invoice".** They are reworded to name the type without an article.

## 4. Evidence

**Fresh organization** `8ebc3064-0d41-4da3-b763-96845a4ca989` ("Phase 73 Print 1010001631"). An earlier run
created `15fd6ea3-…`, which stopped at its first invoice with a 400: `warehouseId` is a required Guid even for a
service line. It holds a chart of accounts and no documents.

Seeded through the API, every status code printed (scratch `e2e73.py`):
- a VAT-registered organization and its GL defaults;
- units Pcs and Box, and a Service product (HS `9987`) with Box at 12;
- a customer with a PAN;
- three custom fields: Site (Invoice and Quotation), Vehicle No (Invoice) and Quote Validity (Quotation);
- invoice `0001` with a Box line and a primary-unit line at 10%, Site = "Butwal depot", Vehicle No blank, approved.

| Check | Result |
|---|---|
| `GET /elements`, Invoice | 16 elements (no Notes), locked `OrganizationAddress`, `OrganizationPan`, `PartyAddress` |
| `PUT /layout` hiding `OrganizationPan` on the Invoice template | **400**, `HiddenElements`: "required on … a tax document (VAT Rules Schedule 5)" |
| `PUT /layout` hiding `Notes`, printing *Quote Validity* | **400** naming both `HiddenElements` and `PrintedCustomFieldIds` |
| `PUT /layout` with only `layout` | **400** naming `HiddenElements`, `PrintedCustomFieldIds`, `CompactMargins` |
| `PUT /layout` Traditional, hide `VatColumn` + `ItemCode`, Site + Vehicle No, footer | **200**; SQL: `Traditional`, `ItemCode,VatColumn`, both ids, the footer |
| `POST /print/Invoice/{id}` twice | **200** both; SQL `InvoicePrints` 1 and 2; the second is boxed "COPY OF ORIGINAL · printed 2 times" top and bottom |
| `POST /preview` Quotation × 3 layouts; Invoice Standard and Minimal-compact | **200** each, PDFs read with the Read tool |
| `POST /preview` DebitNote hiding `PartyAddress` | **400** naming `HiddenElements` |

**The printed invoice (Traditional)**, looked at, not only extracted:
- a boxed organization block, then the shaded title band with TAX INVOICE / कर बीजक;
- both statutory dates in BS with AD, and the seller's PAN (locked);
- Bill To with address and PAN, then `Site: Butwal depot`, with the blank Vehicle No absent;
- no VAT column, and product names without codes;
- HS Code 9987 and units Box / Pcs, with the quantity total blank because the units differ;
- `Amount in Words: Rupees Fifteen Thousand Four Hundred Eighty One Only`, the footer line, and "Printed by".

The Minimal and Standard previews were read the same way after fixes 2 and 3.

**403-not-404**, same user, same organization, same run:
- `PUT /printing-templates/{nonexistent}/layout` as Admin is **404** ("Printing template not found.").
- After moving to a custom role holding only `Configuration.PrintingTemplate.View`, the same PUT is **403** naming
  `Configuration.PrintingTemplate.Manage`.
- The same user still got **200** on `GET /elements` and on `POST /preview`.
- The role was restored in SQL (`OrganizationMemberships.RoleId` = Admin, read back).

**Browser and keyboard pass** (Browser pane, signed in by transplanting the run's cookie; it expired after an hour
mid-pass and was renewed):
- Enter on *Layout of Ruled* moved focus to the heading "Layout: Ruled (Invoice)", after fix 4.
- Tab order: Close → **one** stop for the layout radios (the checked one) → the checkboxes, **skipping** the three
  locked ones.
- Space ticked *Item code*; Down moved the radios to Minimal; Enter on Preview drew the PDF in the titled frame, with
  the status "Preview updated with sample data."
- Enter on Save Layout announced "Layout saved for Ruled."; the row read "Invoice · Minimal layout" and SQL read
  `Minimal`, `VatColumn`.
- Enter on Close removed the editor and returned focus to *Layout of Ruled*.

## 5. Left open, and for later phases

- **Per-location templates** (Decision C): the vendor selects per type, channel and location.
- **Modern and Classic layouts**, and the vendor's **label renames** (Decision B). A rename would have to stay off the
  statutory labels.
- **An 80 mm ERP invoice** (the vendor's *Retail* layout). The till prints its own receipts.
- **Delivery Note / GRN / Warehouse Transfer custom HTML templates.** The vendor has a blank-template designer for
  these three. Not read in depth; it is a different feature (an HTML editor).
- **Ordering and placement** (the vendor's `_sorts` and Standard's quadrant positioning). Not built.
- **Amount in words for foreign currency** (Decision F).
- **The organization's location name/address** on the print (the vendor's two off-by-default organization fields).
- Carried unchanged: phase 72 § 5, phase 71 § 5, phase 70 § 5, phase 69 § 5, phase 67 § 5, phase 66 § 5, phase 63
  § 5, phase 62 § 5.

## Writes made to the vendor tenant

None. Every API request the page made was logged, and anything other than a GET would have been refused in the page:
89 were made, all GETs. Four layout editors were opened, and each was left by navigating away without Save. The
*Add New Template from Library* dialog was opened (a GET) and dismissed.
