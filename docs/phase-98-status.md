# Phase 98 — the money receipt on the 80 mm roll

## TL;DR

**In plain words, what changed for someone using the app.** A shop that takes a customer's payment at the counter can
now hand back the money receipt from the counter's thermal printer. Until now a Payment's PDF printed only on A4; the
80 mm roll was offered to invoices, credit notes and sales orders.

- In *Configurations > Printing Templates*, a Payment template now offers **Retail (80 mm roll)** beside the five A4
  layouts. Choose it, and every money receipt printed from that template comes out as one long 80 mm slip.
- The slip carries what the A4 receipt does: the business's name, address and PAN, *Customer Receipt*, the receipt
  number, the date in BS with AD beside it, who paid, the cash or bank account, each invoice the payment settles with
  its date and amount, the allocated and unallocated amounts, the amount in words and the footer.
- Like every roll, it has no logo and no signature line (a thermal head prints neither well). Nothing on the slip names
  who received the money: a money receipt is not a counted print, so it carries no *Printed by …* line (§ 5).
- A Payment is one type for both directions, so a supplier payment can also print on the roll if its template says so.
  Purchase documents still cannot.

Nothing else changed: no new screen, permission, endpoint or migration.

**Found on the way and fixed:** every money receipt, on every layout, printed its number twice (*Number: 0001* and
*Receipt No: 0001*). It now prints once.

Tests: Domain 1055, Application.UnitTests **1915** (+1), Infrastructure.UnitTests 14, Api.IntegrationTests **80** (+1),
Angular 935. All green with Docker up.

| | |
|---|---|
| Live read taken | **no** (the roll is our own since phase 79; the vendor offers its Retail layout to three types and no payment) |
| Migration | **none** (a template's layout is stored by name; `Retail` already exists) |
| New permission keys | **none**. Choosing a layout stays `Configuration.PrintingTemplate.Manage`; printing a receipt `Payments.Payment.View` |
| Changed endpoints | `PUT /configuration/printing-templates/{id}/layout` and `POST .../preview` accept `Retail` on a Payment template; `GET .../elements` lists it for Payment; `GET /print/Payment/{id}` renders the roll when the template chose it |
| Changed screens | the Payment template's layout radios gain the roll (the editor reads its choices from the server) |
| Changed print output | the money receipt, when its template chooses the roll (§ 4: looked at) |
| Initial bundle | **656.17 kB**, unchanged, against 680 kB |

## 1. What shipped

`PrintLayouts.RetailTypes` gains `DocumentType.Payment`. That one list is read by the template's own guard, the layout
save's and the preview's 400, the applier, and `GET /printing-templates/elements` (which the editor's radios come from),
so nothing else needed to change: phase 79 kept the roll renderer free of any document type (each section's row prints
its first cell over "Header value" pairs, the amount at the right). A money receipt's two sections, *Payment Details*
and *Payment For*, stack like an invoice's lines.

The receipt's builder (`PrintDocumentQueryHandler.BuildPaymentAsync`) no longer adds a *Receipt No* / *Payment No*
header field: the renderer prints every document's code as its *Number*, so the receipt carried it twice.

## 2. Scope decisions

- **A. The roll is offered to the Payment type as a whole** (the user's request, 2026-10-21). A Payment is one type in
  both directions with one template, so a supplier payment may print on the roll too. Splitting the template by
  direction would be a new axis for one case; the roll is a choice an organization makes, never a default.
- **B. No logo and no signature line on this roll either**, as on every roll (`PrintLayouts.Omits(Retail)`): a thermal
  head prints neither well, and the vendor's roll has neither. The template editor says so beside the radio. On a
  receipt this leaves nothing naming who took the money; the user chose to keep it that way (§ 5).
- **C. Not a tax document** (phase 84 Decision D): one *Date* in BS with AD beside it, no issue date, nothing locked. The
  roll changes the paper, not what the receipt is.
- **D. The duplicate number fixed in passing** (phase 95's rule: fix what the session finds). The A4 layouts lose the
  repeated line too.
- **No new key, no migration**: choosing a layout stays `Configuration.PrintingTemplate.Manage`, printing the receipt
  `Payments.Payment.View`; a template's layout is stored by name and `Retail` already existed.

## 3. Bugs hit and fixed

- **The receipt's number printed twice** (§ 2 D), seen on the rendered roll. A handler test now holds that no header
  field repeats the document's code.

No test needed teaching; the domain test that pinned the roll's three types was updated to four on purpose.

## 4. Verification

- **Tests.** Domain: the roll is offered to exactly Invoice, Credit Note, Sales Order and Payment. Application: the
  element table offers Payment the roll with its omissions; the applier keeps a Payment template's roll and hides the
  signature line; the receipt prints its number once. Api: a payment-shaped receipt on the roll carries every field the
  A4 receipt does on one page 80 mm wide (226.75 pt).
- **E2E on a fresh organization** (*Phase 98 Roll Receipt 101052*, VAT-registered). Two approved invoices of 3,390 and
  2,260; a 5,000 receipt from *Ram Bahadur Thapa* in cash, 3,390 and 1,000 allocated, approved. `GET .../elements`
  offered Payment the five A4 layouts and Retail, and the Purchase Bill no Retail. A Payment template saved on Retail
  (200); a Purchase Bill template on Retail was **400** naming `Layout`. `GET /print/Payment/{id}`: **200**, one page,
  `/MediaBox [0 0 226.75 …]`. SQL: the template's `Layout` = `Retail`; the receipt Approved, 5,000, 4,390 allocated.
- **403-not-404.** `GET /print/Payment/{nonexistent}`: **404** "Payment not found." as Admin; **403** naming
  `Payments.Payment.View` from a role copied from Admin without that key, beside a **200** on `GET .../elements` from
  the same user; **404** again once restored.
- **Keyboard, in a real browser** (keys only, from the page heading): Tab to the Counter receipt's *Layout* button,
  Enter; focus moved to the editor's heading *Layout: Counter receipt (Payment)*, whose radios list *Retail (80 mm
  roll)*; Tab past *Close* to the radios, arrow keys to Retail (focus ring visible); Tab to *Save Layout*, Enter: "Layout
  saved for Counter receipt.", focus stayed on the button, and SQL read `Retail`.
- **PDF, looked at.** The receipt rendered to an image (`tools/pdf/pdf2png.ps1`): the business's name, address, phone
  and PAN; CUSTOMER RECEIPT; Number 0001; the date 2083-07-04 BS (2026-10-21 AD); the reference and payment mode; who
  paid; the cash account and Net Debit 5,000.00; both invoices with their dates and amounts and Allocated 4,390.00;
  Unallocated NPR 610.00; the amount in words; the footer. Reprinted after the fix, the number appears once and the slip
  is 12 points shorter.

## 5. Left open, and why

- **Who received the money, on the roll: settled, not open.** The A4 receipt has a signature line; the roll has none,
  and a payment is not a counted print, so no *Printed by* line either. Asked whether to add a "Received by" line, the
  user chose to leave the roll as it is (2026-10-21). Not carried.
- **The tax advisor's question on exempt sales and input VAT** (phase 97 § 5) and **free goods in a scheme**: unchanged.
- **Not fixable without new information** (unchanged from phase 95): a partial refund of a sale from before phase 94/95
  that walked its batches returns into the oldest-taken batch.

Phase 79 § 5's list is now closed: the money receipt on the roll here, and the rest dropped by the user in phase 96.

## Writes made to the vendor tenant

None; the vendor was not read.
