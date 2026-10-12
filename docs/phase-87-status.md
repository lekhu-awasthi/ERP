# Phase 87 — what reverses or settles a document uses the accounts it posted to

## TL;DR

**An invoice and a purchase bill now record every account their approval posts to.** Everything that later reverses or
settles them uses those accounts, whatever the tenant's defaults or a product's accounts say by then. Phase 86 did this
for a sale's service charge and packaging fee only. Now:

- An invoice records each line's Sales account, plus Receivable, VAT Payable, COGS and Inventory.
- A purchase bill records each line's debit account (Inventory for goods, else the Purchase account), plus Payable,
  VAT Receivable, TDS Payable, Inventory and Landed Cost Clearing.
- A **credit note** (an ERP return, a till refund, or a price adjustment against a picked invoice) and a **debit note**
  returning a bill reverse into those accounts.
- A till refund's **payout** debits the Receivable the refund credited.
- A **receipt or supplier payment** settles each allocated document out of the Receivable or Payable that document
  recorded. A **later allocation** moves its share there from where the payment's unallocated part was posted.

Until this phase, every one of those documents re-derived its accounts from today's settings. A tenant that moved its
Receivable, Sales or Inventory default left the old account carrying balances nothing would ever clear, while every
total still balanced.

The user's decisions (2026-10-12):

- **A — every account on a credit note**, not only the fees (phase 86 § 5 chose the fees first).
- **B — price adjustments follow their invoice too**, the recommended option. Receivable and VAT come from the picked
  invoice, and each line whose product the invoice had uses that invoice's Sales account; a product it never had uses
  today's.
- **C — debit notes too**, in this phase. The recommendation was to carry it.
- **D — receipts too.** I extended this to supplier payments, as the mirror case, and said so when starting. The
  recommendation was to record it and not fix it.
- Carried from phase 86 Decision B: **no backfill**. An empty column means "today's rule", so every document approved
  before this phase reverses and settles exactly as it did yesterday.

No live read: the vendor's behaviour would not change a rule this codebase already holds, that a reversal follows what
the original posted (phases 16a, 85, 86).

| | |
|---|---|
| Live read taken | **no** |
| Migration | `Phase87RecordedPostingAccounts`: 12 nullable uniqueidentifier columns, no FK, no index, no backfill. `sales.Invoices` (Receivable, VatPayable, Cogs, Inventory), `sales.InvoiceLines.SalesAccountId`, `purchasing.PurchaseBills` (Payable, VatReceivable, TdsPayable, Inventory, LandedCostClearing), `purchasing.PurchaseBillLines.DebitAccountId`, `payments.Payments.UnallocatedControlAccountId` |
| New permission keys | **none** |
| Changed endpoints | none in shape; what `POST .../approve` on invoices, bills, credit notes, debit notes and payments, `POST /pos/refunds`, `POST /pos/sales` and `POST /payment-allocations/apply` **post** |
| Changed screens | Allocate Customer/Supplier Payment: *Apply*, *Cancel* and *Confirm* each hand keyboard focus on instead of dropping it, and each *Apply* names its payment to a screen reader |
| Changed print output | none |
| Initial bundle | **652.58 kB**, unchanged, against 680 kB |

**Three things worth carrying forward.**

1. **"Today's settings" is a hidden input to every reversal.** No test caught it for 80 phases because no test changed
   a default between a document and its reversal. Each test here does exactly that, and checks the result against a
   twin reversed *before* the change: same lines, account for account, and nothing on the new accounts.
2. **One account is the entry it always was.** A payment's control leg splits only when its targets sit on different
   accounts. Every tenant that never moved a default, and every document from before this phase, posts byte for byte as
   before. The whole existing suite passing unchanged, before any new test was written, was the evidence. A split part's
   base amount comes from a running total, so the parts add up to the cash line to the paisa (phase 65's rule), and each
   part books its own exchange difference on its own account.
3. **A control that removes itself, found by driving a flow, not by a sweep.** Apply on the Allocate screen gives way to
   Cancel, and focus fell to `<body>`. It was found only because the keyboard check went through a screen this phase's
   posting runs behind. The fix and its spec focus the control before pressing it (phase 85's lesson).

Tests: Domain 1032, Application.UnitTests **1795** (+13), Infrastructure.UnitTests 14, Api.IntegrationTests 71,
Angular **861** (+2 here; 859 before them, including the user's 4 `nepali-date-pipe` tests committed during the pause),
105 files. `dotnet build`, all four .NET suites (Docker up, one at a time), `ng build` and `ng test` (alone) are green.

---

## 1. What shipped

**Domain**

- `Invoice.ReceivableAccountId`, `VatPayableAccountId`, `CogsAccountId`, `InventoryAccountId` (with phase 86's two fee
  accounts) and `InvoiceLine.SalesAccountId`, set by `Invoice.RecordPostingAccounts(InvoicePostingAccounts)`. This
  replaces phase 86's `RecordFeeAccounts`: draft only, one Sales account per line, and nothing recorded for a leg the
  sale does not post.
- `PurchaseBill.PayableAccountId`, `VatReceivableAccountId`, `TdsPayableAccountId`, `InventoryAccountId`,
  `LandedCostClearingAccountId` and `PurchaseBillLine.DebitAccountId`, set by
  `PurchaseBill.RecordPostingAccounts(PurchaseBillPostingAccounts)`.
- `Payment.UnallocatedControlAccountId`, set by `RecordUnallocatedControlAccount`: where the approval put the part not
  yet allocated.

**Application**

- `InvoiceApprovalPosting` and `ApprovePurchaseBillCommandHandler` record what their resolver resolved, just before
  `Approve`. Each is the one approval path for its type.
- `RecordedSaleAccounts` and `RecordedBillAccounts` read what a sale or bill recorded; a line's account is found by
  product. `InvoiceAccountResolver` and `PurchaseBillAccountResolver` take them as an optional `recorded`: a recorded
  account wins, and only an account neither recorded nor configured is refused.
- `CreditNoteApprovalPosting` loads the note's `RelatedInvoiceId` (a return's `ReferrerId`, an adjustment's
  `AgainstInvoiceId`). It returns `CreditNotePosting(Entry, ReceivableAccountId)`, so a till refund's payout debits the
  same Receivable. `PosSaleCompletion`'s tender entry uses the Receivable its sale just recorded.
- `ApproveDebitNoteCommandHandler` passes its source bill's recorded accounts. Its Inventory Adjustment account stays
  today's: it books this return's own difference from cost.
- `PaymentControlAccounts`: `LoadAsync` (each target's recorded control account), `SplitLinesAsync` (null when every
  target sits on today's default, so `PaymentPostingRule` builds the entry it always did) and `MoveLines` (a later
  allocation's move). `ApprovePaymentCommandHandler` and `ApplyPaymentAllocationCommandHandler` use them. A
  journal-voucher allocation keeps today's control account for its forex.

**Angular**

- `allocate-payment-page`: Apply focuses the document picker, Cancel the row's Apply, and Confirm the page heading
  (`tabindex="-1"`). Each Apply carries a visually hidden payment code.

## 2. Scope decisions

**A–D** are the user's, above.

**E. No backfill** (phase 86 Decision B). Nothing records which account an old document used, and a GL line has no role
to read it back from (phase 86's gotcha).

**F. No foreign keys on the new columns.** Each recorded account is also on a `GlLine` the same approval wrote, and
`GlLines.AccountId` is a Restrict FK, so the account cannot be deleted while the document exists. Twelve FK indexes on
`Invoices`, `InvoiceLines`, `PurchaseBills`, `PurchaseBillLines` and `Payments` would buy nothing. Phase 86's two fee
columns keep the FKs they shipped with.

**G. What stays today's.**

- A debit note's Inventory Adjustment (its own difference from cost).
- A credit or debit note's Rounding (its own figure).
- The stock catch-up entry `StockCostCatchUp` posts (phase 37). That entry is a correction made now, not a reversal.
- A journal voucher's allocation. Its line's account is its author's choice, so there is no recorded control account
  to move from.

**H. A later allocation moves, it does not repost.** The payment's unallocated part sits on
`UnallocatedControlAccountId`. Allocating some of it to a document on another account posts its own two-line entry
between the two (plus that allocation's forex, on the target's account), against the payment, as phase 36's forex
entry does. A payment from before this phase is read as sitting on today's default, as every allocation read it.

**I. The Allocate screen's focus was fixed here**, because the exit bar's keyboard check ran through it.
Its picker still lists every approved invoice of the contact, including ones already settled. That predates this phase
and is § 5.

**J. Permissions: none new.** Every changed path keeps its key: Create and Approve for each document,
`Payments.Payment.Approve` / `.Edit` for approving and allocating payments, and the till's refund keys.

## 3. Bugs found and fixed

1. **Allocate's Apply, Cancel and Confirm dropped keyboard focus** (Decision I). Found in the browser pass; the spec
   fails with the focus calls removed.
2. **Two patch scripts hit known traps.** One anchored LF text against a CRLF file. One went through a Bash heredoc,
   which stripped its backslashes. Each script's anchor assertion stopped it before it wrote anything, and the edits
   were made with the Edit tool.
3. **The shell came up on Node 16.** `nvm use 24.11.0` from PowerShell reported success but did not update that
   session's PATH. A fresh shell saw the junction pointing at 24.11.0, and the Angular runs used it.

## 4. Evidence

**Unit tests**

- `RecordedPostingAccountsTests` (10). Each switches every default (Sales, Receivable, VAT Payable, Purchase, Payable,
  VAT Receivable, Inventory, COGS) to a new account:
  - an invoice and a bill record what they posted;
  - a return after the switch posts what its twin, returned before it, posted, and the new accounts stay at zero;
  - a price adjustment: the Widget line on the old Sales (50), old VAT (6.50) and old Receivable (−66.50); the Gadget,
    which the invoice never had, on the new Sales (10);
  - a sale with nothing recorded: everything on the new accounts;
  - a debit note posts what its twin posted;
  - a receipt split 678 on the old Receivable and 322 on the new, which also records its unallocated account;
  - a one-account receipt is exactly two lines;
  - a later allocation moves 300 from the new Receivable to the old;
  - a supplier payment debits the old Payable;
  - a till refund's sale Receivable nets to zero.
- `MultiCurrencyPostingTests` (+2):
  - a USD receipt split over two invoices on two Receivables books 300 loss for each, and both Receivables end at 0;
  - a USD advance applied later to an invoice on the old Receivable moves 13,000 and clears the 300 there.
- `PurchaseBillLandedCostTests` (+1): a full return after Inventory, Clearing and Payable all moved nets the bill's own
  three to zero, and the new three are never touched.
- `SaleFeeAccountTests` (Domain, rewritten for `RecordPostingAccounts`):
  - every account is recorded;
  - nothing is recorded for a leg not posted;
  - the method is draft only and takes one account per line.
- `allocate-payment-page.spec` (2, new): focus goes picker → Apply → heading, and the request sent is
  `['org-1', 'Payment', 'pay-1', null, 'Invoice', 'inv-y', 35.6]`.
- **Each guards something.**
  - All five uses switched off at once (credit note, debit note, payment split, later move, till payout): 7 of the 10
    new tests fail. The other three (recording, the one-account entry, a sale from before this phase) do not run
    through those paths.
  - The same switch fails both new forex tests and the landed-cost test.
  - The allocate spec fails with the focus calls removed.
  - Each file was restored by hash.

**E2E** (fresh organization `48773714-6a90-4ba4-8a5a-c10160ddbb54`, *Phase 87 Recorded Accounts 132304*, seeded from
nothing as in phases 85 and 86; customer *Acme Retail*, supplier *Global Supplies*; Coke at 60/40 with 13% VAT, Momo):

- **Before the move.**
  - Bills A and B (10 Coke at 40 + VAT, 452 each) and invoices X and Y (4 Coke at 60 + VAT, 271.20 each).
  - Y's twin return (2 Coke) and B's twin debit note (3 Coke).
  - A till sale of a Momo at Lakeside (249).
  - SQL: X records *Accounts Receivable | VAT Payable | Cost of Goods Sold | Inventory*; bill A records *Accounts
    Payable | VAT Receivable | Inventory*, and its line *Inventory*.
- **The move.** Eight "… 2083" accounts are created, and `PUT /accounting-defaults` points every default at them.
- **After the move.**
  - **Return of 2 from X:** −135.60 Receivable, 120 Sales, 15.60 VAT, 80 Inventory, −80 COGS. That is **identical to
    its twin** (the "accounts that differ" query returns none), and none of it is on a 2083 account.
  - **Debit note of 3 from A:** identical to its twin (135.60 Payable, −120 Inventory, −15.60 VAT Receivable).
  - **The Lakeside till refund**, both entries: Receivable 0.00 net on the old account, *Service Charge – Lakeside* 20,
    Sales 200, VAT 28.60.
  - **Invoice Z** (made after the move) posts to the 2083 accounts.
  - **Receipt `0001`, 400:** all of X's remaining 135.60 plus 100 of Z. Cash 400, *Accounts Receivable* −135.60,
    *Accounts Receivable 2083* −264.40 (Z's 100 and the 164.40 left).
  - **Advance `0002`, 200**, then 100 of it applied to Y: −100 on each Receivable, the move.
  - **Supplier payment `0003`, 316.40 to bill A:** *Accounts Payable* 316.40.
- **Balances.**
  - Old Receivable 35.60 (Y's balance); *Receivable 2083* −93.20 (Z's 171.20 less 264.40 of advances).
  - Old Payable −316.40 (bill B's balance); *Payable 2083*, *Purchases 2083*, *VAT Receivable 2083* 0.00.
  - The 2083 Sales, VAT, COGS and Inventory carry only invoice Z.
  - `UnallocatedControlAccountId`: *Accounts Receivable 2083* on the two receipts, *Accounts Payable 2083* on the
    payment.
- **403-not-404** (roles each holding every Admin key but one; the membership moved in SQL and back):
  - `POST /payment-allocations/apply` with a valid body (a real invoice, a nonexistent payment): **404** "Payment not
    found." as Admin; **403** naming `Payments.Payment.Edit` without it, beside a 200 on `GET /payments/{id}`.
  - `POST /credit-notes/{nonexistent}/approve`: **404** as Admin; **403** naming `Sales.CreditNote.Approve` without it,
    beside a 200 on `GET /credit-notes/{id}`.

**Keyboard pass** (`erp-web-ssl`, the cookie through `web/public/p87.txt`, deleted at once; 1440×900; Allocate Customer
Payment). The receipts read "0001 … 400.00 235.60 164.40" and "0002 … 200.00 100.00 100.00".

- **Before the fix:** Tab ×34 to *Apply* (checked), Enter, and focus was on `<body>`.
- **After it:**
  - Tab ×34 to *Apply 0001* (checked), Enter: focus on the document picker.
  - Down ×2 to `0002`, Tab, Ctrl+A, `35.60`, Tab to *Confirm* (checked), Enter.
  - Sent (read from a logging wrapper): `POST …/payment-allocations/apply {"sourceType":"Payment","sourceId":"e45d…",
    "parentDocumentId":null,"targetDocumentType":"Invoice","targetDocumentId":"bc2d…","amount":35.6}`.
  - Focus moved to the heading (ring visible); the banner says "Applied 35.60 from 0001."; the row reads 271.20
    allocated, 128.80 left.
  - SQL: a second entry on the receipt, *Accounts Receivable 2083* debit 35.60 and *Accounts Receivable* credit 35.60.
    The old Receivable is now **0.00**, with everything sold before the move settled. The new one is −57.60.

No PDF changed this phase.

## 5. Left open, and for later phases

- **Documents from before phase 87** recorded nothing and keep today's rule (Decision E).
- **The payment GL preview** (`PreviewPaymentGlPostingQuery`) takes no allocations, so it shows one control line and no
  forex, as it always did. Approve may now post a split it does not show.
- **What stays today's** (Decision G): Inventory Adjustment, Rounding, the stock catch-up, journal-voucher allocations.
- **Expenses** record nothing. Nothing reverses an expense but its Void, which mirrors its posted lines, and payments
  cannot be allocated to one.
- **The Allocate screen's picker** lists every approved invoice or bill of the contact, settled ones included (Decision I).
- **Carried unchanged:** phase 86 § 5's ERP return page warehouse; phase 84 § 5's money receipt on the roll and the
  annex exports in BS (phase 79 § 5, phase 71 § 5); phase 83 § 5, phase 81 § 5, phase 80 § 5, phase 79 § 5's other
  items, phase 77 § 5, phase 76 § 5, phase 75 § 5, phase 72 § 5, phase 71 § 5's other items, phase 70 § 5, phase 69 § 5,
  phase 67 § 5, phase 66 § 5 and phase 62 § 5.

## Writes made to the vendor tenant

None. The vendor was not opened this phase.
