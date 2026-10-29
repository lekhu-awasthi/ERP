# Build Roadmap — Phases & Task Breakdown

Companion to `architecture-spec.md` (what to build) and `product-requirements.md` (why). This doc says *in what order*, broken down small enough to actually pick up and work. The reference product is a live Tigg UAT tenant; when a screen's shape is unconfirmed, it is read live through the Browser pane before building (the user logs in themselves — credentials are never entered by the agent and never committed to this repo; see `phase-8f-status.md` for the established workflow).

Guiding rule for phase sizing: each phase ends with something *runnable and demonstrable* (an API you can hit, a screen you can click through), not just "code exists." Every phase's exit criteria include: `dotnet build`/`dotnet test`/`ng build`/`ng test` all green; a hand-driven E2E pass against the real API/DB/browser (seed master data via curl + cookie jar, reserve UI clicks for the phase's own new screens); at least one **negative** check (a permission `403` naming the exact key, a lifecycle `409`, or a validation `400`) proven against the real API, not just the happy path; and a `docs/phase-N-status.md` history doc recording scope decisions and bugs before the phase is called done.

---

## Completed phases (0–95)

One line each; phase *N*'s full story is `docs/phase-N-status.md`. The verbose index as it stood on
2026-09-28 is archived verbatim in `roadmap-history.md` ("Completed-phases index, verbose form").

| Phase | Shipped |
|---|---|
| 0 | Scaffold, CI, Testcontainers harness |
| 1a–1c | Registration, email verification, cookie-JWT login, password reset; Organization, wizard, memberships, invites; Role stub + `AuthorizationBehavior` |
| 2 | Generic lookups, TenantSettings, race-safe numbering, custom-field definitions |
| 3 | Contacts, Products, list/detail chrome |
| 4 | Chart of accounts, Journal Voucher, GL posting engine, Cash Transfer |
| 5 | Sales chain (Quotation → Invoice → Payment), Sales Order, Credit Note, Warehouse |
| 6 | Purchase chain, Expense, Debit Note, TDS |
| 7 | FIFO stock ledger, COGS, transfers, adjustments |
| 8a–8f | Financial and statutory reports (TB, BS, P&L, VAT, TDS, Annex 13, Annex 5) |
| 9–11 | Ageing summary/statement; Contact Overview; payment-allocation fix |
| 12–15 | Transaction Approval queue; Tasks; custom roles + permission matrix; CRM Deals |
| 16a–16d | Void + lock date; discounts; pagination + `.xlsx` export; System Audit |
| 17 | Quick Payment/Receipt, bank accounts, cheques, allocation, opening balances |
| 18 | Files, attachments, personnel, comments, SMS |
| 19 | Reporting tags + remaining reports |
| 20a–20g | Custom fields on forms, custom status, cost terms, print pipeline, alert scheduler, feature flags, Turnstile |
| 21a–21c | Async jobs + bulk import; full-tenant export; migrated tax registers |
| 22–25 | Document inbox (AI extraction); Nepali localization; variant products; manufacturing |
| 26a–26c | Report catalogue completed (accounting, receivable/payable, inventory) |
| 27a–27b | Document mechanisms swept to every type; print for all 15 types |
| 28–31 | Multi-currency; landed cost; communications; credit control + Configurations > General |
| 32–32b | Billing locations; per-location permissions |
| 33–34c | Global search/History/Quick Links; WCAG sweep; shell on `NavigationCatalog`; scale + indexes |
| 35a–38 | Location dimension in forms and reports; allocation/forex consistency; inventory policy; import/export breadth |
| 39–41 | Rich text + logo + search page; human WCAG pass; subscription plans and quotas |
| 42–47 | Performance follow-through; aggregate completions; report semantics read live; multi-UOM × variants; metered axes; accessibility completion (NVDA hour still open) |
| 48–50 | Shared display components; the expiry decision; measured indexes |
| 51–52 | Batch and serial tracking; a unit on the document line |
| 53 | Re-planning by permission-key census (no code) |
| 54–57 | Deferred line types settled; bank statement import; bank reconciliation; bank module finished + census re-run |
| 58 | Physical-movement inventory (Delivery Note, GRN, Inventory Variance) |
| 59 | POS scoping (no code); phases 60–66 planned |
| 60 | POS foundation: `PosMode`, per-location settings, payment modes with kind + account, walk-in, POS default accounts |
| 61 | POS sale engine: a till sale is an Invoice created approved, tenders a second GL entry, `PosSession` drawer posts, one session/day reader |
| 62 | Retail till UI (lazy full-screen `/pos`); abbreviated tax invoice and reprint-copy rules settled from the statutes |
| 63 | Returns at the till: a refund is a credit note created approved, its payout a second entry out of the drawer |
| 64 | Restaurant floor, orders and KOT: `PosOrder`, kitchen tickets per send × station, Sales > POS Orders |
| 65 | Kitchen board and settling: whole/by item/equal bills from frozen rates, rounding on the running total |
| 66 | POS reports and dashboard: Day Report, Payment Summary, Order Report, POS Sessions, the overview; the ERP sales reports and System Audit read with a Channel filter; every figure from one reader, the round-off the one line to the Sales Register |
| 67 | The ERP invoice and credit-note PDF: the heading the bill is (English and Nepali), every copy counted across till, PDF and email, copies marked; a counted print is a POST |
| 68 | Mark as Take Away and item transfer: a parcel is a split line moved by a take-away ticket, its service charge decided once at the mark by the location's setting; a transfer is a ticket on each order at the same rates; plus the buyer's PAN on the ERP PDF |
| 69 | A credit note names its invoice (VAT Rule 20(1)(e)): a picked one is a price adjustment capped by value and VAT, a typed one predates the system; conversion templates carry currency; line caps re-checked on edit |
| 70 | The packaging fee: per item on every parcel (percent of the line or flat per unit), inside the VAT base, its own account, frozen on the line; a product may opt out (on by default) |
| 71 | Every bill's date in BS: invoices, credit and debit notes (PDF and email) and till receipts print BS with AD beside it whatever the toggle, with both statutory dates (transaction and issue); the statutory register exports carry a BS column |
| 72 | The restaurant bill finished: a line's and the order's discount (percent) and a waived service charge are terms of the order, fixed at its first bill; a bill names its own customer and may leave the rest on their account; lowering a bill (a discount, a waiver, a Retail line below the catalogue price) needs `Sales.Invoice.Approve` at the location; one shared customer search that returns focus |
| 73 | Printing templates: three fixed layouts (Standard, Traditional, Minimal) chosen per type, show/hide toggles (a tax document's seller address and PAN and buyer address locked), the custom fields a bill prints, a footer line, compact margins, a preview of unsaved settings; every bill gained a Unit column, the HS code where a line has one, and the amount in words |
| 74 | Till pricing: Amount discounts at both tills (a line's rupees off each unit, a bill's or an order's spread over its lines and stored per line, an order's frozen at its first bill); the refund returning the last of a sale line takes what is left of it, so refunds tie out to the paisa; any Retail price but the catalogue's needs `Sales.Invoice.Approve` |
| 75 | Printing by location: "Location: name, address" under the registered address on every PDF of a multi-location organization (hideable, never locked); a location may choose its own template per type, falling back to the default; the till receipt prints the registered address and the location's own |
| 76 | Separate till keys: a discount, a waived service charge and any price but the catalogue's each need their own location-scoped key (`Sales.Invoice.TillDiscount` / `.TillRemoveServiceCharge` / `.TillChangeRate`, the vendor's three POS role switches) instead of `Sales.Invoice.Approve`, which also approved every ERP draft; existing Approve grants backfilled at the same scope; credit stays on Approve |
| 77 | An order's Amount discount moves with its food (a transferred or parcelled quantity takes its share, read from the line's bills once billed; discard still refused); the export's Sales Documents carry every part of a sale's money and Payments the till's tenders, change and payouts |
| 78 | The export finished: Purchase Documents carry expenses, TDS and additional costs as rows of their own, a debit note's bill and the classification it inherits; Unit, Exchange Rate and Location on every sheet; a drawer's Cash In / Cash Out on Payments; the export screen's unticked categories are no longer exported anyway |
| 79 | Print layouts finished: Modern and Classic on A4 for every printable type; an 80 mm roll (*Retail*) for Invoice, Credit Note and Sales Order that stacks each line and keeps both statutory dates; the total in words in any catalogue currency; the vendor's block ordering retired as no control |
| 80 | The export's remaining documents: four new sheets (Bills of Materials; Orders for Quotation, Sales Order and Purchase Order; Goods Notes for Delivery Note and GRN; Production for Production Order and Journal), Document No and Reference on Ledger Transactions and Stock Movements, an additional cost's Allocation Method |
| 81 | The ERP's price keys: a discount or a rate the form would not suggest on a Quotation, Sales Order or Invoice draft needs `Sales.Invoice.Discount` / `.ChangeRate` at its location (Admin+Member, backfilled to every role that drafts them; what a draft or its source quotation already carried needs neither); the forms disable what Save would refuse; a picked unit's rate goes through the price basis |
| 82 | A dish carrying a frozen share of an order's Amount discount can be discarded: its share leaves with it (the order's amount falls by what the quantity carries, every other dish keeps its price), exactly what billing and refunding it gave; before the first bill a discard leaves the amount whole; the panel says so and the message names what the discount fell by |
| 83 | A discount on one guest's part: a restaurant bill takes its own % or Rs after the order's terms, kept apart on each line (`PartDiscountAmount`) so the last part of a dish never takes it back and a void gives it up; `Sales.Invoice.TillDiscount` at the location from whoever bills; a PDF with both kinds prints the percentage first. Plus the ERP forms' *Remove* for a clerk, "in this organization" on a document with no location, and the discard capped at what is unbilled |
| 84 | Returns at another branch: a till refunds another location's retail sale (none with service charge or packaging fee), found by its full number under the refund's own keys there, out of that branch's drawer, its goods into that branch's warehouse (`CreditNote.WarehouseId`). Split payouts and *Fully refunded* on the refund screen; the money receipt's date in BS with AD beside it (`BillDatedTypes`, not a tax document) |
| 85 | A restaurant bill refunded at another branch: its service charge and packaging fee come back out of the selling location's accounts, in one entry stamped with the refunding branch, so its lines equal the same refund at home. The ERP credit note names a till refund's warehouse; removing a tender on the till or a restaurant bill keeps focus |
| 86 | A sale records the accounts its service charge and packaging fee went into (`Invoice.ServiceChargeAccountId`, `.PackagingFeeAccountId`), and a refund reverses into them after the location's setting changes; older sales keep the old rule. The credit-note list names the warehouse each note's goods went back into, through one reader with the note's page |
| 87 | An invoice and a purchase bill record every account they post to, and what reverses or settles them uses those: credit notes (returns, till refunds, price adjustments), debit notes, a refund's payout, and receipts and supplier payments (split per control account; a later allocation moves its share). No backfill; the Allocate screen keeps focus |
| 88 | The payment screens: the FIFO Suggest gains the membership and permission check it never had (`OrganizationScopeSweepGuardTests` fails any request naming an organization unscoped); the payment GL preview is built by Approve's own builder (`PaymentGlLines`); the four allocation pickers list one contact's open documents in the payment's currency, oldest due first (`ListOpenAllocationTargetsQuery`) |
| 89 | The VAT Summary is the IRD VAT return (a BS fiscal year, twelve month columns, the return's lines in English with the form's Nepali, a month's credit brought forward across the year's end), read through the registers' own readers (`SalesBookReader`, `PurchaseBookReader`) so a month equals the registers; expenses are purchases in the register and the return; Annex 5/13 and the return export in BS with AD beside it; Annex 5 gains Fiscal Year, Discount, prints and Entered By; both annexes open on this fiscal year to date; an ERP return's page names its warehouse; the client's calendar day is the Nepal day in 67 sources (reports opened, and due dates were suggested, a day early) |
| 90 | An expense's VAT read live on the vendor (filed as ours, nothing changed); Annex 13 is before VAT, as the vendor's is (it was gross since phase 8e); the Purchase Register's *Invoice No* is the supplier's own number (a debit note's own), beside *Document No*; one contact's picker, Suggest and till refund read that contact's settlements through a subquery (38,015 → 238 reads per picker), and the voucher read is one join for every caller; the VAT return's history read measured and left; the registers' returns no longer export `-0` |
| 91 | Serial-tracked products sell at the Retail till (a picker of the units in stock, a scan by serial, payment refused until each unit is named; kept off restaurant orders); returns carry their serials (`CreditNoteSerials`: till refunds tick the sale's units, ERP notes are prefilled; each unit restocks as its own layer, fixing phase 51's un-serialled restock); the till refund's owed read narrowed by the sale (56,566 → 59 logical reads with a measured `CreditNotes (OrganizationId, ReferrerId)` index); one guest may decline the service charge on their own bill (`InvoiceLine.PartServiceChargeWaived`), and the receipt, PDF and roll print a guest's discount apart from the order's |
| 92 | Serial integrity: a debit note names the units it sends back (`DebitNoteSerials`; the template offers the bill's units still on the shelf, a standalone note any unit there), voiding an invoice, a debit note or a transfer restocks each unit under its serial, receiving a unit already in stock is a 400 on the draft and a 409 from the ledger (`SerialStock`), the till's scan across two products names each one's own unit, and the debit-note form gains the warehouse phase 43 required and no screen sent |
| 93 | Batch integrity: an ERP invoice or debit-note line of a batch-tracked product names its batch, as the vendor requires (`StockLineKind`; the till still walks); both notes carry the batch phase 51 gave them and nothing wrote, prefilled by their templates (`DebitNoteBatches`: a converted note's from its bill; `CreditNoteBatches`: a converted note's from what the invoice issued, capped per batch); a void or till refund of a walked issue goes back into the batches it took, read from the document's own movements (`IssuedStockRestock`); over-long serial or batch numbers are 400s on all ten line commands, and two saves putting one serial in stock at once a 409; every native select binds per option (the credit note's showed the walk-in on every converted note), guarded by `select-binding-sweep-guard.spec.ts` |
| 94 | The Retail till's batch picker: a till line of a batch-tracked product names its batch, as the vendor's till requires (read live; `StockLineKind.TillIssue` retired, a till sale is an `Issue`); `GET /pos/tills/{loc}/products/{id}/batches` lists the batches left at the till's warehouse with their expiry, soonest first; the till opens a batch line on a radio picker and refuses payment until each names one; restaurant bills still walk, and a batch has no price of its own |
| 95 | Seven leftovers cleared in one session at the user's request: focus kept app-wide after a failed Save or a removed line (`FocusKeeper`); a batch on a restaurant order line; the till's scan indexed (11,189 → 50 reads) and its grid's location rule made a set (85,606 → 5,413); a batch's own selling price and required expiry, which every price gate keeps; serials printable on the bill and receipt (a template switch, off by default); company Batch/Serial Tracking switches overriding each product's; a return's price-to-cost difference to its bill's recorded account; line-table boxes kept readable at 1,024 px |

---

## Archived plans

Every finished plan moved verbatim to `roadmap-history.md`: parity 26–34c and consolidation 35–41
(2026-09-14), completion 42–47 (2026-09-15), continuation 48–52 (2026-09-20), bank-module 54–57
(2026-09-21), physical movement 58 (2026-09-25), and on 2026-09-28 the verbose index plus the
superseded "empty" plan. Two warnings travel with them:

- The bank-module entry holds **the phase-53 census of record** (166 keys, 20 of 22 document types,
  51 reports), reproduced exactly by phase 57.
- The phase-58 entry's FIFO paragraph is kept as written and is **wrong**; `phase-58-status.md` is
  what was built.

---

## Forward plan (60–66) — POS, planned 2026-09-28 in phase 59

Planned from a live read *and* a written service on the vendor's own till (`phase-59-status.md`;
evidence in `erp-module-scan.md`, "POS, read and written"), not from a screen list. **The vendor's
POS is an ERP client**: same API, same numbering, same GL, and its sale and refund are an ordinary
Invoice and Credit Note. So ours is **one app** (a lazy `/pos` route tree on the existing cookie and
API), reuses Invoice/Credit Note, and adds aggregates only for what an ERP lacks. **Retail first**:
60–63 alone ship POS Retail, and 64–65 add POS Restaurant on a proven sale engine.

| Phase | Scope (decision letters refer to `phase-59-status.md` §4) |
|---|---|
| **60 — POS foundation** ✅ *done 2026-10-01, `phase-60-status.md`* | `BillingLocation.PosMode` (None/Retail/Restaurant; B) replacing the two reserved `BillingLocationType` members; per-location POS settings (service charge rate + account, round-off + account, cash verification + denominations, default tab, print toggles); phase 2's `PaymentMode` extended with a kind, an account and a location link; the seeded walk-in customer; tenant-default Service Charge Income / Rounding / Cash Over-Short accounts; `Product.ServiceChargeApplicable` / `AvailableForSale`; new `Pos.*` keys; ERP-side configuration screens |
| **61 — POS sale engine** ✅ *done 2026-10-01, `phase-61-status.md`* | Invoice gains channel, session, order type, per-line service charge (inside the VAT base), round-off, change and **tenders**, which post as a second GL entry on the same source document (C, D); `PosSession` with denominations, Cash In/Out and close, **all posting** (H); one create-approved command; the shared session/day reader |
| **62 — Retail till** ✅ *done 2026-10-01, `phase-62-status.md`* | the `/pos` shell, session picker, product grid + search + barcode, cart and line edit, payment screen (multi-tender, change), hold/recall, 80 mm receipt with the correct header and separate service-charge and round-off lines, reprint marked as a copy |
| **63 — Returns at the till** ✅ *done 2026-10-02, `phase-63-status.md`* | a refund is a Credit Note created approved against the till sale, returning service charge and VAT in proportion; its payout is a second entry out of the caller's open drawer, after clearing what the customer still owes; one planner for the preview and the refund; the ERP's conversion of a till sale refused; refunds in the X/Z report and every reader of what is owed |
| **64 — Restaurant: floor, orders, KOT** ✅ *done 2026-10-02, `phase-64-status.md`* | areas and tables with a layout editor; `PosOrder` with per-line invoiced/served/discarded counters (E); `KitchenTicket` per send × station (F); Take Away and Delivery; an ERP *POS Orders* list (the divergence's surface) |
| **65 — Kitchen display and settling** ✅ *done 2026-10-02, `phase-65-status.md`* | a polled KOT board (`Pos.Kitchen.Operate`); the estimate bill; whole, by item/qty and equal split, every part from the order lines' frozen rates (defect 1's regression test), the last of a line taking what is left and rounding on the running total; each part an approved till Invoice; Settled frees the table, a void reopens it |
| **66 — POS reports and dashboard** ✅ *done 2026-10-02, `phase-66-status.md`* | Day Report, Payment Summary, Order Report, Product/Customer Sales, Sales Master/Summary, POS activity; the home dashboard, with every figure reconciling to the session reader and the Sales Register |

**Reopened from phase 47's Dropped list, with evidence:** `Product.PrintProfileId` (the KOT station,
phase 64) and the Service Charge column (a location rule × product flag, phases 60–61). Both were
correctly dead *from the ERP side*. The POS is where they act.

**Phase 59 §6's rule questions are all settled.**

- q1 was settled in phase 61. Service charge is inside the VAT base, and a mandatory one has been
  unlawful in Nepal since the Supreme Court's ruling of 2023-01-25 (`phase-61-status.md` Decision A).
- q2 and q3 were settled in phase 62 (`phase-62-status.md` Decisions A and B):
  - an abbreviated tax invoice needs the Tax Officer's permission, a VAT-registered seller and a
    bill of at most Rs 10,000 (VAT Rules Rule 18 as amended in 2076), and is never issued to a
    customer who asks for a full one;
  - a reprint must say "copy of original" and how many times the bill has been printed (the 2072
    computerised-invoicing procedure, §6).

The two rules apply to every tax invoice; phase 67 brought the ERP's own PDF and its emailed copy under both.

**Carried from phase 61** (§ 5 there): the full-tenant export has no service-charge or round-off
column (now for credit notes too). The ERP credit note against a till sale is settled by phase 63:
the ERP may no longer convert one, and the till's refund returns the service charge.

**The POS sequence (60–66) is complete, and so are phases 67 to 72.** Phase 67 put the ERP invoice and
credit-note PDF under the till's two statutory rules (`phase-67-status.md`). Phase 68 shipped *Mark as Take Away*
and item transfer on the restaurant till (`phase-68-status.md`): a parcel is a split line moved by a take-away
kitchen ticket, its service charge decided once, at the mark, by the location's *Service charge on take-away*;
a transfer is a ticket on each order at the same rates, and one that empties a tab closes it. Ahead of it, the
ERP PDF now prints the buyer's "PAN: …" when the contact has one (VAT Rules Schedule 5). Phase 69 made every ERP
credit note able to name its tax invoice (`phase-69-status.md`): a picked one makes a standalone note a price
adjustment capped by the invoice's value and VAT, a typed one must predate the first invoice here, and a
VAT-registered seller cannot approve a note naming none; it also put the source's currency on four conversion
templates and re-checked the line caps on an edit of a converted note. Phase 70 shipped the packaging fee
(`phase-70-status.md`): per item on every parcel (Take Away, Delivery, and dine-in food marked take-away), a percent
of the line or a flat amount per unit, inside the VAT base at the line's own rate, its own account, frozen on the
line, with a per-product opt-out on by default. Phase 71 made every bill's date BS with AD beside it, whatever the
toggle (`phase-71-status.md`): invoices, credit and debit notes on paper and by email, and the till's receipts, with
both statutory dates (transaction and issue); the statutory register exports gained a BS column. Phase 72 finished the
restaurant bill (`phase-72-status.md`): a line's and the order's discount and a waived service charge are terms of the
order, fixed at its first bill so the bills still add up to it; a bill names its own customer and may leave the rest on
their account; and lowering a bill anywhere (a discount, a waiver, a Retail line below the catalogue price) needs
`Sales.Invoice.Approve` at the location. Phase 73 made printing templates real (`phase-73-status.md`): three fixed
layouts chosen per type, show/hide toggles with a tax document's statutory content locked, the custom fields a bill
prints, a footer line and compact margins, previewed over sample data; no date-system option anywhere. Phase 74
finished till pricing (`phase-74-status.md`): Amount discounts at both tills (a line's per unit, a bill's or an order's
spread over its lines and stored per line, an order's frozen at its first bill), refunds that give back exactly what the
sale charged, and any Retail price but the catalogue's gated on `Sales.Invoice.Approve`. Phase 75 printed the
issuing location (`phase-75-status.md`): "Location: name, address" under the registered address when an organization
has more than one location, a template per location falling back to the default (phase 73 Decision C reversed), and
both addresses on the till receipt. Phase 76 split the till's price powers off Approve (`phase-76-status.md`): a
discount, a waiver and a changed price each have their own location-scoped key, as the vendor's POS role editor keeps
three switches, and every existing Approve grant was copied to them. Phase 77 let an order's Amount discount move
with its food (`phase-77-status.md`): a transferred or parcelled quantity takes its share, read from the line's bills
once the order is billed. The full-tenant export now carries every part of a sale's money (service charge, packaging
fee, both kinds of discount, round-off on a row of its own, the order and the invoice a note is against) and the
till's tenders, change and refund payouts. Phase 78 finished the export (`phase-78-status.md`): the Purchase Documents
sheet carries expenses, TDS and additional costs as rows of their own and a debit note's bill and inherited
classification; every sheet names its unit, exchange rate and location; a drawer's Cash In / Cash Out are Payments rows;
and the export screen sends only the categories left ticked. Phase 79 finished the print layouts
(`phase-79-status.md`): Modern and Classic on A4, an 80 mm roll for Invoice, Credit Note and Sales Order that keeps
everything a tax invoice must carry (each line stacked, so its figures still add up), and the total in words in the
document's own currency; the vendor's block ordering was found to be no control at all and retired. Phase 80 finished
the export's documents (`phase-80-status.md`): a census of every document type found eight on no sheet and four named
only by a GUID, so the export gained Bills of Materials, Orders, Goods Notes and Production sheets, and both ledger
sheets now print each row's Document No and Reference. Phase 81 gave the ERP its own price keys
(`phase-81-status.md`): the vendor's ERP has none (168 keys read in its bundle, none about price), so a clerk who may
draft but not price is our divergence. A discount, or a rate the form would not suggest, on a Quotation, Sales Order or
Invoice needs `Sales.Invoice.Discount` / `.ChangeRate` at the document's location; what the draft or its source
quotation already carried needs neither, and the forms disable what Save would refuse. Phase 82 let a dish carrying a
frozen share of an order's discount be discarded (`phase-82-status.md`): its share leaves with it, which is exactly
what billing it and refunding it gave. **Next: not yet scheduled**; the candidates are phase 79 § 5's (positioning
and renames, the custom HTML templates, lakh grouping, the money receipt on the roll), phase 71 § 5's, and phase
72 § 5's discount on one guest's part after splitting.

Open question for the tax advisor, not to be guessed (carried from the PAN fix): whether IRD expects a buyer PAN
on a high-value sale to an unregistered buyer. If it does, it would be a warning on approve, never a block.

**Raised 2026-10-08 by the user, done in phase 71: every bill shows its date in BS.** The statutory forms require
a transaction date and an issue date and name no calendar; the BS rule is practice and Rule 26's BS tax period, so
it was recorded as a product decision (`phase-71-status.md` § 0). The vendor's bill follows a per-template AD/BS
switch defaulting to AD (read live on 2026-10-09); ours has no switch on a tax document. This constrains the
printing-templates item below: no per-type "date system" toggle may switch BS off on a tax document.

**Carried from phase 71** (§ 5 there): the money receipt (a Payment's PDF) dates in BS with AD beside it since phase
84, as a bill and not a tax document. Annex 5/13 and the VAT Summary export are BS with AD beside it since phase 89. The
on-screen register grids follow the toggle; the email body's `$[DATE]$` stays AD; a tax advisor's word on the calendar
is still open.

**Raised 2026-10-03, done in phase 73: real printing templates.** Three fixed layouts with show/hide toggles and
the custom fields to print; no date-system toggle (phase 71), no renamed labels, one template per type.

**Carried from phase 73** (§ 5 there): per-location templates and the location's name and address were done in phase
75; the Modern and Classic layouts, the 80 mm ERP invoice and amount in words for a foreign currency in phase 79, which
also retired the ordering of header blocks (the vendor's `_sorts` is written by nothing). Label renames (never on a
statutory label), quadrant placement and the vendor's custom HTML templates for Delivery Note, GRN and Warehouse
Transfer remain, in phase 79 § 5.

**Carried from phase 75** (§ 5 there): which address a branch-issued tax invoice must carry (the tax advisor's); the
till receipt's location line is unconditional; a location's template has no channel axis.

**Carried from phase 79** (§ 5 there): quadrant positioning and label renames; the vendor's DN/GRN/WT custom HTML
templates; lakh grouping for BDT, PKR, LKR and BTN (one flag each); the vendor's own words for a dollar bill (unread).

**Carried from phase 67** (§ 5 there): the print race on both print tables is still unraced on SQL Server. The
standalone credit note was settled by phase 69, and the count note beside Print was looked at in phase 69 (it
wraps under Print at 375 px, legible, still described-by).

**Carried from phase 69** (§ 5 there): older standalone notes name nothing (no backfill is possible); two drafts can
race for one invoice's remainder, as the line caps always could. The export's invoice column was added in phase 77.

**Carried from phase 66** (§ 5 there): a from/to *time* on the Day Report (a shift across midnight as one report;
a reader change, not a screen change); the vendor's Delivery Partner Statement (delivery partners are outside
the sequence).

**Carried from phases 64, 65 and 68** (§ 5 in each): *Mark as Take Away* and item transfer shipped in phase 68
with the user's 2026-10-03 answer, the packaging fee in phase 70, and a discount, credit and per-part customers on a
restaurant bill in phase 72. VAT stays in code; only fees are settings.

**Carried from phase 72** (§ 5 there): a discount on one guest's part once splitting has begun was done in phase 83 (on
the bill, not the order). The export's order
terms were settled in phase 77 (a bill names its order and its lines carry the terms; no POS Orders sheet). Its refund-of-a-remainder paisa, Amount discounts and *Can Change Rate* were done
in phase 74.

**Carried from phase 74** (§ 5 there): a dish carrying a share can be discarded since phase 82. Its
transfers and parcels, and the export's per-unit discount and bill amount, were done in phase 77; its third item, the
vendor's three role switches as one key, in phase 76.

**Carried from phase 76** (§ 5 there): the ERP form's discount and rate were gated in phase 81, and a maximum discount
per role was closed there (neither vendor app has one); the vendor's server-side enforcement of its switches is unread;
its POS-channel role sets are not modelled.

**Carried from phase 81** (§ 5 there): the form's removal of a discount for a clerk and the note's wording on a document
with no location were done in phase 83. A price change between saves keeps the saved line's price (the carried rule).

**Carried from phase 82** (§ 5 there): the discard panel's maximum was capped at what is unbilled in phase 83.

**Carried from phase 83** (§ 5 there): the per-part service charge waiver and the receipt and roll printing a guest's
discount apart were done in phase 91.

**Carried from phase 78** (§ 5 there): done in phase 80 (four new sheets, an additional cost's Allocation Method, and
the ledger sheets naming each document). Cash Transfers and the drawer's over/short stay on Ledger Transactions by
phase 78's Decision C, now named by number.

**Carried from phase 80** (§ 5 there): POS Orders stay off the export (phase 77 Decision D); an Inventory Adjustment's
entered unit is not on Stock Movements, which is primary-unit by design.

**Carried from phase 77** (§ 5 there): discarding a dish that carries a frozen share was done in phase 82 (the share
leaves with it); the vendor's server-side handling of an amount on a transfer is unread. The drawer's cash in/out and the Purchase Documents sheet's
missing columns were done in phase 78.

**Carried from phase 70** (§ 5 there): the VAT Act §13 wording for packaging is unverified (phase 61's reading
applied to a second charge); a receipt prints the fee's total, not which items carried it. The export's packaging
fee, service charge and round-off were done in phase 77.

**Carried from phase 63** (§ 5 there): cross-branch returns, split payouts on the refund screen and the fully refunded
bill's mark were done in phase 84, and a restaurant bill at another branch in phase 85.

**Carried from phase 84** (§ 5 there): the restaurant bill at another branch, the credit-note page's warehouse and the
till's *Remove* focus were done in phase 85; the money receipt on the roll and the annex exports in BS are still open.

**Carried from phase 85** (§ 5 there): the sale's own fee accounts and the credit-note list's warehouse were done in
phase 86.

**Carried from phase 86** (§ 5 there): every other account on a credit note was done in phase 87 (with debit notes and
payments); an ERP return's own page names its warehouse since phase 89.

**Carried from phase 87** (§ 5 there): the payment GL preview and the Allocate picker were done in phase 88; documents
approved before phase 87 recorded nothing and keep today's rule. A debit note's Inventory Adjustment follows its bill
since phase 95 (debit notes post no rounding); the stock catch-up and journal-voucher allocations stay today's (a
correction made now, and an author's choice of account).

**Carried from phase 88** (§ 5 there): the vendor's per-row *This Allocation* table (a layout choice, not asked
for); a foreign-currency allocation on the vendor is unread. `OutstandingDocumentReader`'s settlement queries were
measured and narrowed by contact in phase 90 (38,015 → 238 reads per picker).

**Carried from phase 89** (§ 5 there): a tax advisor's word on carrying a VAT credit across the fiscal year's end and on
a domestic zero-rated supply; Annex 5's Payment Method, VAT Refund and IRD-sync columns (no backing). The expense's VAT
was read live in phase 90 (as ours) and the return's history read was measured there and left (inside budget, reads
independent of history).

**Carried from phase 90** (§ 5 there): the till refund's owed read was measured and narrowed by the sale in phase 91;
Annex 13's opening and closing balance stay phase 8e's approximation; a tax advisor's word on the annex's before-VAT
basis.

**Carried from phase 91** (§ 5 there): all done (serials on a debit note in phase 92, the till's batch picker in
phase 94, the tenant switches and a serial on the printed bill in phase 95).

**Carried from phase 92** (§ 5 there): all four were done in phase 93 (the walked void, both notes' batch, the
length 400s, the race's 409).

**Carried from phase 93** (§ 5 there): the till's batch picker was done in phase 94; focus after a failed Save or
*Remove line* and the batch price and expiry in phase 95. A partial refund of a sale that walked its batches (now only
one from before phase 94/95) still returns into the oldest-taken batch: no record says which units came back.

**Carried from phase 94** (§ 5 there): both done in phase 95.

**Carried from phase 95** (§ 5 there): answered in phase 96 (2026-10-21). A sale return's rounding follows its
invoice (built); the vendor's print extras (phase 79 § 5) and its per-row allocation table (phase 88 § 5) are
**dropped**, not carried.

**Carried from phase 96** (§ 5 there): the tax advisor answered all three (2026-10-21), built in phase 97: the credit
carries on unchanged, a domestic zero-rated sale has its own return line, and Annex 13 stays before VAT.

**Carried from phase 79** (§ 5 there), last item: the money receipt on the roll was done in phase 98. The rest of
that list was dropped in phase 96 by the user's choice.

**Carried from phase 97** (§ 5 there):

- Whether **exempt** sales should limit the input VAT a month claims: **answered from the statutes (2026-10-29) and
  scheduled as phase 99.** Rule 40(3)–(5): credit only on purchases for taxable supplies, a shared purchase in the month's
  proportion of taxable to total sales; Rule 41's blocked and partial credits beside it. Detail in § 5 there.
- **Free goods** in a discount scheme (the vendor's mock-up has an *Issue Qty*): left out by the user's choice (percent
  only). Start condition: a user asking for "buy N get M free", plus the advisor's word on VAT for free units.

**Carried from phase 62** (§ 5 there):

- Serial-tracked products at the till were done in phase 91.
- The barcode lookup was measured and indexed in phase 95 (11,189 → 50 logical reads on 20,001 products).

**Outside this sequence, each with a start condition:**

- FonePay/NepalPay dynamic QR: merchant credentials, plus phase 22's third-party-data decision.
- IRD CBMS real-time sync: IRD test credentials.
- Delivery partners and their statement: the settlement shape is unread.
- Offline mode: a product decision, which also changes numbering.
- Device binding (OTP per device).

**Also open, not scheduled** (carried from the 2026-09-25 plan):

- Phase 58's carried items (`phase-58-status.md`): the Mode filter on Movement / Ledger / Ageing,
  partial receipts and deliveries, Mark as Delivered / Processed, batch/serial and a per-line
  warehouse on DN/GRN. Phase 64's per-line counters are the same shape.
- The auto-match suggestion engine: a product decision first (the vendor returns
  `account_suggestions: null`).

**Read, and absent, so no session re-opens them:** Recurring Invoices (a route whose server 404s,
with no key among the 166) and `is_bank_user` (a read-only lender's report shell, a different
product). Full text in `roadmap-history.md`.

---

## Outside the sequence — three items a session cannot schedule

Re-confirmed 2026-09-20. Phase 52 had added a fourth (its four unread line types); the 2026-09-20 read
supplied their evidence, so that item is now **phase 54** and has left this list.

**An hour with NVDA**, on Windows, over the live regions, the rich-text toolbar's roving tabindex, the
three phase-46 subscription panels and phase 47's field-level errors. Phases 40 and 47 both built
everything derivable and both recorded plainly that no screen reader was run, because none is
available in this environment; it was not simulated. It is the only thing standing between this
codebase and a finished WCAG 2.1 AA story. **Start condition:** a person with headphones. Record what
is heard verbatim before changing anything — phase 40's own rule, and the reason its first focus sweep
nearly reported an app-wide 2.4.7 failure that did not exist.

**The two traceability reports' real column sets** (51 carried #1). Phase 51 built the Product Batch
Report and the Product Serial No Report from the product detail tabs and the catalogue's filter
lists, because the vendor gates both reports behind permission keys its demo Admin does not hold —
the phase-8f rule, invoked explicitly. One page load each would settle whether this codebase's column
order, grouping and totals match. **Start condition:** an account or tenant holding those two keys.
Ask before falling back again — phase 32's lesson is that a second tenant existed and four written
scope decisions were wrong.

**Full-text search** (42 carried #1). List search on a term matching nothing is fixed — a count of
zero is now a complete answer — but a term matching many rows still costs what a `LIKE` costs. Phase
42 was explicit that the remedy is a *semantics* change, not a performance fix, and so not something
to do because it is next. **Start condition:** a tenant complaining about search latency, or a product
decision about what "search" should mean.

---

## Deferred beyond this roadmap (post-v1 — seams kept, no phases planned)

Explicit decisions (2026-08-18, revised 2026-09-02, 2026-09-10, 2026-09-14, 2026-09-15, 2026-09-16 and
**re-confirmed 2026-09-20**), not omissions. The 2026-09-20 permission-key census touched two of them and
strengthened both: the DN/GRN and marketplace seams are demonstrably real in the vendor, not aspirational.

- **Delivery Note / Goods Received Note (physical-movement inventory).** `TenantSettings.InventoryTrackingMode`
  is the seam. Cadehi's General page offers *Physical Movement*; with Accounting Movement selected no
  DO/GRN appears anywhere, and both tenants still read `inventoryTrackingMode: "Accounting Movement"`.
  **The 2026-09-20 census confirms the feature is fully built behind that flag** — the vendor carries
  `delivery-note-view/add/edit/approve/void` and the same five for GRN, so these are the only two of
  its 22 document types we lack. **Phase 57 adds a third item to this deferral:** its report
  catalogue diff is the **Inventory Variance Report** (`/reports/new/inventory-variance`), which
  opens live as *"Inventory Tracking **and Physical Inventory Tracking** Not Enabled"* — the same
  flag. Scoping this phase from the two document types alone would ship it without the report. **PROMOTED to phase 58 on 2026-09-22 and BUILT** (`phase-58-status.md`) — no longer deferred; the start condition and the FIFO sentence after it are the pre-read plan, and the second is wrong. **Start condition:** the user flips that setting on a tenant and the screens
  are read; then it is a phase of its own (FIFO consumption moves from Invoice/Bill Approve to DO/GRN
  Approve under a handler-level gate, plus a goods-received-not-billed default account).
- **A vendor-side actor** (41 Decision G): every subscription ceiling is self-liftable until an actor
  outside `OrganizationId` exists. A console, a support role, or an API key — a phase of its own.
  Phase 46 confirmed this live on all three metered axes, and phase 47's `LocationQuota` decision
  (Decision H #2) waits on the same thing.
- **Unrealised forex revaluation at period end** (28 Decision A): no revaluation document exists in
  the reference product; only the realised account does.
- **Per-user location assignment** (32b #4): both live tenants' Users screens have no location
  column; the role carries the scope.
- **Multi-level BOM explosion** (25): the live Planning report states "Multiple Level: No".
- **E-commerce / marketplace SKUs** (`marketplace_skus`, `sku_id` on the product JSON; the vendor's
  August 2026 release notes name "e-commerce sales"): a public storefront is a PRD non-goal. The
  2026-09-20 read found the integration is **live, not aspirational** — both tenants call
  `general-settings/daraz/access-token` on every page load — which strengthens the deferral rather
  than reopening it: it is a named third-party marketplace, i.e. exactly the storefront the PRD excludes.
- **POS Retail / POS Restaurant** front-ends (PRD non-goal): Phase 32 models the location *types*
  so a POS phase is additive later. **PROMOTED on 2026-09-28 (phase 59) to phases 60–66**, on a
  live read of a POS-enabled tenant. Phase 32's location *types* turned out to be the wrong axis:
  a POS type is a mode any location has (HeadOffice is typed Retail), so phase 60 replaces them.
- **IRD e-filing integration** (Annex 5's Sync-with-IRD columns): aspirational until committed; the
  Annex reports omit rather than fake those columns (Phase 8f precedent). Phase 59 found the
  vendor's POS carries **IRD CBMS test-server credentials** and an `ird-bills-re-sync` endpoint, so
  the integration is real in the vendor. Start condition unchanged: IRD test credentials.
- **Marketplace / third-party app ecosystem**: a permission flag in the research, nothing more.

## Dropped, with the reason (phase 47, Decision G)

Not deferred — decided against, so that no future session has to re-open them from a list. The full
reasoning and the re-entry condition for each is in `docs/phase-47-status.md`:
`Organization > Developer Mode` (API credential management — a platform feature, confirm-lived in
phase 25), `Organization > Documents` (a bare upload zone over phase 18's `Attachment`),
`Product.PrintProfileId` (live-confirmed to do nothing observable — **reopened by phase 59**: it is
the POS's KOT station), the Marketplace flag, the Service
Charge column (a product flag this codebase does not model, printing `-` on every live row —
**reopened by phase 59**: the POS applies it per location), supplier
credit-limit enforcement (the setting's own wording says Customer), `customtags` in the rich-text
toolbar, and rich-text tables/images (a *renderer* capability before a toolbar one).

## Settled, not carried

Recorded here so no future reading re-opens them: **keyset pagination** was retired rather than
deferred (42 — the tail's cost was the row fetch, and `ToKeyPagedResultAsync` fixes it inside
`PagedResult<T>`); **product-to-location** is a picker filter and always will be (43 #2 — the
reference product saves *and* approves a document naming an out-of-location product); and **a
standalone Credit Note putting no stock back** is deliberate asymmetry, not a divergence, because its
GL posts no Inventory leg either (43 #3).

---
*Living doc — re-order/re-scope as real constraints surface. When picking up work: read its confirmed shape in `erp-module-scan.md` first; if the screen was never opened in the hands-on pass, confirm it against the live Tigg UAT tenant through the Browser pane (user logs in themselves) before writing code — the Phase 8f Annex 5 lesson: the speculative design and the real screen had nothing in common. Every phase ends with its own `phase-N-status.md`; CLAUDE.md's known-gotchas list is the pre-flight checklist for migrations, EF Core LINQ, and Angular selects.*
