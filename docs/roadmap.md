# Build Roadmap — Phases & Task Breakdown

Companion to `architecture-spec.md` (what to build) and `product-requirements.md` (why). This doc says *in what order*, broken down small enough to actually pick up and work. The reference product is a live Tigg UAT tenant; when a screen's shape is unconfirmed, it is read live through the Browser pane before building (the user logs in themselves — credentials are never entered by the agent and never committed to this repo; see `phase-8f-status.md` for the established workflow).

Guiding rule for phase sizing: each phase ends with something *runnable and demonstrable* (an API you can hit, a screen you can click through), not just "code exists." Every phase's exit criteria include: `dotnet build`/`dotnet test`/`ng build`/`ng test` all green; a hand-driven E2E pass against the real API/DB/browser (seed master data via curl + cookie jar, reserve UI clicks for the phase's own new screens); at least one **negative** check (a permission `403` naming the exact key, a lifecycle `409`, or a validation `400`) proven against the real API, not just the happy path; and a `docs/phase-N-status.md` history doc recording scope decisions and bugs before the phase is called done.

---

## Completed phases (0–67)

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

**The POS sequence (60–66) is complete, and so is phase 67**, the ERP invoice and credit-note PDF: the heading
the bill is, in English and Nepali, and every copy counted across the till, the Print button and email, with
copies marked (`phase-67-status.md`). **Next: phase 68, Mark as Take Away and item transfer**, scheduled by the
user on 2026-10-03, with the repricing question answered (below).

**Before phase 68's own work, a small statutory fix (scheduled by the user, 2026-10-03): the buyer's PAN on the ERP
PDF.** VAT Rules Schedule 5 asks a tax invoice for the buyer's PAN when the buyer is registered. The ERP invoice
and credit-note PDFs print the customer's name and address and never the PAN, so a VAT-registered buyer gets an
invoice they cannot claim input tax on. Print "PAN: …" in the customer block **only when the contact has one**.
PAN stays optional on a contact, so a retail customer without one prints exactly as today, and the till already
behaves this way (phase 62: an abbreviated bill omits the buyer block, a named customer gets name, address and PAN
when present). Open question for the tax advisor, not to be guessed: whether IRD expects a buyer PAN on a
high-value sale to an unregistered buyer. If it does, it would be a warning on approve, never a block.

**Also raised 2026-10-03, not yet scheduled: real printing templates.** Phase 20d kept `PrintingTemplate` as a
name and a default flag, by the user's decision; every type prints one shared layout, where the vendor offers
about 20 per type plus a toggle editor. A middle ground is a handful of fixed layouts (Standard, Compact/Retail,
Classic) with per-type toggles for organization fields, custom fields and the date system. The law fixes the
content and the audit trail, not the look, so this is a product choice, not a statutory one.

**Carried from phase 67** (§ 5 there): a standalone ERP credit note names no invoice, which VAT Rule 20(1) asks
for (a product decision: require the reference, or refuse the standalone note on a VAT-registered tenant); the
print race on both print tables is still unraced on SQL Server; the count note beside Print at phone width was
not looked at.

**Carried from phase 66** (§ 5 there): a from/to *time* on the Day Report (a shift across midnight as one report;
a reader change, not a screen change); the vendor's Delivery Partner Statement (delivery partners are outside
the sequence).

**Carried from phases 64 and 65** (§ 5 in each): *Mark as Take Away* on a dine-in line and item transfer
between orders, now read live (`erp-module-scan.md`, "Kitchen board and split"). Both are quantity moves; Mark
as Take Away also asks whether a parcelled dish loses its service charge (phase 64 Decision H) against the
rule that rates never move. **Answered by the user, 2026-10-03:** marking take-away outranks the frozen rate. A
per-location setting, *Service charge on take-away* (On / Off), decides whether the parcelled quantity keeps the
line's service charge. The product's existing `ServiceChargeApplicable` flag still exempts an item on its own,
and the choice is **frozen on the line when it is marked**, so a later settings change reprices nothing. Read
from the vendor's POS bundle the same day: it has a location switch, a per-product flag and a per-line override,
its client never reprices a take-away quantity, and it has **no packaging fee**. A packaging fee (flat line /
percentage / none, per location, inside the VAT base, its own account) was asked for and is **carried
separately**, not part of phase 68. VAT stays in code; only fees are settings. Also: a discount and credit on a
restaurant bill, per-part customers. The live floor shipped in phase 65 (polled).

**Carried from phase 63** (§ 5 there): cross-branch returns; split payouts on the refund screen (the
API takes several); a fully refunded bill still listed in the refund search.

**Carried from phase 62** (§ 5 there):

- **Serial-tracked products** cannot be sold at the till yet: the cart sends no serials.
- **The barcode lookup is unindexed**; measure it on `tools/scale` before indexing.

**Outside this sequence, each with a start condition:**

- FonePay/NepalPay dynamic QR: merchant credentials, plus phase 22's third-party-data decision.
- IRD CBMS real-time sync: IRD test credentials.
- Discount schemes (item / category / slab): after 62's basic discounts.
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
