# Phase 85 — a restaurant bill refunded at another branch, the refund's warehouse on the credit note, and focus on a removed tender

## TL;DR

**Another branch now refunds a restaurant bill.** Phase 84 let a till refund another location's retail sale and refused a
bill carrying a service charge or packaging fee (a 409), because both sit in per-location accounts. Now such a bill comes
back like any other. Its service charge and packaging fee are reversed out of the accounts **the sale put them in** (the
selling location's, through `PosAccountResolver`). Everything else is the refunding branch's, as in phase 84: the note's
number, session and drawer, its round-off, the warehouse its goods go into, and the location stamped on its entries.
Two small leftovers from phase 84 § 5 were closed with it. The ERP credit-note page names the warehouse a till refund's
goods went back into. Removing a tender on the till and on the restaurant bill screen no longer drops keyboard focus.

The vendor has no cross-branch refund at all (phase 84 § 0, read live in both tills), so there was nothing to confirm
live. The user chose the posting shape (2026-10-12), the recommended option:

- **A — one entry at the refunding branch, the fees out of the selling branch's accounts.** The entry is stamped with the
  branch that refunded (phase 84 Decision D: where the money left), and its service-charge and packaging-fee lines name
  the accounts the sale credited. Its account lines are therefore **identical** to the same refund made at home; only
  the stamp differs, which is phase 84's twin test unchanged. A refunding branch that charges no fees needs no account
  for them. Two entries (the fee legs stamped with the selling branch) and the refunding branch's accounts (phase 84's
  rejected option) were offered and not chosen.

| | |
|---|---|
| Live read taken | **no**: the vendor has no cross-branch refund (phase 84 § 0 read both tills, location-pinned) |
| Migration | **none** |
| New permission keys | **none**. An away restaurant bill is refunded under `Sales.CreditNote.Create` + `.Approve` at the till, as an away retail sale is |
| Changed endpoints | `GET /pos/tills/{loc}/sales` drops `refundableHere` (every sale it finds can be refunded there); the refundable read, preview and refund no longer 409 on an away sale with fees; `GET /credit-notes/{id}` adds `posRefund.warehouseName` |
| Changed screens | the refund page (a restaurant bill from another branch has a Refund button, no "Refund it at X"); the credit note's *Refunded at the till* panel ("Goods returned into X"); the till's and the restaurant bill's tender lists (focus and an announcement on *Remove*) |
| Changed print output | none |
| Initial bundle | **652.58 kB**, unchanged, against 680 kB |

**Three things worth carrying forward.**

1. **"Two locations in one entry" was a question about accounts, not about the entry.** Phase 84 recorded that reversing
   the fees elsewhere would put two locations in one entry, which nothing posts. The location an entry carries is one
   stamp (`GlJournalEntry.LocationId`), and phase 84 had already decided it is the refunding branch's. A per-location
   account is a different thing: the place a figure is kept. Reversing a figure means taking it back out of the account
   it went into. Once the two were separated, the change was one argument: which location the account is resolved for.
2. **A reversal resolves an account where the original posted, not where it is reversed.** `CreditNoteApprovalPosting`
   now resolves the service-charge and packaging-fee accounts at the sale's location (`SoldAtLocationAsync`). At home that
   is the note's own location, so every phase 63–84 refund posts exactly as before. The round-off stays the note's,
   because the note rounds its own figure.
3. **A focus test passes without the fix if focus never left.** The first till spec for *Remove* passed with the fix
   commented out: opening Pay already focuses the amount field, and a scripted `.click()` moves no focus, so focus never
   left. A real keyboard user's focus is on the button that disappears. The spec now focuses the button, asserts it, then
   presses it, and fails without the fix.

Tests: Domain 1029, Application.UnitTests **1780** (+2: three new, one phase 84 refusal test retired),
Infrastructure.UnitTests 14, Api.IntegrationTests 71, Angular **854** (+2, 102 files). `dotnet build`, all four .NET
suites (Docker up, one at a time), `ng build` and `ng test` are green.

---

## 1. What shipped

**Application**

- `PosRefundPlanner.EnsureRefundable`: the phase 84 refusal of an away sale with a service charge or packaging fee is gone.
- `CreditNoteApprovalPosting`: the service-charge and packaging-fee accounts are resolved at the location of the invoice
  the note returns (`SoldAtLocationAsync`: the loaded source invoice, else a one-column read of the referrer, else the
  note's own location). Round-off and everything else unchanged.
- `FindPosSalesQuery`: `PosSaleMatchDto.RefundableHere` removed, with the `Fees` sum that fed it. A field that is always
  true says nothing.
- `GetCreditNoteQuery`: `CreditNotePosRefundDto.WarehouseName`, the warehouse the refund's goods went back into (the
  note's `WarehouseId`, else its sale's, which is where every refund before phase 84 put them). Null when no line is
  Goods, because then nothing went back into stock anywhere.

**Angular**

- `pos-refund-page`: the "Refund it at X: it carries a service charge or packaging fee" branch is gone; `PosSaleMatch`
  loses `refundableHere`.
- `credit-note-detail-page`: "Goods returned into X" under the session line of *Refunded at the till*.
- `pos-till-page` and `pos-bill-page`: `removeTender` prefills the amount with what is left to pay, announces
  "Card 18.00 removed." in the existing live region, and hands focus to the amount field (`#pos-tender-amount` /
  `#pos-bill-amount`), the shape phase 84 gave the refund screen's *Remove*.

## 2. Scope decisions

**A. The posting shape** (the user's, above). One entry, stamped with the refunding branch; the fee lines name the
selling branch's accounts. The alternatives:

- *Two entries*, the fee legs and their share of the customer's credit stamped with the selling branch. A branch-filtered
  income statement would show the selling branch's fees reversed there. But the revenue reversal would still be at the
  refunding branch (phase 84 Decision D), so one refund would be split between two branches by a rule only the fees
  follow. Void would have handled it (`ReverseOutstandingAsync` groups by location), so it was possible, just not more
  true.
- *The refunding branch's accounts*: phase 84's rejected option. It leaves the selling branch's account in credit and
  this one's in debit by the same amount, and it makes a shop that charges no service charge configure one to take a
  refund.

The accepted cost: a report filtered to the refunding branch shows a line on the selling branch's fee account, exactly as
it already shows the revenue reversal of a sale it never made.

**B. Which account, when the sale's location changed its settings since.** The fee accounts are resolved from the selling
location's *current* settings, not read off the sale's own GL lines. That is what a refund at home has always done
(phase 61/70), and the twin rule says an away refund must leave what the home refund leaves. If a branch changes its
Service Charge account between a sale and its refund, both a home and an away refund debit the new account. Recorded,
not changed: changing it would change every home refund since phase 63.

**C. `RefundableHere` removed, not left true.** It existed to say "not here"; after this phase nothing makes a found sale
unrefundable at the till that found it. A permanently true field reads to a client as a rule that might fire.

**D. The credit note's warehouse: the effective one, Goods only.** A note from before phase 84 stores no warehouse, and
its goods went into the sale's; the page names that rather than nothing. A refund of services only names none, because
"returned into" a warehouse would be false.

**E. Permissions: none new.** The away restaurant refund is the phase 84 away refund with more on the bill. Its keys are
the refund's own at the till (`Sales.CreditNote.Create` in the pipeline, `.Approve` at the location in the handler). The
credit-note page's new line rides on `Sales.CreditNote.View`. Per the derivation rule: routine daily-use working data.

## 3. Bugs found and fixed

1. **A focus spec that could not fail.** TL;DR point 3. Found by injecting the regression (removing the focus calls) and
   watching the till's spec stay green while the bill page's failed. The bill page differs because its amount field is
   not focused on load. Both specs now focus the *Remove* button first.
2. **A spec patch script lost its backslashes again.** An injection script written through a Bash heredoc had its `\n`
   turned into raw newlines (the known heredoc gotcha), so its anchor assertion stopped it before it changed anything.
   Rewritten with the Write tool. No source file was touched by the failed run; both were restored and checked by hash
   anyway.

## 4. Evidence

**Unit tests** (`PosCrossBranchRefundTests`, now 12; `pos-till-page.spec`, `pos-bill-page.spec`, `pos-refund-page.spec`):

- *the restaurant twin*: two identical restaurant bills at home (a Momo eaten in, a Momo taken away: 40 service charge,
  20 packaging fee, 520). Each branch has its own fee accounts, and the tenant defaults are a third pair. One parcel Momo
  is refunded at the branch, the other at home. The total, round-off, payout and GL lines are identical. The selling
  branch's two accounts gave back 20 each. The branch's own pair and the defaults stay at 0. Every entry of the away
  refund is stamped with the branch. The rest of each bill comes back at home and ties out to 520, with the fee accounts
  and Receivable at 0 and the search marking it fully refunded. Voiding the away refund puts 20 back into each of the
  selling branch's accounts, still under the branch's stamp;
- a shop branch with service charge and fee off and no accounts for either (here or as a tenant default) refunds the
  bill, and the selling branch's accounts take it back;
- the credit note's `WarehouseName`: an away refund names the branch's store, a home refund the main store, a Momo-only
  refund null, and a home refund with its `WarehouseId` cleared (as before phase 84) still names the main store;
- both new tests fail with the old account rule injected (`creditNote.LocationId`), and pass restored;
- the search test no longer reads `RefundableHere`;
- the till and the bill page: *Remove* on a focused button leaves focus in the amount field, prefilled with what is left
  (116.00 of 316 after Cash 200; the bill's due less 50), announces the removal, and the till then sends exactly
  `[Cash 200, Cash 116]`. Both fail with the focus calls removed;
- the refund page: an away retail row and an away restaurant row each have a Refund button, the fully refunded row none,
  and "Refund it at" appears nowhere.

**E2E** (fresh organization `595cc352-5668-4b50-9188-aab1d1635a51`, *Phase 85 Branches 083422*, seeded from nothing:
five account groups and 23 accounts including *Service Charge – Thamel*, *Packaging – Thamel*, the same two for Lakeside,
and a default pair; HeadOffice (Restaurant, Main Store, 10% service charge on take-away too, a 20-per-unit packaging fee
into the Thamel accounts) and Lakeside `LKS` (Retail, Lakeside Store, the same rates into the Lakeside accounts); Cash and
Card; Chicken Momo (service, service-charged) and Coke (goods, 50 opening at 40); a session at each):

- Bills `0001` (T1) and `0002` (T2) at HeadOffice, each 2 Momo (one marked take-away) and a Coke: 587.60 → **588**,
  service charge 40, packaging fee 20, round-off 0.40.
- Lakeside, `search=0001`: one row, `soldAtLocationName` HeadOffice, `fullyRefunded` false, **no `refundableHere`**. The
  read: `HeadOffice | Lakeside Store | canRefund True` (phase 84: a 409).
- The parcel Momo and the Coke of `0001` refunded at Lakeside and of `0002` at HeadOffice: each **339.00** (service
  charge 20, fee 20, round-off 0), paid out in cash; credit notes `0001` (Lakeside, Lakeside Store, SES0002) and `0002`
  (HeadOffice, Main Store, SES0001).
- `GET /credit-notes/{id}`: `posRefund.warehouseName` Lakeside Store away, Main Store at home.
- **SQL, the twin:** the two notes' GL by account is identical. Receivable 339/339, Cash −339, COGS −40, Inventory 40,
  *Packaging – Thamel* 20, Sales 260, *Service Charge – Thamel* 20, VAT 39. The only difference is the entry location
  (Lakeside / HeadOffice); the "accounts where the two differ" query returns no rows. Line sums: service charge 20, fee
  20, Amount 260, VAT 39 on both.
- **SQL, the balances:** *Service Charge – Thamel* −40.00 (the two Momo eaten in), *Packaging – Thamel* 0.00,
  *Service Charge – Lakeside*, *Packaging – Lakeside* and both defaults 0.00, Receivable 0.00, Cash 498.00 (588 × 2 −
  339 × 2), Rounding −0.80 (the two bills' 0.40), Sales −400.00, VAT −57.20.
- **SQL, stock:** Lakeside Store 1 at 40, Main Store 49; the away note's movement is Lakeside Store / Lakeside, the home
  note's Main Store / HeadOffice.
- **403-not-404** (two custom roles, each every one of Admin's 255 keys but one; the run moved its own membership in SQL
  and back):
  - `GET /credit-notes/{nonexistent}`: **404** as Admin; **403 "…(Sales.CreditNote.View)"** without that key, beside a
    200 on `GET /billing-locations`; the real away note is 403 too.
  - `POST /pos/refunds` with a valid body naming a nonexistent sale at Lakeside: **404 "Sale not found."** as Admin;
    **403 "…(LKS.Sales.CreditNote.Approve)"** without Approve. The same role refunding HeadOffice's real bill `0001` at
    Lakeside: the same 403, and its exact search there finds 0 rows. Back on Admin, the note reads 200.

**Keyboard pass** (`erp-web-ssl`, the cookie through `web/public/p85.txt`, deleted at once; Lakeside's till at 1440×900):

- Load: focus in `#pos-search`. `Coke`, Enter: added. F9: the payment panel, focus in the amount, 68.00.
- Ctrl+A, `50`, Enter: Cash 50. Shift+Tab to the mode radios, Right to Card, Tab: amount 18.00; Enter: Card 18.
- Tab ×15 to *Remove Card tender* (checked before pressing), Enter: the Card row gone, **focus on `#pos-tender-amount`**
  (the ring visible in the screenshot), value 18.00, the live region "Card 18.00 removed.".
- Shift+Tab, Left to Cash, Tab, Enter: Cash 18; Enter again completes the sale. `POST /pos/sales` 200; in SQL invoice
  `0003` at Lakeside holds tenders **Cash 50.0000 and Cash 18.0000**, change 0: what was sent after the removal.
- The ERP credit note `0001`: *Refunded at the till* reads "Session SES0002 · Parcel came back cold" and "Goods returned
  into Lakeside Store"; its GL panel shows *Packaging – Thamel* debited 20.00.
- Lakeside's refund page, `0002`: "0002 sold at HeadOffice … 588.00 Refund bill 0002" (phase 84: "Refund it at
  HeadOffice").

No PDF changed this phase.

## 5. Left open, and for later phases

- **Which account when a branch changed its fee accounts since the sale** (Decision B): a refund, at home or away, reads
  the selling location's current account. Reading the sale's own GL line instead would be a change to every refund.
- **The ERP credit-note list** has no warehouse column; only the detail page names it.
- **Carried unchanged:** phase 84 § 5's money receipt on the roll and the annex exports in BS (phase 79 § 5, phase 71
  § 5), phase 83 § 5, phase 81 § 5, phase 80 § 5, phase 79 § 5's other items, phase 77 § 5, phase 76 § 5, phase 75 § 5,
  phase 72 § 5, phase 71 § 5's other items, phase 70 § 5, phase 69 § 5, phase 67 § 5, phase 66 § 5 and phase 62 § 5.

## Writes made to the vendor tenant

None. The vendor was not opened this phase.
