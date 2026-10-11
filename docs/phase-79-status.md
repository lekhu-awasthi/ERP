# Phase 79 — Print layouts: Modern, Classic, an 80 mm ERP invoice, and the total in words in any currency

## TL;DR

**Phase 73 § 5's print items (carried through phase 75 § 5), finished or retired.**

1. **Two more A4 layouts, Modern and Classic**, drawn from the vendor's own previews (read live 2026-10-11).
   - *Modern*: the organization flush left in light type with the logo opposite, and a rule under each half of the header.
     The title and the details sit as a label/value list opposite the customer, with a rule under every table line. The
     total in words goes under the totals.
   - *Classic*: a centred header over a rule, then *Bill To* opposite the details and a fully gridded table. Below it,
     one box holds the notes and signature beside the totals and the total in words.
   - Like the first three, each serves all seventeen printable types without a `DocumentType` in the renderer.
2. **An 80 mm roll for the ERP (*Retail*)**, offered to Invoice, Credit Note and Sales Order only, as the vendor offers
   its Retail layout.
   - It is statutory-complete where the vendor's is not. The vendor's Retail prints no transaction date by default.
     Ours prints the seller's PAN and address, the title in both languages, both dates (BS with AD), the buyer's PAN and
     address, and the copy mark top and bottom.
   - **A line is stacked, not squeezed.** The item prints on its own line. Under it go "Qty 2  Unit Box  Rate
     1,000.00  Disc % 10  VAT 234.00", with the amount on the right. The vendor drops the discount and VAT columns,
     which leaves an Amount that is not Qty × Rate with nothing on the paper saying why.
   - The roll has no logo and no signature line. Those are the layout's omissions (`PrintLayouts.Omits`), never a
     statutory element.
3. **The total in words is in the document's own currency**: "US Dollars Seventy Six and Fifty Cents Only". This
   reverses phase 73's Decision F, which printed no words on a foreign-currency document.
   - Every catalogue currency is covered.
   - A foreign currency counts in millions; NPR and INR count in lakhs.
   - A currency with no minor unit (yen, won) prints no words for a fraction, rather than words that disagree with the
     figure.
4. **Ordering of header blocks is retired as a non-feature.** The vendor's `_sorts` arrays are server-side defaults
   that no client code ever writes (searched across the whole bundle). **Positioning and label renames were not
   built** (Decision C).

The user's decisions (2026-10-11), each the recommended option:

- **A:** Print layouts are the phase (over discount controls, discard-with-a-share and the export's remaining gaps).
- **B:** Both Modern and Classic.
- **C:** An 80 mm ERP layout, statutory-complete. Ordering retired; no quadrant positioning, no renames.
- **D:** A foreign currency's words name its currency and count in millions; NPR and INR keep lakhs.

| | |
|---|---|
| Live read taken | **yes**, read-only, abcagro1, 2026-10-11 (§ Live read). Every request a GET, none refused, nothing saved |
| Migration | **none**. `Layout` is stored by name in a 20-character column; the members are appended |
| New permission keys | **none**. Saving stays on `Configuration.PrintingTemplate.Manage`, the preview and the element table on `.View` |
| Changed endpoints | `GET /configuration/printing-templates/elements` gains `layouts: [{layout, omits}]` per type; `PUT .../{id}/layout` and `POST .../preview` refuse a layout the type is not offered (**400 naming `Layout`**) |
| Changed print output | three new layouts; a foreign-currency document now prints its total in words; nothing changes for an existing template |
| Changed screens | Configurations > Printing Templates > *Layout*: the radios come from the server (six for the three sales types, five elsewhere), and an element the chosen layout never prints is unticked, disabled and labelled "(not on this layout)" |
| Initial bundle | **652.58 kB**, unchanged (the configuration page is lazy) |

**Three things worth carrying forward.**

1. **A narrow page wants a different shape, not fewer columns.** The roll stacks each row as its first cell over its
   other cells, which works for any section. Dropping columns would have made the printed figures stop adding up.
   This is phase 73's "a new column narrows the others" turned around.
2. **A fixed-width element inside a relative half throws only in the layout that halves it.** Classic's signature line
   (two 140 pt rules) fit the full width in Modern. In Classic's half-page box it raised QuestPDF's
   `DocumentLayoutException`, and only a render of that one layout found it. Every layout × shape now has a render test
   (`A_document_with_no_party_summary_or_words_still_renders`).
3. **A vendor's data shape is not its feature list.** `_sorts` sits in every template configuration, and no screen
   writes it. Phase 54's "ask what writes it" again: the question retired a carried item outright.

Tests: Domain **1014** (+19), Application.UnitTests **1729** (+7), Infrastructure.UnitTests 13, Api.IntegrationTests
**71** (+14), Angular **838** (+2, 101 files). `dotnet build`, all four .NET suites (Docker up, one at a time), `ng build`
and `ng test` are green.

---

## Live read, 2026-10-11 (abcagro1, *Abc Agro Trading*, 14 trial days left), read only

The ERP was entered through *Open Tigg*'s hand-off (`window.open` replaced, the captured URL followed in the same tab,
its token never printed). `XMLHttpRequest` and `fetch` were wrapped to log every request and refuse anything but a GET:
**none refused**. Configurations > Printing Templates, the Standard, Retail, Modern and Classic editors, and Sales >
Invoices were opened; each editor was left by navigating away, never saved.

1. **The Sales Invoice gallery** holds Standard, Traditional, Minimal, Modern, Classic and two Retail copies. Each
   carries `fields` (the editor sections it offers) and one configuration shape. Retail's sections are its own:
   `LINE_ITEM_INVOICE_RETAIL`, `TOTAL_INFO_INVOICE_RETAIL`, `FOOTER_INVOICE_RETAIL` and `SPACING`, with no logo,
   formatting, margin or signature section.
2. **Retail's defaults:**
   - `spacing.paper_width: 80`.
   - Columns `#`, *Items*, *Qty*, *Rate*, *Amount*. Code, HS code and UOM are off; there is **no discount or tax column
     at all**.
   - `truncate_item_name` is on. Three free footer lines.
   - Totals: Sub Total, Discount, Non-Taxable, Taxable, VAT, Net Total and Amount in Words; Tender, Change, Service
     Charge and Total Qty are off.
   - **`entry_date` ("Transaction Date") is off**, with "Invoice Date" and "Bill No:" shown. The preview prints Bill
     No, Invoice Date in BS, the mode of payment, and the customer's name, address and PAN. It prints no heading at the
     top: the title leaf is on but the preview shows none.
3. **The words name the currency**: Retail's preview reads "Eleven Thousand Seven Hundred Forty-Five Nepalese Rupees".
   The client bundle carries a number-to-words library with Rupee/Paisa, Rupee/Cent and Dollar/Cent locales (the
   Dollar one counts in millions). No foreign-currency invoice exists on this tenant (all three approved invoices are
   NPR), so the vendor's own words for a dollar bill were not read; Decision D is ours.
4. **Modern** (preview):
   - The organization is in light type top-left, with two rules under the header halves.
   - CUSTOMER DETAIL sits left and the details as a label/value list right.
   - Rules between table rows; totals right with a rule above Net Total; a NOTES block left.
   - AMOUNT IN WORDS right-aligned under the totals, a "FOR: organization" signature, the footer line.
   - **No title is printed** on the sample.
5. **Classic** (preview):
   - A centred organization header over a rule; *Bill To:* left; details right with dates as `22-09-2023 / 05-06-2080`.
   - The custom field row (`country: Nepal`), then a fully gridded table.
   - One box: Notes and "For: org" left, the totals grid and *Amount in Words* right.
6. **Ordering is not a control.** `_sorts` (organization, contact, document detail) appears in every configuration,
   but the string `_sorts` occurs in **none** of the eleven client scripts. The editor has show/hide switches and label
   inputs, and no drag handle or order control.
7. **Positioning** (Standard only) is four dropdowns, Top Left / Top Right / Bottom Left / Bottom Right. Each offers
   all four blocks, none disabled, so two quadrants can name one block.

## 1. What shipped

**Domain.**
- `PrintLayout` gains `Modern = 3`, `Classic = 4` and `Retail = 5`, appended (stored by name).
- `PrintLayouts`:
  - `A4` (every printable type); `RetailTypes` (Invoice, Credit Note, Sales Order).
  - `OfferedFor(type)` and `IsOffered`.
  - `Omits(layout)`: Minimal omits the logo; Retail the logo and the signature line.
- `PrintingTemplate.UpdateLayout` refuses a layout the type is not offered. `Update` moving a template to a type not
  offered its layout falls back to Standard.
- `PrintingTemplateSettings.Shows` is false for what the layout omits. The applier, the logo read and the preview all
  ask that one question.
- `AmountInWords.For(amount, currency)`:
  - NPR exactly as `Rupees` reads.
  - A table of major/minor units for the other 24 catalogue currencies, international grouping except INR.
  - Three minor digits for KWD; none for JPY and KRW.
  - Null for a negative amount, an unknown code, or a fraction of a currency without one.

**Application.**
- `PrintingTemplateChecks` takes the layout. The save and the preview answer a 400 naming `Layout`.
- The print handler's words come from `AmountInWords.For` in the document's currency.
- `ListPrintElementsQuery` returns each type's `Layouts` with what each `Omits` (narrowed to the type's elements).

**Api.** `DocumentPdfRenderer`:
- Modern's and Classic's header and body.
- Shared pieces factored out of the Standard frame, which draws as before: the centred organization block, the title,
  the party block, a summary row, custom fields, notes and terms.
- Classic grids its table with half-width borders, so two cells meet as one rule.
- The roll: `ContinuousSize(80 mm)`, 4 mm side margins, 8 pt type, each row stacked. Pairs are kept whole with no-break
  spaces ("Disc %" no longer breaks from its "10"), and the amount sits right.

**Web.** `PrintingTemplateLayoutEditor`:
- The layout radios are the server's list for the type, labelled from the client's table.
- An element the chosen layout omits is drawn unticked and disabled, described by "(not on this layout)".
- A saved hidden choice for that element is kept, so switching back restores it.

## 2. Scope decisions (the user's, 2026-10-11, each the recommended option)

- **Decision A — the print items are the phase.** Discount controls had no live evidence, discard-with-a-share no
  vendor dependency, and the export had just had two phases. Printing was the one needing a tenant that has two weeks
  left.
- **Decision B — Modern and Classic, both.** Drawing code only, as phase 73 found the vendor's layouts to be.
- **Decision C — the roll, statutory-complete; ordering retired; no positioning, no renames.**
  - The vendor's Retail is offered to three types, and so is ours.
  - Unlike it, ours prints both statutory dates and keeps every line figure.
  - Ordering has no control to copy (§ Live read 6).
  - Positioning is a Standard-only quadrant picker that lets one block sit twice.
  - Renames could only reach non-statutory captions; phase 73's Decision B (no renamed labels) stands.
- **Decision D — words in the document's currency, millions abroad, lakhs for NPR and INR.**

Decided in the session, not asked:

- **Decision E — the roll stacks every column rather than dropping any** (TL;DR 2). It needs no per-type code, and it
  is why the roll could be generic.
- **Decision F — a layout's omissions are data in the Domain, not drawing code.** `PrintLayouts.Omits` lets the editor
  disable what the layout will not print, and a guard test holds every omission off every locked element. Minimal's
  logo, which phase 73 dropped only in the renderer, moved there too.
- **Decision G — a template moved to a type not offered its layout falls back to Standard**, as phase 73's move already
  drops what the new type may not hide. Refusing the move instead would make a rename of an old template fail for a
  reason the rename screen does not show.
- **Decision H — the roll's party block is its own, at the roll's type size.** The A4 party block prints the address
  at 9 pt under an 8 pt name, which read as backwards on the paper.

## 3. Bugs found and fixed

1. **Classic threw `DocumentLayoutException` on every document.** The signature line was two `ConstantItem(140)` rules
   with a gap between them: 280 pt and more, inside a box half the page wide. It now uses relative widths. Only a render
   of Classic itself could show it.
2. **"Disc %" broke from its value on the roll** ("Disc %" at a line's end, "10" on the next), and the buyer's address
   printed larger than the name. The figures are now unbreakable pairs with three spaces between pairs. The roll got its
   own 8 pt party block. Both were found by looking at the rendered page.
3. **"Exchange Rate To NPR:" wrapped onto two lines** in Classic's details list on the E2E's dollar invoice. The label
   column was a relative third of half the page. It is a fixed 108 pt column now, and the details block takes 1.25 of
   the row against the party's 1. Modern's header rules split the same way.

## 4. Evidence

**Unit and PDF tests.**
- `PrintLayoutsTests` (Domain): offered sets; omissions never locked; the save's refusal; the move's fallback.
- `AmountInWordsTests` (Domain): every catalogue currency, the rupee unchanged, USD in millions, INR in lakhs, KWD's
  fils, a fraction of yen refused.
- `PrintingTemplateLayoutTests` (Application):
  - the applier on the roll; the save and the preview refusing Retail as a 400 naming `Layout`;
  - the element table's layouts;
  - "A foreign currency document prints its amount in words in its own currency" (renamed from phase 73's "prints
    no amount in words").
- `Phase79PrintLayoutPdfTests` (Api): every new layout prints all Schedule 5 content in the PDF's text; the roll is
  one page and 80 mm wide (MediaBox 226.77 pt); the roll stacks a line and keeps its VAT and discount; logo and
  signature drawn by Modern and Classic but not the roll; every new layout renders a document with no party, summary
  or words. Phase 75's location test runs over all six layouts.
- `printing-template-layout-editor.spec.ts`: the radios are the server's (six for an invoice, no Retail for a journal);
  choosing Retail disables the logo with its note and sends the saved hidden choice unchanged.

**E2E on a fresh organization** (*Phase79 Print 081605*, `e1f3df08-…`, VAT-registered, multi-currency). Seeded by API:
three account groups and accounts, the defaults, a warehouse, a unit, two Service products, and a customer with a PAN.
USD was activated. Invoice 0001 (NPR, a 13% line with 10% discount and an exempt line, grand total 2,534.00) and 0002
(USD at 133.5, 3 × 25.50) were approved.

- `GET .../elements`: Invoice lists six layouts, Retail omitting `[OrganizationLogo, SignatureLine]`; Quotation and
  Purchase Bill list five.
- `PUT .../layout` Retail on the invoice template **200**. On the quotation template it is **400** `{"Layout": ["The
  Retail layout is not offered for Quotation."]}`; Classic **200**. `POST .../preview` Retail for a Sales Order **200**
  (a PDF); for a Purchase Bill **400 naming `Layout`**.
- Counted prints, each a `POST /print/Invoice/{id}` **200**:
  - 0001 on the roll: one page, **MediaBox 226.75 × 421.75 pt**;
  - 0002 on the roll: 226.75 × 405;
  - 0001 in Modern (copy 2), 0002 in Classic (copies 2 and 3) and in Modern (copy 4), each 595 × 842.
- **Looked at, rendered with `Windows.Data.Pdf`:**
  - The roll reads TAX INVOICE / कर बीजक, Number 0001, Transaction and Issue Date `2083-06-25 BS (2026-10-11 AD)`, Ref
    PO-79, Bill To with PAN 609876543, each line's figures under its name, Grand Total NPR 2,534.00, "Rupees Two
    Thousand Five Hundred Thirty Four Only", and the printed-by line.
  - Classic and Modern on 0002 read **"US Dollars Seventy Six and Fifty Cents Only"** under **USD 76.50**, with
    "Exchange Rate To NPR: 133.5" on one line after fix 3, and the copy mark top and bottom.
- **403-not-404.** `PUT /configuration/printing-templates/{nonexistent}/layout` with a valid body is **404**
  ("Printing template not found.") as Admin. The same user moved to a custom role holding only
  `Configuration.PrintingTemplate.View` gets **403 naming `Configuration.PrintingTemplate.Manage`**, beside that
  user's **200** on `GET .../elements`. The membership was put back on Admin in SQL.
- **In SQL:**
  - `configuration.PrintingTemplates`: *Counter* (Invoice) `Retail` hiding `SignatureLine`, and *Quote* (Quotation)
    `Classic`, so the refused Retail left nothing behind.
  - `sales.InvoicePrints`: 0001 printed 1–2 and 0002 printed 1–4, every row `Pdf`.
- **Browser and keyboard pass** (`erp-web-ssl`; the cookie was moved in through a `web/public` file written by a script
  and deleted at once):
  - The list reads "Invoice · Retail layout" and "Quotation · Classic layout". Enter on Counter's *Layout* opened the
    editor with focus on its heading.
  - Six radios with Retail checked. *Logo* and *Prepared By / Approved By* are unticked and disabled with "(not on
    this layout)".
  - Keys only: Tab, Tab reached the Retail radio, and **ArrowUp chose Classic**. The logo box came back ticked and
    enabled; the signature line stayed unticked, as saved.
  - Tab reached the logo box, and **Space unticked it**. Enter on *Save Layout* (focus placed on it) sent the PUT,
    whose response read back `{"layout": "Classic", "hiddenElements": ["OrganizationLogo", "SignatureLine"]}`.
    SQL agrees, and the page said "Layout saved for Counter."
  - The Quote template's editor offers five radios, with no Retail.
  - The console's one error was this session's first cookie fetch, made before the dev server had finished building.

## 5. Left open, and for later phases

- **Quadrant positioning and label renames** (Decision C). Renames would have to stay off the statutory labels.
- **The vendor's DN / GRN / Warehouse Transfer custom HTML templates.** Still unread in depth; an HTML designer is a
  different feature.
- **The roll's per-location default.** A location's template (phase 75) can be the roll, so a counter branch can print
  rolls while the head office prints A4. Nothing chooses a layout by printer.
- **South Asian currencies other than INR** (BDT, PKR, LKR, BTN) count in millions under Decision D, though their own
  documents count in lakhs. This is one flag per row in `AmountInWords` if the user wants it.
- **The vendor's own words for a dollar bill** are unread (no USD invoice on the tenant).
- **The money receipt** (a Payment's PDF) is offered every A4 layout but not the roll.
- **Carried unchanged:** phase 77 § 5, phase 76 § 5, phase 75 § 5's tax-advisor question and channel axis, phase 74
  § 5, phase 72 § 5, phase 71 § 5, phase 70 § 5, phase 69 § 5, phase 67 § 5, phase 66 § 5, phase 63 § 5 and phase 62
  § 5; phase 78 § 5's export items.

## Writes made to the vendor tenant

None. Every request in the live read was a GET; no editor was saved.
