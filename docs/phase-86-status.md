# Phase 86 — a sale keeps the accounts its fees went into, and the credit-note list names where returned goods went

## TL;DR

**A refund now reverses its service charge and packaging fee into the accounts the sale actually credited.** Phase 85
resolved them at refund time from the selling location's *current* setting (§ 2 Decision B there), so a branch that
changed its Service Charge account between a sale and its refund debited the new account. The old one then stayed in
credit forever. Now a sale records the two accounts when it is approved (`Invoice.ServiceChargeAccountId`,
`.PackagingFeeAccountId`), and a refund, at home or at another branch, reverses into them. A sale approved before this
phase recorded nothing, and refunds exactly as before. The credit-note list also names, on each card, the warehouse
that note's goods went back into, read through the same new reader as the note's page.

No live read: the user chose to decide on accounting (2026-10-12). The rule is this codebase's own: a reversal follows
the original's posted lines (phase 16a), and an account is resolved where the original posted (phase 85). The user's
decisions, each the recommended option:

- **A — the two fee accounts only.** These are the only location-routed accounts, and the ones phase 85 made
  cross-branch. Every other account on a credit note (each line's Sales account, VAT, Receivable, Inventory, COGS) is
  still resolved from today's settings, for ERP notes and till refunds alike: the credit note's standing rule since
  phase 5. "Every account on a credit note" was offered and not chosen; it is § 5.
- **B — no backfill.** An empty column means "resolve it as before" (the selling location's current setting, then the
  tenant default), so every existing sale refunds as it would have yesterday. A backfill from the GL would have guessed
  wherever two legs share an account.
- **C — no vendor read.** Finding out what the vendor does would have needed four writes on its tenant, including a
  settings change, and its answer would not have changed ours.

| | |
|---|---|
| Live read taken | **no** (Decision C) |
| Migration | `Phase86InvoiceFeeAccounts`: `sales.Invoices.ServiceChargeAccountId` and `.PackagingFeeAccountId`, uniqueidentifier NULL, FK to `accounting.Accounts` (Restrict), each with its FK index. No backfill |
| New permission keys | **none** |
| Changed endpoints | `GET /credit-notes` returns rows (`CreditNoteListItemDto`: the header fields plus `returnWarehouseName`) instead of the aggregate; `GET /credit-notes/{id}`'s `posRefund.warehouseName` reads through the same reader, so it is null once the refund is voided |
| Changed screens | the credit-note list's cards ("Goods returned into X") |
| Changed print output | none |
| Initial bundle | **652.58 kB**, unchanged, against 680 kB |

**Three things worth carrying forward.**

1. **"Read it off the sale's GL lines" is not a design, because a GL line has no role.** `GlLine` is an account and two
   amounts. A tenant may point service charge, packaging fee and Sales at one account, and then nothing in the entry
   says which credit was the service charge. The answer has to be written down when it is decided, as a till tender
   already keeps its `AccountId` and an invoice line its `CogsUnitCost`. Recording is phase 68's rule (a value a setting
   decides is decided once, at the action) applied to an account instead of a rate.
2. **A shared reader can answer more honestly than either copy did.** Phase 85's detail-page lookup named a warehouse for
   a voided refund, whose goods had been taken back out. Writing the list's version first asked *when did stock move*:
   approved, against an invoice, with a Goods line. That is exactly when `CreditNoteApprovalPosting` restocks. The
   detail page now reads through the same reader and retires its own copy (phase 33: retire copy 1 before writing copy 2).
3. **A list that needs a figure from other rows stops returning its aggregate.** Every document list returned its
   aggregate, which carries new fields for free. The credit-note list now returns `CreditNoteListItemDto`, so a new header
   field must be added there to reach it: phase 32's detail-DTO gotcha, now true of one list too.

Tests: Domain **1032** (+3), Application.UnitTests **1782** (+2), Infrastructure.UnitTests 14, Api.IntegrationTests 71,
Angular **855** (+1, 103 files). `dotnet build`, all four .NET suites (Docker up, one at a time), `ng build` and `ng test`
are green. One Angular run, made while the integration suite was running, failed to load one file (843 tests). Re-run
alone, it is 855 of 855: the load flake phase 36 recorded, and why the suites are run one at a time.

---

## 1. What shipped

**Domain**

- `Invoice.ServiceChargeAccountId`, `.PackagingFeeAccountId`, and `RecordFeeAccounts(sc, pf)`: draft only (the approval
  is the action), and a figure the sale does not carry records no account.

**Application**

- `InvoiceApprovalPosting` records the two accounts the entry credits, just before `Approve`. Every till sale (counter,
  restaurant bill, part bill) and every ERP approval takes this one path. An ERP invoice carries neither figure, so it
  records nothing.
- `CreditNoteApprovalPosting`: the fee accounts are the sale's recorded ones, else phase 85's resolution at the sale's
  location (`SaleFeeSourceAsync` replaces `SoldAtLocationAsync`). Round-off unchanged: a refund rounds its own figure.
- `CreditNoteReturnWarehouses` (new, shared): per note, the warehouse its goods went back into: the note's own, else its
  invoice's. Named only for an Approved note returning an invoice with a Goods line. Batched over a page.
- `ListCreditNotesQuery` returns `PagedResult<CreditNoteListItemDto>`, projected after the page is fetched.
- `GetCreditNoteQueryHandler` reads `posRefund.warehouseName` through the shared reader; its phase 85 lookup is deleted.

**Infrastructure.** The two FKs (Restrict, as for a tender's account) and the migration.

**Angular.** `CreditNoteListItem`, `SalesService.listCreditNotes` typed to it, and the card's "Goods returned into X".

## 2. Scope decisions

**A, B, C** are the user's, above.

**D. Recorded at approval, not at create.** A till sale is created and approved in one request, but an ERP invoice is
saved as a draft first. The setting that applies is the one in force when the entry is posted, so the recording sits
beside the posting, inside the one shared approval path.

**E. Recorded only for a figure the sale carries.** A sale with no service charge records no service-charge account,
even if one is configured. "Null" then means one of two things, and the reader treats them alike: nothing to reverse, or
a sale from before this phase.

**F. The list's warehouse follows stock, not the column.** `CreditNote.WarehouseId` is set on every till refund since
phase 84, including refunds of services only, which move nothing. Displaying the column would have named a warehouse for
a Momo refund. The reader asks whether stock moved, so a services refund, a draft, a void, a standalone note and a price
adjustment name none. An ERP return of goods names its invoice's warehouse, which is true.

**G. Permissions: none new.** The list's new field rides `Sales.CreditNote.View`. The refund's keys are unchanged.

## 3. Bugs found and fixed

1. **Phase 85's detail page named a warehouse for a voided refund.** Its lookup checked for Goods lines and nothing
   else. Found while writing the list's rule (Decision F), and fixed by sharing it. The phase 85 test now also voids the
   away refund and asserts that neither the card nor the page names a warehouse.
2. **A Python patch against a CRLF file found no anchor.** `GetCreditNoteQuery.cs` is CRLF in a mostly-LF tree. The
   script's assertion stopped it; the Edit tool made the change.

## 4. Evidence

**Unit tests**

- `SaleFeeAccountTests` (Domain, 3): a take-away sale records both accounts; a sale with neither figure records neither;
  after approval `RecordFeeAccounts` throws and the recorded account stands.
- `PosCrossBranchRefundTests` (now 14):
  - Two restaurant bills record the old Thamel accounts. The head office then moves to new accounts, and a later bill
    records those (−40 on the new service-charge account).
  - The two earlier parcels are refunded, one at the branch and one at home. They post identical lines, both into the
    old accounts (−80 + 40, −40 + 40); the new accounts keep only the later bill's −40 and −20.
  - The rest of both bills ties the old accounts out to 0.
  - With the recorded accounts ignored (injected), this test fails; restored by hash, it passes.
- A counter sale at a location with no account of its own records the tenant default. With the record cleared, as on a
  sale from before this phase, the refund follows phase 85's rule: the location's account set since, +20, while the
  default keeps the sale's −20.
- The warehouse test also reads the list: the away refund's store, the home refund's main store, none for the Momo
  refund, and none on card or page once the away refund is voided.
- `credit-note-list-page.spec` (new): the card names the warehouse the server sent, and a null names none.

**E2E** (fresh organization `ee90815c-2493-4588-a16c-0eff1e1a5b62`, *Phase 86 Fee Accounts 090903*, seeded from nothing
as in phase 85: HeadOffice Restaurant into *Service Charge – Thamel* / *Packaging – Thamel*, Lakeside Retail into its
own pair, a default pair; Main Store and Lakeside Store; Momo and Coke; a session at each):

- Bills `0001` and `0002` (588 each: service charge 40, fee 20). SQL: each records *Service Charge – Thamel* and
  *Packaging – Thamel*.
- Two accounts created, *… – Thamel 2083*; `PUT /pos/locations/{HO}/settings` points HeadOffice at them. Bill `0003`
  records the 2083 pair.
- The parcel Momo and Coke of `0001` refunded at Lakeside (`0001`), of `0002` at HeadOffice (`0002`), 339.00 each. SQL,
  by account, away | home: Receivable 0 | 0, Cash −339 | −339, COGS −40 | −40, Inventory 40 | 40, **Packaging – Thamel
  20 | 20**, Sales 260 | 260, **Service Charge – Thamel 20 | 20**, VAT 39 | 39. No line on either 2083 account.
- Balances: *Service Charge – Thamel* −40.00 (the two Momo eaten in), *Packaging – Thamel* 0.00, *Service Charge – Thamel
  2083* −40.00 and *Packaging – Thamel 2083* −20.00 (bill `0003` alone); Lakeside's pair and the defaults 0.00.
- `GET /credit-notes`: `0002 Approved | Main Store`, `0001 Approved | Lakeside Store`. After `POST /credit-notes/{0001}/void`:
  `0001 Void | None`, and the old accounts are *Service Charge – Thamel* −60.00, *Packaging – Thamel* −20.00. The void put
  the fee back where the refund had taken it from.
- **403-not-404** (roles each holding every Admin key but one; the membership moved in SQL and back):
  - `GET /credit-notes`: Admin **200** (2 rows); without `Sales.CreditNote.View` **403** naming it, beside a 200 on
    `GET /billing-locations`.
  - `POST /pos/refunds` with a valid body naming a nonexistent sale: Admin **404** "Sale not found."; without
    `Sales.CreditNote.Create` **403** naming it, beside a 200 on `GET /credit-notes`. Back on Admin, 200.

**Keyboard pass** (`erp-web-ssl`, the cookie through `web/public/p86.txt`, deleted at once; 1440×900): the credit-note
list showed "0002 HeadOffice … Goods returned into Main Store Approved" and "0001 Lakeside … Void" with no warehouse. Tab
×33 from the page start landed on *Approved* (checked before pressing); Enter sent
`GET …/credit-notes?…&status=Approved` (read back from the network log). One card remained, still naming Main Store, and
focus stayed on *Approved*.

No PDF changed this phase.

## 5. Left open, and for later phases

- **Every other account on a credit note** (Decision A): each line's Sales account, VAT, Receivable, Inventory and COGS
  are still resolved from today's settings, on ERP notes and till refunds alike. Freezing them means an account per
  invoice line, a larger change.
- **Sales approved before phase 86** keep today's resolution (Decision B); nothing can say which account they used.
- **The ERP credit-note detail page** names the warehouse only inside the till-refund panel; an ERP return's page does
  not, although its card now does.
- **Carried unchanged:** phase 84 § 5's money receipt on the roll and the annex exports in BS (phase 79 § 5, phase 71
  § 5), phase 83 § 5, phase 81 § 5, phase 80 § 5, phase 79 § 5's other items, phase 77 § 5, phase 76 § 5, phase 75 § 5,
  phase 72 § 5, phase 71 § 5's other items, phase 70 § 5, phase 69 § 5, phase 67 § 5, phase 66 § 5 and phase 62 § 5.

## Writes made to the vendor tenant

None. The vendor was not opened this phase.
