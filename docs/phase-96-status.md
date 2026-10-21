# Phase 96 — the carried questions answered: a refund rounds where its sale rounded, two vendor extras dropped

## TL;DR

**In plain words, what changed for someone using the app.** Phase 95 left three questions only the user could answer.
On 2026-10-21 the user answered them, and this session built the one that needed building.

1. **A counter refund now rounds into the same account its sale rounded into.** When a till sale is rounded (203.40
   billed as 203), the 0.40 goes to the location's Round Off account. Before this phase a refund of that sale rounded
   into whatever account the *refunding* branch had set *today*. Now it uses the account the sale used, even when the
   refund happens at another branch or the setting has changed since. Refund a whole sale and the rounding account goes
   back to exactly where it was. Sales from before this phase, and refunds of sales that were not rounded, work as they
   did.
2. **Tigg's extra print options are dropped for good**: moving print blocks around the page, renaming non-statutory
   captions, the custom HTML designer for Delivery Note/GRN/Transfer prints, and amount-in-words in lakhs for
   Bangladeshi, Pakistani, Sri Lankan and Bhutanese currencies. Nobody asked for them.
3. **Tigg's payment-allocation table is dropped for good.** Ours keeps one row per invoice picked; it offers the same
   invoices and balances.
4. **The three VAT points still wait for a tax advisor.** The user asked what to ask; the questions are in § 5, written
   so they can be handed over as they are.

Nothing else on screen changed. There is no new screen, no new permission and no change to any printed bill.

| | |
|---|---|
| Live read taken | **no** (nothing here depends on the vendor's screens) |
| Migration | `Phase96InvoiceRoundingAccount`: `sales.Invoices.RoundingAccountId` uniqueidentifier null, **no FK** (phase 87 Decision F), **no backfill** (nothing records which account an old sale rounded into). Applied to the dev DB |
| New permission keys | **none**. A refund keeps `Sales.CreditNote.Create` + `.Approve` at the location |
| Changed endpoints | **none** in shape. `POST /pos/refunds` (and an ERP credit note naming a rounded till sale) posts its round-off to the sale's recorded account |
| Changed screens | **none** |
| Changed print output | **none** (the account is not printed). No PDF to look at |
| Initial bundle | **656.01 kB**, unchanged, against 680 kB |

Tests: Domain **1041** (+1), Application.UnitTests **1897** (+3), Infrastructure.UnitTests 14,
Api.IntegrationTests 79, Angular 928. All green with Docker up.

## 1. What shipped

`Invoice.RoundingAccountId`, written by `RecordPostingAccounts` only when the sale's `RoundOff` is non-zero (phase 87's
rule: a figure the sale does not carry records no account). `InvoicePostingAccounts` gains the member (positional, so its
one caller and the Domain test failed to compile until they passed it, which is the sweep). `RecordedSaleAccounts` reads
it, and `CreditNoteApprovalPosting` uses `recorded?.RoundingAccountId`, falling back to today's rule (the note's
location's Round Off account, then the tenant default) when the sale recorded none.

The note still **rounds its own figure** (phase 63): the amount is the refund's round-off, as before. Only the account
moved. That is what makes a whole sale refunded in parts tie out: the parts' round-offs sum to the sale's, and now they
land on the same account.

**Phase 87 Decision G is now narrower.** It listed four things that stay "today's": a debit note's Inventory
Adjustment (moved to the bill's in phase 95), a credit or debit note's Rounding (the credit note's moved here; a debit
note posts no rounding), the stock catch-up and a journal voucher's allocation (both unchanged: neither reverses a
recorded figure).

## 2. Scope decisions (the user's answers, 2026-10-21)

- **A — a sale return's rounding follows its invoice** (recommended option). A note naming no invoice, or one whose
  invoice was approved before this phase or was not rounded, keeps today's rule.
- **B — none of Tigg's print extras** (phase 79 § 5: block positions, label renames, the DN/GRN/transfer HTML
  templates, lakhs for BDT/PKR/LKR/BTN). Dropped, not carried.
- **C — keep our allocation rows** (phase 88 § 5's per-row table). Dropped, not carried.
- **D — the VAT points wait**; the user asked what to put to the advisor (§ 5).

**Why the fallback is the refunding location, not the selling one.** Phase 85 sent an unrecorded *fee* back to the
selling location's current account, because a fee reverses a figure the sale posted. An unrecorded rounding has no
such figure to reverse; it is the refund's own, so it keeps the rule every refund had before this phase. The option the
user chose said so ("older invoices ... keep today's rule").

## 3. Bugs hit and fixed

None. The new tests passed first time, so the main one was proven to bite: with `CreditNoteApprovalPosting` put back to
the old rule, `A_refund_after_the_location_changed_its_round_off_account_rounds_into_the_account_the_sale_used` fails;
the file was restored by hash and touched.

## 4. Verification

- **Tests.** Domain: a rounded sale records its rounding account and a whole one none. Application
  (`PosCrossBranchRefundTests`): two identical rounded sales at the head office; the head office moves its Round Off
  account; one sale is refunded at the branch and its twin at home, both rounding into the old account, line for line
  equal; the last glass of each ties the old account to zero while the new one keeps only the later sale's 0.40 and the
  branch's own account stays untouched. A sale with its recorded account cleared (as before phase 96) rounds at the
  refunding branch's account. A sale of 339.00 records none.
- **E2E on a fresh organization** (*Phase 96 Rounding 061658*, Retail till at Head Office and Lakeside, a Service
  product at 60 + 13%). Three sales of 3 glasses (203.40 → 203): two with Head Office rounding into *Rounding - Thamel*,
  one after it moved to *Rounding - Thamel 2083*. In SQL `sales.Invoices.RoundingAccountId` named those accounts. Refunds
  0001 (at Lakeside, 2 glasses, +0.40), 0002 (home, +0.40), 0003 (home, the last glass, −0.80) and 0004 (at Lakeside by
  keyboard, the last glass, −0.80) all posted their round-off to *Rounding - Thamel*. Final balances: *Rounding - Thamel*
  0.00, *Rounding - Thamel 2083* 0.40, *Rounding - Lakeside* and the tenant default no line at all.
- **403-not-404.** `POST /pos/refunds` naming a nonexistent sale: **404** "Sale not found." as Admin; **403** "You do not
  have permission to perform this action (HO.Sales.CreditNote.Approve)." from a role copied from Admin without that key
  (membership switched in SQL), beside a **200** on `GET /credit-notes` from the same user; **404** again once restored.
- **Keyboard, in a real browser** (Lakeside's refund screen, keys only): Tab to *Bill number*, type 0002, Enter; Tab to
  the row's *Refund* (its hidden text says "bill 0002"), Enter; focus moved to the *Bill 0002* heading; Tab to the
  quantity, 1, Tab past *Refund everything left* to the reason; the server's figures appeared (60.00, VAT 7.80, Round
  off −0.80, hand back 67.00); Tab through the Cash radio, the amount and *Add part* (focus ring visible) to *Complete
  Refund*, Enter. Refund 0004 completed, focus moved to the *Credit note 0004* panel, and SQL showed its rounding on the
  sale's account.
- **PDF.** None changed, so none was rendered.

## 5. Left open, and why

Only the tax advisor's three points (phases 89 and 90 § 5). Here they are as questions, each with what the app does
today, so the user can hand them over as written:

1. **VAT credit at the end of the fiscal year.** When a month's purchase VAT is more than its sales VAT, the extra is a
   credit carried to the next month. Our VAT return carries it on **across the year's end** (Asar into Shrawan), with no
   limit. *Question:* does an unused VAT credit at the end of Asar carry into the new fiscal year as it is, or must it be
   claimed as a refund, or is any of it lost or reset? Is there a time limit on carrying it (for example, a number of
   months after which it must be claimed or lapses)?
2. **A zero-rated sale inside Nepal.** Some goods are sold at 0% VAT to a customer in Nepal (not an export). Our VAT
   return lists such a sale under **local exempt sales**, beside genuinely exempt goods; exports have their own line.
   *Question:* on the VAT return (the IRD form), does a domestic zero-rated sale belong with exempt sales, or in a line
   of its own, or with exports? Does it change how much purchase VAT can be claimed against it (zero-rated usually keeps
   the input credit; exempt usually loses it)?
3. **Annex 13's amounts.** Annex 13 lists each customer and supplier whose dealings pass the threshold (with their PAN).
   Our Annex 13 shows the amounts **before VAT**, as Tigg does, reasoning that it is filed with the income-tax return,
   where a VAT-registered business's sales and purchases exclude VAT. *Question:* should Annex 13's figures be before VAT
   or including VAT? And is the reporting threshold judged on the amount before VAT or including it?

Whatever the advisor says, each is a contained change: (1) the carry in the VAT Summary handler (phase 89 Decision G);
(2) one line's classification in the return; (3) one flag in the Annex 13 query (phase 90 Decision C).

**Not fixable without new information** (unchanged from phase 95): a partial refund of a sale from before phase 94/95
that walked its batches returns into the oldest-taken batch, because no record says which units came back.

## Writes made to the vendor tenant

None. The vendor was not opened this phase.
