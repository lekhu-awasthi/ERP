# Phase 63 — Returns at the till: a refund is a credit note that pays itself out

## TL;DR

**A cashier can now refund a till sale, in part or whole, today's or an earlier one, from the till.**
The refund is an ordinary Credit Note, created approved against the sale. It gives back the service
charge and VAT in proportion, rounds to the rupee, and puts goods back at the cost they left at. Its
payout posts as a **second GL entry on the same credit note**, the mirror of phase 61's tenders, out
of the caller's open drawer. The X/Z report, the day summary and every reader of what a customer
owes count refunds.

| | |
|---|---|
| Live read taken | **no**. Phase 59 wrote the vendor's refund (one Coke, 68, `POST /pos/credit-notes`), and nothing here needed a fact it lacked |
| Rule questions settled first | **yes, both**, from the VAT Rules and the 2072 procedure; sources in Decision A |
| Migration | `Phase63TillReturns`, scaffolded and **not** hand-edited: four additive `CreditNotes` columns and two `CreditNoteLines` columns, all defaulting to what is true of every existing row (`Erp`, 0, null), and two tables |
| New tables | `sales.CreditNotePayouts`, `sales.CreditNotePrints` (append-only, unique on organization × credit note × print number) |
| New endpoints | 6, under `/pos`: find sales, a sale's refundable lines, the refund preview, the refund, a session's refunds, and the credit note's print |
| New permission keys | **none**. A refund rides `Sales.CreditNote.Create` and re-checks `Sales.CreditNote.Approve` at the location |
| New screens | 1 lazy route, `/organizations/:id/pos/refund/:locationId`; the session page gains refunds, and the ERP credit-note page a till-refund panel |
| Initial bundle | **647.29 kB** (+0.17 kB, the route entry) against 680 kB; the refund screen is its own 18 kB lazy chunk |

**Four things worth carrying forward.**

1. **A refund's figure depends on other documents, so the screen keeps no copy of it.** A sale's
   total is a function of its own cart, which is why phase 62 mirrored it in TypeScript. A refund's
   is not: it depends on the sale's earlier refunds (the last one gives back exactly what is left)
   and on what the customer still owes (which decides the payout). So one **planner** builds the
   refund, and both the preview the cashier reads and the refund that posts call it. That is phase
   38's dry-run rule, applied to money.
2. **What is handed back is not a choice.** The refund first clears what the customer still owes on
   the sale, and the rest is paid out exactly. That one rule gives every case its right answer: the
   walk-in (never given credit) gets back the whole refund and never more than they paid, a credit
   sale's refund goes to the account, and a part-paid sale pays back only what was paid.
3. **A payout is the credit note paying itself out, in every reader of what is owed.** Phase 61 taught
   the readers that a sale's tenders settle it. This phase taught them the mirror. Without it a cash
   refund of a paid sale would show the walk-in in credit forever. Proven to bite by removing the
   payout from `OutstandingDocumentReader`.
4. **One door per document.** The ERP's *Convert to Credit Note* on a till sale is now a 409 naming
   the till. Two doors would have meant two ways to price one return, and phase 61's carried item (an
   ERP credit note that returns no service charge) was exactly that.

Tests: Domain **842** (+12), Application.UnitTests **1464** (+18), Infrastructure.UnitTests 13,
Api.IntegrationTests **30**, Angular **697** (+10). `dotnet build`, all four .NET suites, `ng build`
and `ng test` are green.

---

## 1. What shipped

**Domain**

- `PosLineArithmetic`: a till line's amount, service charge and VAT, computed in one place.
  `InvoiceLine.CreatePos` and the new `CreditNoteLine.CreatePos` both call it.
- `CreditNoteLine` gains `ServiceChargeRate` and `ServiceChargeAmount` (persisted), plus the derived
  `TaxableAmount` and `LineTotal`.
- `CreditNote` gains:
  - `Channel`, `PosSessionId`, `RoundOff` (signed) and `Reason`;
  - the child `Payouts` (`CreditNotePayout`: mode, frozen kind and account, amount);
  - `CreatePosRefund`, `AddPosLine`, `SetRoundOff` and `PayOut`;
  - the derived `GrandTotal`, `ServiceChargeTotal`, `PaidOutAmount` and `ToAccountAmount`.
  `AddLine` refuses a till refund, and the till methods refuse an ERP note.
- `CreditNotePrint`: the sibling of `InvoicePrint` (Decision B).

**Application**

- `Sales/Posting/CreditNoteApprovalPosting`, the approve core extracted from
  `ApproveCreditNoteCommandHandler`. The ERP's Approve and the till's refund both call it.
  `CreditNotePostingRule` gains the service-charge and round-off legs (init properties on its
  input, phase 60's rule).
- `Sales/Posting/CreditNotePayoutPostingRule`, the second entry.
- `Pos/Refunds/PosRefundPlanner` (Decision F) and `Pos/PosTenderModes` (the sale's tender resolution,
  lifted rather than copied, now shared by the payout).
- `Pos/Commands/CreatePosRefund` and `PrintPosRefundReceipt`.
- `Pos/Queries`: `FindPosSales`, `GetPosRefundableSale`, `PreviewPosRefund`, `ListPosSessionRefunds`.
  `GetPosTill` gains `PrintCreditNote` and `CanRefund`.
- `PosSalesReader` gains the refunds half (`PosRefundsSummaryDto`) and `NetSales`; `ExpectedCash`
  subtracts cash refunds.
- Readers swept (Decision H).
- `VoidCreditNoteCommandHandler` refuses a till refund whose session is closed.
- `CreateCreditNote` and the conversion template refuse a till sale (Decision G).

**Api.** Six endpoints under `/api/organizations/{id}/pos` (shapes in `e2e-recipes.md`).

**Angular**

- `features/pos/pos-refund-page`: find a sale, choose quantities, reason, the server's preview, the
  payout mode, complete, and the credit note printed.
- `pos-refund-receipt`: the 80 mm credit note, laid out to Rule 20. The receipt stylesheet is shared
  (`POS_RECEIPT_STYLES`).
- The till header and the session page link to Refund for a cashier who may refund. The session page
  shows refunds in the X/Z report, *Cash refunds* in the drawer, *Net sales*, and a refunds list
  with reprint.
- The ERP invoice page hides *Convert to Credit Note* on a till sale and says why. The credit-note
  page shows a till refund's service charge, round-off and payouts, and its server total.

## 2. Scope decisions

**A. The rules: what a credit note must carry, and whether its reprints are counted.**

*Content, settled.* **VAT Rules 2053, Rule 20(1)** requires a credit or debit note to carry:

- its serial number and date of issue;
- the supplier's name, address and registration number;
- the recipient's name and address, and registration number if they are a registered person;
- the number and date of the tax invoice the transaction relates to;
- details of the goods or services and of the credit;
- the amount of the credit, and the amount of tax on it.

Rule 20(2) requires a monthly account of the notes (the Sales Return Register already is one). The
receipt carries each particular; the refund stores the sale's number as its `Reference` (what the
vendor stores as `reference_no`), and a **required reason** (the vendor's *Remarks\**). Unlike an
abbreviated tax invoice, the note always prints the buyer block, because the rule asks for the
recipient. For a walk-in sale that recipient is the walk-in contact, which is what the sale named.

*Copies, settled by reading.* The **2072 procedure, §6** speaks of *invoices*: printed once, every
reprint marked "copy of original" with the number of prints. Its text does not name credit notes.
A credit note is the invoice's statutory counterpart in the same billing records (the IRD's
credit-note book; CBMS receives both), and approved billing software applies the rule to every
printed bill. This phase applies it to credit notes. The reading is the conservative one: it can only
add a "copy" mark, never leave one off.

Sources:

- [Value Added Tax Rules, 2053, English text](https://legaladvisorsnepal.com/value-added-tax-rules-2053-1996/) (Rule 20);
- [Pioneer Law, the 2072 procedure's text](https://pioneerlaw.com/procedure-related-to-computerized-invoicing-2072-2015/) (Standards 6 and 8);
- [an IRD-approved vendor's statement of the requirements](https://discuss.frappe.io/t/finally-got-approval-for-e-billing-from-ird-nepal-erpnext-v13-28-saas/93072) (credit-note book; reprint marking).

**B. Print rows: a sibling table, not a generalised `InvoicePrint`.** Each row keeps a real foreign key
to the one document it counts, with the unique (organization, document, number) index that makes the
count a count. A polymorphic (type, id) table would trade the foreign key for one table, and phase
18's polymorphic parents each needed their own reason; this one has none. The two entities are 60
lines each and say the same thing.

**C. Where a refund happens: the caller's open session, at the location that sold it.**

- A refund is paid **out of today's drawer**, so it belongs to the session open now, even for
  yesterday's sale. `CreditNote.PosSessionId` is that session, never the sale's. Phase 61 Decision H
  refused a void after the close for exactly this reason, and named the credit note in today's session
  as the way out; this is it.
- The sale must be **of the till's location**. A refund at another branch would number the note in
  one location's pool against an invoice of another's, and move stock at a warehouse the till does not
  use. Cross-branch returns are left open (§ 5).

**D. What is handed back: the refund clears what is still owed first, and the rest is paid out exactly.**

- `requiredPayout = refund total − min(refund total, still owed on the sale)`, with "still owed" read
  from `OutstandingDocumentReader` (phase 36's one reader).
- The payouts must sum to exactly that (400 naming `Payouts` otherwise). Paying out more would hand
  cash back for a bill nobody paid; paying out less would leave the customer in credit for money owed
  back to them.
- Cases: a walk-in's sale is always fully paid, so the whole refund is handed back, never more
  ("never cash to the walk-in beyond what they paid"). A credit sale's refund comes off the account
  with nothing handed back. Phase 61's part-paid sale (249: card 100, credit 149) refunded whole pays
  back 100 and clears 149.
- Payout modes are the till's own, resolved by the sale's code (`PosTenderModes`), and a cash payout
  must come out of this session's drawer. A cash payout **larger than the drawer should hold** is a 409:
  a drawer below zero is a count that can never agree.
- The screen offers one payout mode per refund (the API takes several); see § 5.

**E. Permission: `Sales.CreditNote.Create` in the pipeline, `Sales.CreditNote.Approve` re-checked at the location.**

- The command is location-bearing, so phase 32b scopes Create to the till's branch.
- The note is created **approved**, so the handler re-checks Approve at the location every time
  (the `AttachmentAccess` shape phase 61 used for credit). A refund is money leaving the drawer, the
  review a paid-in-full sale does not need. A default Member holds Create and not Approve, so a Member
  cashier sells but cannot refund until an Admin grants Approve at that branch. The till says so
  before a cashier starts (`canRefund`).
- The reads: finding sales and the refundable sale ride `Sales.Invoice.View` at the sale's location
  (they read invoices, like the receipt); the preview rides `Sales.CreditNote.Create`; a credit note's
  print rides `Sales.CreditNote.View` at its location.
- Named `CreatePosRefundCommand`, not `RefundPosSale`: `AuditBehavior` audits Create-prefixed verbs
  (phase 61's gotcha), so the refund writes an audit row.

**F. Rounding, and why the planner owns it.**

- A refund rounds to the nearest rupee when the location rounds or the sale was rounded, like the
  sale (phase 61 Decision B), so a cash refund is a count of notes. Phase 59's Coke refund: 67.80 → 68.
- Never so far that the sale's refunds together exceed the sale.
- **A refund that returns the last of a sale gives back exactly what is left of its total.** Phase 59's
  bill (633) refunded as two halves of 316.40 rounds to 316 then **317**, not 316 again, and every
  account the sale touched returns to zero. Without it, a bill refunded in parts drifts by up to half a
  rupee per part.
- These depend on other credit notes, so `PosRefundPlanner` decides them, and the aggregate only holds
  the shape (whole paisa, never below zero). The preview and the refund share the planner, which is
  why the till keeps no TypeScript copy (TL;DR point 1).

**G. The ERP cannot convert a till sale.** `CreateCreditNoteCommand` with a till sale as referrer, and
its conversion template, are 409s naming the till. The invoice page hides *Convert to Credit Note* on
a till sale and says to refund it at the till. This settles phase 61 § 5's open item (an ERP credit
note against a till sale returned no service charge) by giving such a return one door that does.

**H. The sweep: what learned about refunds, and what deliberately did not.**

Changed to count a refund's service charge and round-off, and its payouts:

- what is owed: `OutstandingDocumentReader` (reduction = note total − payouts),
  `ContactLedgerReader` (the payout as a debit event under the same number),
  `GetDefaultPaymentAllocations`;
- totals: `DailyTransactionSummaryContentBuilder`, `RecentTransactions`, `TransactionList`,
  `EmailMergeValueReader`, `GetCreditNote`, the PDF print pipeline;
- taxable bases, service charge only (round-off is not a supply): VAT Summary, Annex 5, Annex 13,
  `SalesReturnReader` (Sales Register returns and the Sales Return Register);
- Sales Master, in its existing Service Charge column.

Unchanged, on purpose, as in phase 61: the product-level readers (`TradeLineReader`, Inventory Master,
Product Profitability, Ratio Analysis). The full-tenant export stays phase 61's open item.

**I. Smaller decisions.**

- **Stock**: a refund puts goods back at the sale line's own `CogsUnitCost`, the path every ERP credit
  note against an invoice already takes (phase 37: a return relieves at the cost the layers gave up).
  The line's batch now rides along, so a batch-tracked return goes back into its batch; an ERP note's
  line names none, as before.
- **Void**: a till refund is voidable only while its session is open (phase 61 Decision H, mirrored),
  and touches the session's rowversion. Its void reverses both entries through
  `SourceDocumentGlEntries`, which also inherits the bank-reconciliation refusal (phase 57). A voided
  refund frees its quantity again.
- **Lock date**: the command is `ILockDateSensitive`, so a refund dated inside a locked period is
  refused like any credit note.
- **Metering**: a refund is a posted credit note, so it spends the transaction allowance (a named
  second door in the metered guard).
- **The guards** that a second door breaks were taught named entries, not exemptions (phase 61's
  rule): the location-bearing, metered and location-read-path guards.
- **Auto-print** follows the location's *Credit Note* print toggle (phase 60).
- **Keyboard**: the search box is focused on load, Enter searches, each sale's Refund button names the
  bill, focus moves to the sale's heading on selection, and to the credit note's panel on completion.

## 3. Bugs found and fixed

1. *(This phase's own, caught before shipping.)* A Bash heredoc carrying a C# patch with an apostrophe
   in a comment ended the heredoc early; nothing was written. Patches went through the Write tool from
   then on (the phase 57/62 gotcha, again).
2. *(Own.)* A test-run collision: `Infrastructure.UnitTests` and `Api.IntegrationTests` built
   `ErpApp.Application.dll` at the same time, and the integration run failed with `CS2012` before any
   test ran. Re-run alone, it passed. Run the .NET suites one at a time.

## 4. Evidence

**Rule research:** three sources, linked in Decision A.

**Migration** (`dotnet ef database update`, dev DB): applied. The scaffold was reviewed by hand and
contains:

- `CreditNotes`: `Channel` (`nvarchar(10)` default `'Erp'`), `RoundOff` (default 0), `PosSessionId`
  (nullable, FK to `pos.PosSessions`, Restrict, indexed), `Reason` (`nvarchar(500)` nullable);
- `CreditNoteLines`: `ServiceChargeRate`/`ServiceChargeAmount` (default 0);
- `sales.CreditNotePayouts` (FKs to the note, Cascade; to the payment mode and the account, Restrict);
- `sales.CreditNotePrints` with the unique `IX_CreditNotePrints_OrganizationId_CreditNoteId_PrintNumber`.

**E2E** on a fresh organization, *P63 Till Returns 235757*: VAT-registered, POS Retail, HeadOffice
renamed *Thamel Counter*, set to Retail with a default warehouse. Settings: 10% service charge,
round-off, cash verification off, credit notes print. Payment modes: Cash → Cash In Hand and Card →
Nabil Bank. Products: Chicken Momo (service, 200, service charge) and Coke 250ml (goods, 60, opening
stock 100 @ 40). Customer: Acme Retail (PAN 301234567, Lazimpat).

Master data, the two sessions' opening and the sales were seeded through the API (every status code
printed, all as expected). The phase's own screen, the refunds, the reprint and the second close were
driven in the browser pane (`erp-web-ssl`, cookie transplant, `window.print` stubbed to read each
print back). The Nepal date rolled from 2026-10-01 to 10-02 between the two sessions, so the third
refund is genuinely of yesterday's sale.

| Step | Checked | Result |
|---|---|---|
| Seed | SES0001, float 1,000 | sale **0001** (walk-in, 2 momo + 2 coke, cash 1,000, change 367): **633**; sale **0002** (Acme, 1 coke, nothing tendered): **68 on credit** |
| Till | header as Admin | **Refund** link beside *Session & Cash* |
| Refund screen | load | the location's sales, newest first; focus in *Bill number* |
| 1. Partial cash refund, same session | 1 coke of 0001 | preview: Sub 60, VAT 7.80, Round off 0.20, **Total 68, hand back 68**, Cash preselected; credit note **0001**, "68.00 handed back in Cash"; auto-printed once, no copy mark; *Left to refund* Coke 1 |
| 2. Credit sale to the account | 0002 by keyboard (type, Enter), *Refund everything left* | "still owed 68.00"; preview "Off Acme Retail's account 68.00, to hand back 0.00, **Nothing is handed back**", no payout modes; credit note **0002**; its print carries Acme's address and buyer PAN |
| X report SES0001 (API) | the one reader | sales 701, **refunds 136** (68 cash + 68 to account), net 565, **expected 1,565** (= 1,000 + 633 − 68); closed at 1,565 |
| 3. An earlier sale, new session | SES0002 (float 1,000), 0001 by number, everything left | preview: Sub 460, **Service charge 40**, VAT 65, **Total 565** (exactly what is left of 633); credit note **0003**, 565 in cash; nothing left on 0001 |
| 4. Reprint | session page, refunds list, *Reprint 0003* | **COPY OF ORIGINAL · printed 2 times**, top and bottom; dated 02-10 against bill 0001 of 01-10 |
| X report SES0002 | screen | refunds 1, SC returned 40, VAT returned 65, total refunded 565, *Cash refunds* 565, net −565, **expected 435** |
| 403-not-404 | `POST /pos/refunds` naming a nonexistent sale, as Admin | **404** "Sale not found." |
| | same user on a role with `Pos.Session.Operate`, `Sales.Invoice.Create/View`, `Sales.CreditNote.Create/View`, `Tenancy.BillingLocation.View`, `Tenancy.Subscription.View`: `GET /billing-locations` | **200** |
| | same user, same request | **403 naming `HO.Sales.CreditNote.Approve`** (the key at HeadOffice) |
| | the refund screen / the till | "needs permission to create and approve credit notes at Thamel Counter (Sales.CreditNote.Create and Sales.CreditNote.Approve)"; the till shows no Refund link |
| | own role restored | via `sqlcmd` (phase 52's recipe), confirmed *Admin* |
| 5. Close | sale 0003 (452, card) rung for the census; SES0002 counted 435 | Z report: sales 452, refunds 565, net −113, **expected 435 = counted, 0.00** |

**SQL agrees with the screens:**

```
Refunds  Session  Bill  Customer       Sub    SC     VAT    RoundOff  Total   PaidOut  Reason
0001     SES0001  0001  Cash Customer  60.00   .00    7.80  .20        68.00   68.00   Damaged can
0002     SES0001  0002  Acme Retail    60.00   .00    7.80  .20        68.00     .00   Ordered the wrong size
0003     SES0002  0001  Cash Customer  460.00 40.00  65.00  .00       565.00  565.00   Party cancelled, returned next day

entries per refund, each balanced: 0001 108/108 + 68/68; 0002 108/108 (no payout entry); 0003 605/605 + 565/565
0003 note entry: Dr Sales 460, SC Income 40, VAT 65, Inventory 40 | Cr AR 565, COGS 40
0003 payout:     Dr AR 565 | Cr Cash In Hand 565
sessions: SES0001 Closed, expected 1565 = counted 1565; SES0002 Closed, expected 435 = counted 435
prints: 0001 #1; 0002 #1; 0003 #1, #2
balances: Cash In Hand 0 (633 - 68 - 565) | Nabil Bank 452 | AR 0 (Acme 0, walk-in 0) | Rounding 0
          Sales -380, SC Income -20, VAT -52 (sale 0003 alone) | Inventory -120, COGS 120
trial balance: Dr 3932.00 = Cr 3932.00
three-view law (opening stock posts no GL): layers 3880.00 = movements 3880.00 = GL Inventory + 4000 opening = 3880.00 (97 coke)
```

**Keyboard census (phase 40).**

- **A sale selected, a quantity entered: a 10-stop cycle**, in order: the skip link, *Back to Till*,
  *Session & Cash*, *Choose another bill*, the two quantity fields (each named "Quantity of
  {product} to refund, at most {n}"), *Refund everything left*, *Reason*, the payout radio group
  (**one** stop; ArrowRight moved Cash → Card and ArrowLeft back), and *Complete Refund*. Every stop
  has a name.
- **Enter on Complete Refund with no reason** marks the reason invalid, describes it ("Say why the
  goods came back.") and moves focus there.
- **The search view: an 8-stop cycle**: skip link, *Back to Till*, *Session & Cash*, *Bill number*,
  *Search*, and one *Refund bill {number}* per sale (the bill number is visually hidden text; the
  pane's accessibility tree reports only the visible "Refund", so the names were read from the DOM).
- Focus moves to the bill's heading on selection, to the search box on *Choose another bill*, and to
  the credit note's panel on completion.

**Tests that bite.** Both were proven by injecting a regression and restoring the file by hash, then
`touch`:

- not subtracting payouts in `OutstandingDocumentReader` fails
  `A_part_credit_sale_refund_clears_what_is_owed_first_and_pays_back_only_what_was_paid`;
- dropping the planner's "returns the rest" branch fails
  `A_bill_refunded_in_two_parts_gives_back_exactly_what_it_charged`.

Tests that read the money and the rules:

- `PosRefundTests` (Application), 18: both entries balanced, cash out of the drawer, service charge
  returned in proportion, a two-part refund tying out to zero on every account, exact payouts, the
  part-paid and credit cases against Invoice Age and the Contact Statement, the drawer refusal,
  yesterday's sale into today's session, the quantity cap, the Approve 403, the markers, the ERP's
  refused door, void while open and refused after close, the reconciliation refusal, day = session,
  the receipt and its print count, and the ERP detail with the Sales Return Register;
- `PosRefundTests` (Domain), 12: a whole-line refund equals its sale line across discounts, payout
  rules, the round-off's shape, the till/ERP refusals, and print counting;
- Angular `pos-refund-page.spec` (6), `pos-session-page.spec` (+3), `pos-till-page.spec` (+1).

## 5. Left open, and for later phases

- **Cross-branch returns.** A sale is refunded only at its own location (Decision C).
- **A fully refunded bill still appears in the refund search**, with a Refund button that opens it
  showing nothing left. Marking or hiding it is cosmetic and was left.
- **Split payouts on screen.** The API takes several payout modes; the screen offers one.
- **Phase 62's ERP-PDF carry is scheduled, not done here**: the QuestPDF invoice print still titles
  every invoice "Invoice" and neither counts nor marks a reprint, and the ERP credit note PDF follows
  neither. It is one decision (a GET that writes print rows, shared with the email pipeline) and
  belongs with phase 66's print and report surface, or a phase of its own.
- **Refunding a serial-tracked product** waits on phase 62's open item (the till cannot sell one).
- **The print race** on `CreditNotePrints` relies on the unique index, which InMemory does not
  enforce; it was not raced against SQL Server here, as phase 62's was not.
- Phase 61 § 5's export item stands: the full-tenant export has no service-charge or round-off column,
  now for credit notes too.
