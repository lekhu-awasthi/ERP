# Phase 16b status — Discounts retrofit

**Status: COMPLETE.** Line-level and transaction-level percentage discount added to all seven
Product-line document types (Quotation, SalesOrder, Invoice, CreditNote, PurchaseOrder,
PurchaseBill, DebitNote) — FR-5.1, deferred since Phase 8b (scope decision #3 there explicitly
left `InvoiceLine`/`CreditNoteLine`/`PurchaseBillLine`/`DebitNoteLine` with no discount fields at
all). Confirmed live against the reference product before writing any GL code: discount reduces
the taxable base before VAT, and nets directly into Sales Revenue / Purchase Expense with **no
separate Discount account** — this one finding is why `InvoicePostingRule`/`CreditNotePostingRule`/
`PurchaseBillPostingRule`/`DebitNotePostingRule` needed **zero changes**.

## Roadmap/brief exit criteria — final status

- [x] Line-level `DiscountPct` on all 7 line types, transaction-level `DiscountPct` on all 7
      header aggregates; totals math (line discount, then header discount, then VAT) confirmed
      live and re-derived with both non-zero (worked example below)
- [x] GL posting rules re-verified with discount as a variable — no code changes needed, since
      `Line.Amount`/`VatAmount` are stored fully-netted (see Design decision #1)
- [x] Conversion-cap enforcement: the `(ProductId, Rate, VatRate)` match triple grew a fourth
      component, `DiscountPct` — plus a document-level header-`DiscountPct` equality check (see
      Design decision #3)
- [x] Master Reports gain `ItemDiscount`/`TransactionDiscount`/`NetSales` columns exactly as named
      in `erp-module-scan.md:278`; VAT Summary/Annex 5/Annex 13 re-verified by hand (grep-confirmed
      none of them recompute `Quantity*Rate` — all read `Amount`/`VatAmount` as stored, already
      correct)
- [x] `dotnet build`/`dotnet test` (Domain.UnitTests 76 — 9 new + 67 pre-existing,
      Application.UnitTests 199 — 5 new + 194 pre-existing, all green) and `ng build`/`ng test` (7
      pre-existing Angular specs green, no new Angular specs — see Design decision #5) all pass
- [x] Confirmed by hand end-to-end against the real API/DB (see "Manual E2E" below)

## Design decisions

1. **`Line.Amount`/`Line.VatAmount` (stored) are fully netted — both the line's own `DiscountPct`
   and the header's `DiscountPct` folded in, VAT computed on what's left.** This was the single
   decision that made every other layer's discount work a non-event: `GlJournalEntry` posting
   rules, `SalesMasterReportQuery`'s VAT columns, `VatSummaryReportQuery`, `AnnexFiveReportQuery`,
   `AnnexThirteenReportQuery` all already read `Amount`/`VatAmount` as opaque already-computed
   values (confirmed by grep — none of them recompute `Quantity*Rate` independently) and are
   therefore correct with zero code changes. The formula, in `XLine.Create` (e.g.
   `InvoiceLine.Create`):
   ```
   GrossAmount = Quantity * Rate
   NetAfterLineDiscount = GrossAmount * (1 - LineDiscountPct/100)
   Amount = NetAfterLineDiscount * (1 - HeaderDiscountPct/100)   // stored
   VatAmount = Amount * VatRate.ToPercent()                       // stored
   ```
   The cost: the per-line "Amount" column the reference product's UI shows (and this codebase's
   Angular line-item table shows) is `NetAfterLineDiscount` — header discount deliberately never
   touches it, matching the confirmed-live behavior where the header Discount% input only moved the
   Totals-panel rows, never the per-line Amount cell. Angular's line-item table already computed
   this client-side from raw `quantity`/`rate` rather than reading a stored DTO field (pre-existing
   pattern, not something this phase introduced), so extending it to
   `quantity * rate * (1 - discountPct/100)` required no architecture change — just a formula
   change in an existing client-side expression.

2. **No dedicated Discount GL account — confirmed live, not assumed.** Read live against the
   reference product's own tenant (`moonbeamtradingandsuppliers.tigguat.com`, per CLAUDE.md's
   confirm-live-before-coding discipline): created a test Invoice with a 10% line discount and a 5%
   header discount (Qty 10 × Rate 1,000 → Sub Total 9,000 → Discount 5% (450) → Taxable Total 8,550
   → VAT 13% of 8,550 = 1,111.50 → Grand Total 9,661.50), approved it, and read the GL Transactions
   panel: **`Sales Goods` credited 8,550.00, `VAT 13%` credited 1,111.50 — only two lines, no third
   Discount line.** Re-confirmed the identical structure against this codebase's own dev API/DB (see
   Manual E2E) for both the Invoice→Sales-Revenue and PurchaseBill→Purchase-Expense sides. This
   settles the brief's open question definitively in favor of option (b): discount nets straight
   into revenue/expense, no `DefaultDiscountAllowedAccountId`/`DefaultDiscountReceivedAccountId`
   pair needed on `TenantSettings`.

3. **Conversion-cap key grew a 4th component (line `DiscountPct`), plus a new document-level header
   `DiscountPct` equality check — both enforced, the second one is new plumbing, not just a key
   extension.** `SalesValidation.GetInvoiceRemainingByLineAsync`/
   `EnsureCreditNoteLinesWithinInvoiceRemainingAsync` (and the `PurchasingValidation` mirrors) key
   remaining quantity by `(ProductId, Rate, VatRate, DiscountPct)` now — a CreditNote/DebitNote line
   can only match a line that was actually invoiced/billed at that exact combination, the same
   "can't invent a cheaper/differently-taxed line" reasoning `docs/phase-6-status.md` established,
   extended to discount. The header-level check is separate and was *not* obviously covered by the
   per-line key extension: since `Line.Amount` folds in the header discount too, a CreditNote citing
   every correct per-line `(ProductId, Rate, VatRate, DiscountPct)` combination could still credit
   the *wrong* `Amount` if its own header `DiscountPct` differs from the source Invoice's — so
   `EnsureCreditNoteLinesWithinInvoiceRemainingAsync`/`EnsureDebitNoteLinesWithinPurchaseBillRemainingAsync`
   now also assert `invoice.DiscountPct == discountPct` (mirroring the existing `ContactId`/
   `TdsTypeId` equality checks already there), throwing the same `ConflictException` class. Verified
   by hand this is a real, not theoretical, gap: proven via both a unit test
   (`InvoiceDiscountTests.CreditNote_with_a_different_header_discount_than_the_source_invoice_is_rejected`)
   and a real `409` against the live dev API (see Manual E2E).
   `GetCreditNoteConversionTemplateQuery`/`GetDebitNoteConversionTemplateQuery` pre-fill both the
   header and every line's `DiscountPct` from the source so the Angular "Convert to X" flow produces
   a passing request by default, same as it already did for Rate/VatRate/TdsTypeId.

4. **PurchaseBill/DebitNote's TDS base switched from `Quantity*Rate` (pre-discount) to the fully
   discounted per-line `Amount`.** TDS is a withholding on the taxable purchase value, the same base
   VAT uses — computing it pre-discount would overstate TDS. Fixed in
   `CreatePurchaseBillCommandHandler`/`UpdatePurchaseBillCommandHandler`/
   `CreateDebitNoteCommandHandler`/`UpdateDebitNoteCommandHandler` (the `tdsBaseAmount` expression)
   and in `PreviewPurchaseBillGlPostingQueryHandler` (so the pre-Approve GL preview matches what
   Approve actually posts). Confirmed by hand: a 1.5% TDS type against a Qty 10 × Rate 1,000 line
   with 10% line + 5% header discount computed `TdsAmount = 128.25` (1.5% of the 8,550 discounted
   base), not `150.00` (1.5% of the undiscounted 10,000) — see Manual E2E.

5. **No new Angular unit tests added.** Matches the pre-existing precedent (Phase 5's scope
   decision #6, reused by Phase 6 and Phase 16a) — this codebase's Angular test suite has stayed at
   7 specs since Phase 5, with UI correctness proven by manual E2E/browser verification instead of
   component specs. The 6 updated detail pages (Quotation/Invoice/CreditNote/PurchaseOrder/
   PurchaseBill/DebitNote) all follow the identical `subTotal`/`discountAmount`/`nonTaxableTotal`/
   `taxableTotal`/`vatTotal`/`grandTotal` computed-signal chain, verified by hand-tracing the same
   worked example (Qty 10 × Rate 1,000, 10% line + 5% header discount → 9,661.50) through each
   page's formula. SalesOrder has no Angular UI at all (confirmed by the exploration pass — no
   `sales-order-detail-page` component exists anywhere in `web/src/app/features`, a pre-existing gap
   unrelated to this phase), so its discount plumbing exists only through the API/backend.

6. **Angular's `EditableLine`/request-shape `discountPct` field is non-optional (`number`, not
   `number | undefined`), always sent explicitly** (defaulting to `0` via `newLine()`/template
   pre-fill), rather than an optional field relying on the backend's `DiscountPct = 0` C# default
   parameter. Keeps the two layers' contracts unambiguous — the wire payload always states its
   discount, never relies on omission-means-zero on either side.

## Bugs hit and fixed

No shipped-code defects found by the test suite or manual E2E this phase — the design decisions
above (particularly #1's "fold both discounts into the stored Amount" choice) were worked out
*before* writing GL/report code specifically to avoid the class of bug Phase 6's bug #3 and Phase
16a's own precedent warn about (a reversal that balances its own entry while leaving a paired
account permanently unbalanced). Two environment/tooling snags during manual E2E, neither a
shipped-code bug:

1. Sending an empty string `""` for a nullable `Guid` request field (e.g. `referrerId`) returns a
   raw `500` instead of a clean `400` — a pre-existing ASP.NET Core JSON-model-binding gap affecting
   every nullable-Guid field across the whole API, not specific to this phase's new fields (a
   malformed test input surfaced it, not a real Angular-originated payload — the client always
   sends `null`, not `""`). Flagged as a separate background task rather than fixed inline, since
   it's out of this phase's scope.
2. A false-positive during browser-driven UI verification: converting an Invoice to a CreditNote,
   then immediately taking a screenshot and re-reading the page tree before clicking Save Draft,
   once produced a saved Qty that didn't match the pre-filled value (5 became 2 on one run, and a
   separate run showed a stray "15" in the Qty field's accessible-name reading before any save).
   Reproduced clean with a fresh Invoice, a fresh browser tab, and a minimal click-convert →
   read-fields → click-save sequence (no extra screenshots/reads in between) — the saved
   `CreditNoteLineDto.quantity` matched the pre-filled value exactly (`8.0000`, `discountPct 20`,
   `amount 1728.0000`, `vatAmount 224.6400`, all matching hand arithmetic). Confirmed this was
   browser-automation keystroke/state bleed from the test session itself (extra `type` actions
   issued earlier in the same tab against an unrelated `New Invoice` draft), not an Angular defect
   — the discrepancy never reproduced once the interaction sequence was minimized, and the backend
   (checked directly via `GET /credit-notes/{id}`, bypassing browser rendering) always held the
   correct value. Worth remembering for the next phase's manual E2E: prefer a fresh tab and the
   fewest possible interactions between a form's pre-fill and its save when verifying an
   auto-populated numeric field, rather than interleaving screenshots/reads that give stray
   keystrokes a chance to land.

## Manual E2E

Confirmed by hand end-to-end against the real API/DB, seeded via curl + a cookie jar per this
session's own memory note (registered/verified via `sqlcmd`-read `VerificationCode` per
`docs/phase-14-status.md`'s recipe, logged in, created an Organization, chart of accounts, a
Warehouse, a Customer, a Supplier, a Service product at 13% VAT, and a 1.5% TDS type):

- Created an Invoice with one line (Qty 10, Rate 1,000, line `DiscountPct` 10) and header
  `DiscountPct` 5. `GET` back confirmed `Amount 8,550.00`, `VatAmount 1,111.50`,
  `GrandTotal 9,661.50` — the exact hand-worked figures, through the real HTTP API.
- Approved it; `sqlcmd` against `accounting.GlLines` showed exactly 3 balanced lines: `Accounts
  Receivable` debit 9,661.50, `Sales Revenue` credit 8,550.00, `VAT Payable` credit 1,111.50 — no
  Discount account line, matching the reference-product finding exactly.
- Converted to a CreditNote (`GET .../credit-note-conversion-template` correctly pre-filled header
  `discountPct: 5` and line `discountPct: 10`), approved it; `sqlcmd` summed both documents' GL
  lines per account and confirmed **every account nets to exactly zero** across the pair
  (`Accounts Receivable`/`Sales Revenue`/`VAT Payable` all `0.0000`).
- `GET` the Sales Master Report for the period: `amount 9,000.00` (net of line discount only),
  `itemDiscount 1,000.00`, `transactionDiscount 450.00`, `netSales 8,550.00`, `vatAmount 1,111.50`,
  `totalAmount 9,661.50` — matches hand arithmetic exactly, for both the Invoice row and the
  CreditNote row.
- Negative check #1: `POST /invoices` with header `discountPct: 150` → real `400` from
  FluentValidation (`'Discount Pct' must be between 0 and 100. You entered 150.`).
- Negative check #2: created and approved a second Invoice (header `discountPct: 5`), then
  `POST /credit-notes` against it with header `discountPct: 0` (everything else matching) → real
  `409` (`"A credit note converted from an Invoice must keep the same transaction-level Discount%
  as the source invoice."`) — proving Design decision #3's new header-equality guard actually fires
  against the real API, not just in a unit test.
- Purchase side: created and approved a PurchaseBill with the identical Qty 10 × Rate 1,000 /
  10%-line / 5%-header discount shape plus a 1.5% TDS type. `GET` confirmed `TdsAmount 128.25`
  (1.5% of the discounted 8,550 base, not 150.00 pre-discount). `sqlcmd` confirmed a balanced
  4-line GL entry: `Purchase Expense` debit 8,550.00, `VAT Receivable` debit 1,111.50, `TDS
  Payable` credit 128.25, `Accounts Payable` credit 9,533.25 (`= 9,661.50 - 128.25`) — Purchase
  Expense debited at the discounted taxable amount, mirroring the Sales side exactly, no separate
  Discount account there either.
- Angular UI, live in the browser (`ng serve` + the real API, per the standing "start the dev
  server and use the feature in a browser" rule): opened the Approved Invoice above and confirmed
  the Totals panel/GL Transactions panel render the exact figures above; on a fresh `New Invoice`
  draft, typed Qty 10 / line Discount% 10 / header Discount% 5 and watched the Totals panel update
  live to the exact hand-worked figures as each field was typed (Sub Total 9,000 → Discount
  (450.00) → Taxable Total 8,550 → VAT 1,111.50 → Grand Total 9,661.50); used "Convert to Credit
  Note" on a second Approved Invoice and confirmed the CreditNote form pre-filled with every field
  locked (Product/Rate/Discount%/VAT) except Quantity, matching the "Bugs hit and fixed" item #2
  investigation above; saved and approved a third Invoice→CreditNote pair entirely through the UI
  (Qty 8, Rate 300, line discount 20%, header discount 10%) and confirmed via both the CreditNote's
  own detail page and `sqlcmd` that it posted the correct reversed GL entry and the pair netted to
  exactly zero.

## Ripple effects checked, no change needed

- `InvoicePostingRule`/`CreditNotePostingRule`/`PurchaseBillPostingRule`/`DebitNotePostingRule` —
  all consume already-resolved `Amount`/`VatAmount` from `*PostingLineInput` records; those come
  straight from `Line.Amount`/`Line.VatAmount` (Design decision #1)
- `ApproveInvoiceCommandHandler`/`ApprovePurchaseBillCommandHandler` (the real Approve-time GL
  posting path) — read `invoice.Lines.Select(x => (x.ProductId, x.Amount, x.VatAmount))` directly
  off the persisted entity, no changes
- `VatSummaryReportQueryHandler`/`AnnexFiveReportQueryHandler`/`AnnexThirteenReportQueryHandler` —
  grep-confirmed none recompute `Quantity*Rate`; all project `Amount`/`VatAmount` directly
- `PreviewInvoiceGlPostingQueryHandler`/`PreviewPurchaseBillGlPostingQueryHandler` — these *did*
  need changes (they recompute `Amount` from raw `Quantity*Rate` independently of the Domain layer,
  since they preview a not-yet-saved draft) — both updated to apply the identical line-then-header
  discount formula, and both endpoints/Angular service calls extended to carry `discountPct`

## What's next

Phase 16c (pagination/export) is next per the roadmap; Phase 16d (audit trail) and multi-currency
remain sequenced after that. Out of scope, untouched by this phase as planned: Expense,
JournalVoucher, CashTransfer, WarehouseTransfer, InventoryAdjustment, Payment line shapes (none of
them price a Product line the way the seven discount-bearing types do).
