# Phase 99 — input VAT apportionment (VAT Rules 40 and 41)

## TL;DR

**In plain words, what changed for someone using the app.** Until now the VAT return claimed all the VAT on every
purchase, every month. Nepal's VAT Rules say a business that also makes exempt sales may not, and that some purchases
never give full credit. The app now follows them.

1. **Each purchase line says what it was for.** Purchase bill and expense lines have a *VAT Credit* column with two
   choices:
   - **Use:** *Taxable use* (the default), *Exempt use*, or *Shared* (rent, electricity: used for both kinds of sales).
   - **Rule 41 category:** *Full credit* (the default), *No credit* (beverages, alcohol, vehicle petrol, entertainment),
     *Vehicle, 40%*, or *Computer, 60%*.

   A product carries its own Rule 41 category (*Purchase VAT Credit* on the product form), which fills a bill line when
   the product is picked; the line can change it. Every existing line and product was set to Taxable use / Full credit,
   so a business with no exempt sales sees no change at all.
2. **What can never be claimed is posted as a cost at approval.** The VAT of an exempt-use line, and Rule 41's share of
   any line, goes to the line's own account (Inventory for goods, the purchase or expense account otherwise) instead of
   VAT Receivable. The supplier is still owed the whole bill. For goods, the stock's cost includes it, so the inventory
   value and the Inventory account still agree. The line shows *Not claimable: Rs …* once saved.
3. **A shared purchase is worked out on the return.** Its share depends on the month's sales, so the approval posts its
   whole VAT to VAT Receivable and the VAT return works out the rest each BS month:
   taxable sales (zero-rated and exports included) ÷ total sales, both net of returns.
4. **The return shows the working**, in a new block *Input VAT not claimable (VAT Rules 40 and 41)*, each line in English
   and Nepali: VAT on purchases for exempt sales, VAT not claimable under Rule 41, VAT on shared purchases, the month's
   taxable share (%), the shared VAT not claimable, and the total. *VAT for the month*, *Payable* and the credit carried
   forward now use only the VAT that may be claimed.
5. **The accountant posts the shared part.** The return's *Shared VAT not claimable* is the one figure the app does not
   post; move it from VAT Receivable to an expense with a journal voucher once the month is settled.
6. **Debit notes and voids reverse exactly what the bill posted.** A debit note takes its bill line's use and category,
   as it takes the line's classification, so the VAT that never reached VAT Receivable is credited back to where it went.

| | |
|---|---|
| Live read taken | **no** (statute work, done 2026-10-29 and recorded in `phase-97-status.md` § 5; the vendor has no such feature) |
| Migration | `Phase99InputVatApportionment`: `purchasing.PurchaseBillLines.CreditUse`/`CreditCategory`, `purchasing.ExpenseLines.CreditUse`/`CreditCategory`, `catalog.Products.VatCreditCategory`, all `nvarchar(20)` NOT NULL by name, backfilled `'Taxable'`/`'Full'` (hand-edited from the scaffold's `''`). Applied to the dev DB and read back (§ 4) |
| New permission keys | **none** (the fields ride the existing bill, expense and product keys) |
| Changed endpoints | `POST/PUT /purchase-bills` and `/expenses` lines take `creditUse`, `creditCategory`; both previews take them; `GET` of each returns them with `disallowedVat`; `POST/PUT /products` take `vatCreditCategory`; the PO → bill template carries them; `GET /reports/vat-summary` gains six lines and `isPercent`; its `.xlsx` the same; the full-tenant export's *Purchase Documents* sheet gains *VAT Credit Use* and *VAT Credit Category* |
| Changed screens | Purchase Bill and Expense forms (a VAT Credit column), the product form (*Purchase VAT Credit*), the VAT Summary (the new block) |
| Changed print output | **none** |
| Initial bundle | **656.23 kB** (+0.06, the select floor) against 680 kB |

Tests: Domain **1070** (+15), Application.UnitTests **1925** (+10), Infrastructure.UnitTests 14,
Api.IntegrationTests 80, Angular **938** (+3). All green with Docker up.

## 1. What shipped

- **Domain.** `VatCreditUse` (Taxable, Exempt, Shared) and `VatCreditCategory` (Full, Blocked, Vehicle, Computer), both
  member 0 = the default. `VatCredit` is the arithmetic, in one place: `DisallowedAtPosting(vat, use, category)` (exempt
  use: all; else Full 0, Blocked all, Vehicle 60%, Computer 40%, rounded to the paisa once; Full and Blocked never
  rounded, because a line's VAT is stored at four places), `TaxableShare` (held between 0 and 1; no sales = 1) and
  `SharedDisallowed`. `PurchaseBillLine` and `ExpenseLine` store the two terms and expose `DisallowedVat`;
  `Product.VatCreditCategory` has its own mutator (`SetVatCreditCategory`), phase 70's rule for a field that must not be
  reset by callers that do not know it. A variant starts with its parent's.
- **Posting.** `PurchaseBillPostingLineInput` and `ExpensePostingLineInput` carry `DisallowedVat`. The three rules debit
  (or, for the debit note, credit) each line's account with `Amount + DisallowedVat` and VAT Receivable with the rest;
  Payable is untouched. The approval works the figure out on the **base-currency** VAT, exactly as the readers do, so the
  return and the ledger cannot differ by a paisa. A goods line's FIFO layer is built from `Amount + allocated additional
  cost + disallowed VAT`, so the layer, the movement and the Inventory account agree (phase 37's three views). A line with
  nothing disallowed is costed by the old expression, unchanged.
- **Debit notes.** `PurchaseLineVatCredit.ForDebitNoteLine` finds the bill line by (product, rate, VAT rate), the key the
  classification has used since phase 19. Approve, the Purchase Return reader and the export all call it. A note line
  with no match (a standalone note, or a converted note whose rate was changed) is Taxable with **its product's own
  category**: Rule 41 is about the goods, so the product knows it; the use is the bill's to say.
- **Previews.** Both GL previews resolve the terms exactly as Create does (the line's category, else the product's), so
  a preview posts what Approve will (phase 88). Pinned by a test.
- **The VAT return.** `PurchaseBookReader` and `PurchaseReturnReader` give each row a `CreditFigures` (exempt-use VAT,
  Rule 41 VAT, shared claimable VAT). The handler adds them per month (returns negative), derives the share, the shared
  disallowance and the total per column (history months included, so the carry is the allowed credit too), and takes
  the total out of *VAT for the month*. The share line is `IsPercent` with no year total.
- **The UI.** A *VAT Credit* column on both line tables (two native selects bound per option; text and *Not claimable*
  once approved), the product form's select, and the return's new block. Shared labels in `core/purchasing/vat-credit.ts`.

## 2. Scope decisions

All taken before the session by the user (2026-10-29), from the statute findings in `phase-97-status.md` § 5:

- **A. Two terms on each line, defaulting to no change.** Use (Taxable / Exempt / Shared) and Rule 41 category (Full /
  None / Vehicle / Computer). The product carries a default category. Debit notes inherit both from the bill line.
- **B. Rule 41 in this phase**, with the percentages read from the Rule: none for beverages, alcohol, vehicle petrol and
  entertainment; 40% for aircraft and automobiles; 60% for computers.
- **C. What is known at approval is posted at approval**, to the purchase's own account: exempt use and Rule 41. The
  shared part is shown on the return only, for the accountant to post by journal.
- **D. The return shows the working** in its BS month, in English and Nepali; Payable and the carry use the allowed
  credit only.
- **E. Rule 40(5)'s "other method" directed by a Tax Officer is out of scope** (§ 5).

Decided in the session, within those:

- **F. Nothing new is recorded on the bill.** Decision C's "records the accounts" was already true: phase 87 records each
  line's debit account, and the disallowed VAT is a function of the line's stored VAT, use and category, all frozen on an
  approved line. A void mirrors the posted entry; a debit note recomputes the same figure from the inherited terms on its
  own VAT. A stored figure would be a second copy able to disagree.
- **G. The proportion is net of returns, and a month with no sales keeps full credit.** Rule 40(4) names taxable and total
  "transactions", not net or gross; netting matches every other line of the return. With nothing sold there is nothing to
  apportion against, so the share applied is 100% (the same answer as a month with no exempt sales). The share line shows
  the share applied: 100% in such a month, and **0.00%** (a figure, not a nil) when every sale was exempt.
- **H. A return gives back its disallowance in its own month.** A debit note's credit lines are negative in the month it is
  dated; a shared line returned in a later month is apportioned at that month's share. The Rules say nothing on this; it is
  how every other return line already works.
- **I. The Nepali for the six lines is ours**, in the form's own words (कट्टी, करयोग्य, कर छुट): the IRD form has no such
  lines and the vendor no such feature to copy.
- **J. The export carries the terms.** *VAT Credit Use* and *VAT Credit Category* on the Purchase Documents sheet (a debit
  note's inherited, through the same key). `DisallowedVat` is named in `PurchaseExportTests` as derived from them.

## 3. Bugs hit and fixed

1. **Line-table selects squeezed (found in the browser pass).** The new column widened the purchase bill's table at
   1,024 px, and the browser took the room from the selects: VAT showed "1" for 13% (46 px). Phase 95 floored every typed
   box and no select. `styles.scss` now floors `select.form-select` in a `.table-responsive` table at 6.5rem, and the two
   credit selects at 180 px (154 px truncated "No credit (Rule 41)").
2. **The bill's totals sat one column left since phase 51.** The footer's spacer was `colspan="5"` while the line table
   had eight columns after the Item Batch column was added, so *Sub Total*'s figure printed under Classification. With
   the new column the spacer is `colspan="7"`, and the figures sit under Amount again.
3. **A percent line cannot use zero as nil.** The first cut showed a no-sales month's share as nil and the export as blank,
   which would also have hidden a real 0% (every sale exempt). Decision G above.

## 4. Verification

- **Unit tests.** `VatCreditTests` (Domain, +15): every use × category, the four-place VAT, the share's bounds, the
  shared rounding. `InputVatApportionmentTests` (Application, +10): a bill's six lines posted by hand; the product's
  category prefills; preview equals Approve; a converted debit note inherits and nets every account to zero; a goods
  line's layer carries its VAT and a return and a void each take back exactly that; an expense's Rule 41 and its void;
  the return worked by hand (below); a return in the month; no exempt sales means no change; the validators.
  `MigratedRegisterReportReachTests` now expects 100% on its empty months. Angular +3 (the return's percent line; the
  bill line's read-back, prefill and save).
- **E2E on a fresh organization** (*Phase 99 VAT credit 193521*, VAT-registered), seeded through the API, Kartik 2083:
  - Sales 6,000 taxable (VAT 780), 2,000 zero-rated, 2,000 exempt: the share is 8,000 ÷ 10,000 = **80%**.
  - Bill S-1: 1,000 taxable-use, 2,000 exempt-use, 3,000 shared, 4,000 Vehicle, a laptop 5,000 (Computer, from the
    product). Expense E-1: entertainment 500 (No credit), rent 1,000 (Shared). Bill S-2: 1,000 exempt-use and a laptop
    2,000, then returned in full by a converted debit note. Bill S-3, the same lines, approved and voided.
  - Worked by hand: exempt use 260 + 130 − 130 = **260**; Rule 41 312 + 260 + 65 + 104 − 104 = **637**; shared 390 + 130 =
    **520**, of which 20% = **104**; not claimable **1,001**. Month VAT 780 − (2,535 − 390 − 1,001) = **(364)**. The return
    showed exactly these, and its `.xlsx` the same (the share as `80.00%`, no year total; the block bold-totalled).
  - GL in SQL: S-1 debited Purchase Expense 10,572 (the four service Amounts, 10,000, + 260 exempt use + 312 Vehicle),
    Inventory 5,260 (5,000 + 260),
    VAT Receivable 1,118; the expense 565 to Entertainment, 1,000 to Rent and 130 to VAT Receivable. VAT Receivable over
    everything: **1,248** = the return's allowed credit 1,144 + the shared 104 left for the journal.
  - The debit note reversed S-2's Payable (3,390), Purchase Expense (1,130) and VAT Receivable (156) exactly. Its laptop
    left at FIFO's oldest layer, S-1's 5,260 (which carries S-1's own 260), so Inventory −5,260 and Inventory Adjustment
    +3,156: together −2,104, S-2's own debit. That is phase 37's rule (a return relieves what FIFO gives up), not this
    phase's. S-3 and its void net to zero on every account and its layer is gone.
  - Stored rows: S-1's lines read `Taxable/Full`, `Exempt/Full`, `Shared/Full`, `Taxable/Vehicle`, `Taxable/Computer`.
- **A backfilled row**, read in SQL: lines approved before the phase (organizations from phases 97–98) read
  `Taxable`/`Full`, expenses the same; across the database 60,283 bill lines are `Taxable/Full` and 61,409 products `Full`
  (the rest are this phase's E2E rows).
- **403-not-404**, same user, same organization, same run: `PUT /purchase-bills/{nonexistent}` **404** as Admin; moved to a
  role holding only `Purchasing.PurchaseBill.View`, `GET /purchase-bills/{id}` **200** and the same `PUT` **403** naming
  `Purchasing.PurchaseBill.Edit`. From that role `PUT /products/{nonexistent}` was **403** naming
  `Catalog.Product.Manage` and the return **403** naming `Reports.VatSummary.View`. The user was restored to Admin in SQL.
- **Keyboard, in a real browser** (Browser pane, 1,024 px): on a new bill, the product chosen by arrow keys filled
  *Computer, 60%*; Tab reached *VAT credit use* then *VAT credit category* with the app's `#0a58ca` ring; arrows changed
  them to Shared and Vehicle; Enter on *Preview GL Posting* showed Inventory 107.80, VAT Receivable 5.20, Payable 113.00
  (13 × 60% = 7.80 into the stock). The expense form the same, to *No credit (Rule 41)*. The approved bill and expense
  read back their terms and *Not claimable* figures; the product form's select shows the category; the return's block
  rendered as above.

## 5. Left open, and why

- **Rule 40(5): a method the Tax Officer directs** in place of the proportion. Out of scope by the user's decision; a
  business told to use one would post the difference by journal. Start condition: a user with such a direction.
- **The shared part is not posted.** By decision C the app shows it and the accountant journals it each month. If users
  forget, VAT Receivable will read high by the sum of those lines. A month-end "post the shared disallowance" action
  would be its own phase; start condition: a user asking for it.
- **No year-end true-up.** None was found in the Act or Rules 39–41 (§ 5 of phase 97). If an advisor names one, the
  monthly figures are all there to compute it from.
- **A standalone debit note** (no bill) is Taxable use with its product's category; if the goods were bought for exempt
  sales its VAT is credited back to VAT Receivable it never reached. Converted notes, the normal case, inherit exactly.
- **The statutes were read in unofficial translations** (nepalarchives.com, actnepal.com). The user is to show the rule
  numbers to the tax advisor; an IRD directive or a 2083 amendment could change a percentage, which is one line in
  `VatCredit.ClaimableShare`.
- **Free goods in a discount scheme** (phase 97 § 5), unchanged.

## Writes made to the vendor tenant

None.
