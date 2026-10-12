# Phase 88 — the payment preview is what Approve posts, and the allocation pickers list what is still owed

## TL;DR

**Three fixes on the payment screens. The first is a security hole.**

- **The FIFO *Suggest* endpoint had no membership check (phase 5 to phase 87).** `GetDefaultPaymentAllocationsQuery`
  took an `OrganizationId` from the route but was neither `IOrganizationScoped` nor `IRequirePermission`. So
  `AuthorizationBehavior` never ran, and any signed-in user could read another organization's open invoices and bills,
  with their balances, by naming its ids. It now carries the target type's view key and location narrowing. A new
  guard, `OrganizationScopeSweepGuardTests`, fails the build for any request that names an organization without being
  scoped to it. A sweep found no second instance.
- **The payment GL preview is now the entry Approve posts.** `PreviewPaymentGlPostingQuery` took no currency, rate or
  allocations. A foreign receipt previewed its USD amount as NPR and never showed a forex line, and since phase 87
  Approve could post a split of the control leg the preview never showed. Both now call one builder,
  `PaymentGlLines.BuildAsync`. Tests and the E2E hold preview and posted entry equal line for line.
- **The allocation picker lists what is still owed.** It appears on four screens: Customer Payment, Supplier Payment
  and both Allocate screens. Each loaded the **first 200 approved documents of the whole organization** and filtered
  them by contact and currency in the browser. That offered fully settled documents, and on a tenant past 200 it
  silently dropped an old unpaid one. `ListOpenAllocationTargetsQuery` now returns one contact's open documents in one
  currency, oldest due first, read through `OutstandingDocumentReader` (phase 36's shared reader). Suggest fills
  exactly those rows. Each option shows its due date and what is left.

The user's decisions (2026-10-12): the candidate (**phase 87 § 5's preview and picker**), the scope (**A + B + C**: the
security fix, the preview, the pickers), and **Suggest re-based on the shared reader**. That last one changes what
Suggest offers: a journal-voucher-settled invoice is no longer suggested, a USD receipt is no longer offered NPR
invoices, and the order is by due date.

| | |
|---|---|
| Live read taken | **yes**, read-only, abcagro1 (13 trial days left): the payment forms and both Allocate screens, plus the client bundle. Nothing blocked, nothing written |
| Migration | **none** |
| New permission keys | **none**. Suggest and the new list take `Sales.Invoice.View` (receipt) or `Purchasing.PurchaseBill.View` (supplier payment) |
| New endpoint | `GET /payments/open-allocation-targets?contactId&direction&currencyCode` |
| Changed endpoints | `GET /payments/default-allocations` now needs `currencyCode` (400 without it) and is permission-gated. `POST /payments/preview-gl-posting` takes `currencyCode`, `exchangeRate`, `allocations` (required; 400 without). `GET /payments/{id}` allocations carry `targetCode` |
| Changed screens | Customer and Supplier Payment: the picker is the contact's open documents with *due … — N left*, reloaded when the contact or currency changes; the preview sends everything Approve uses. Allocate Customer/Supplier Payment: the picker is loaded per row when *Apply* opens, and choosing a document fills Amount |
| Changed print output | none |
| Initial bundle | **652.58 kB**, unchanged, against 680 kB |

**Three things worth carrying forward.**

1. **The mirror question of a guard is a guard of its own.** Phase 12 made every `IOrganizationScoped` request
   permission-gated. Nobody asked which requests *name* an organization without declaring that marker. The guard keys on
   the property, not a type name or folder, and is pinned non-empty (over 300 requests).
2. **A preview built apart from the action drifts.** The payment preview was correct when written (phase 5). Phase 28
   (currency and forex) and phase 87 (the split) each changed what Approve posts and never reached the preview. One
   builder, and a test holding the two equal, is the fix. Each test previews and approves the same payload.
3. **"Load N, filter in the browser" is two bugs with one cause.** It listed what should not be offered (settled
   documents) and lost what should be (the 201st). Neither shows on a test tenant with a dozen invoices. A per-contact
   server query fixes both.

Tests: Domain 1032, Application.UnitTests **1805** (+10), Infrastructure.UnitTests 14, Api.IntegrationTests 71,
Angular **864** (+3), 106 files. `dotnet build`, all four .NET suites (Docker up, one at a time), `ng build` and
`ng test` (alone) are green.

---

## 1. What shipped

### A. Suggest is scoped, and a guard covers the class

- `GetDefaultPaymentAllocationsQuery` is `IRequirePermission`, `IOrganizationScoped` and `ILocationFilteredQuery`. Its
  key is computed from the direction (`OpenAllocationTargets.ViewKeyFor`). It gains a required `CurrencyCode` and a
  validator (it had none).
- `tests/Application.UnitTests/Common/OrganizationScopeSweepGuardTests.cs`: every `IBaseRequest` in the Application
  assembly with a public `Guid`/`Guid?` `OrganizationId` must be both `IOrganizationScoped` and `IRequirePermission`.
  The allow-list is empty, and a second test pins that every allow-listed name still exists. **Bite proved:** removing
  the two markers from the query again fails it, naming the type, and the file was restored by hash.

### B. One builder for Approve and the preview

- `Payments/Posting/PaymentGlLines.cs`: `PaymentGlDraft` (account, amount, direction, currency, rate, allocations;
  `Of(Payment)` for Approve) and `PaymentGlPlan` (lines plus the unallocated control account). `BuildAsync` resolves the
  default control account, converts the amount, prices the forex from the targets' rates, and either splits
  (`PaymentControlAccounts.SplitLinesAsync`, now taking the draft) or builds the rule's one-account entry.
- `ApprovePaymentCommandHandler` calls it and records `plan.UnallocatedControlAccountId`. The cash-balance check, the
  numbering and the entry are unchanged.
- `PreviewPaymentGlPostingQuery` gains `CurrencyCode`, `ExchangeRate` and `Allocations`, implements
  `ICurrencyBearingCommand` (the 28th; `CurrencyValidationRulesTests` counts it), and has a validator: allocations
  present, each positive, their sum within the amount (Approve's 409, as a 400 naming `Allocations`), plus the shared
  currency rules. Both forms send the payload and render preview lines `track $index`, since a split or forex leg can
  put one account on two lines.

### C. The open-documents list, and Suggest on it

- `OutstandingDocumentReader.LoadInDocumentCurrencyAsync`: the same rows as `LoadAsync`, before the fold to base
  (`LoadAsync` now folds its result). An allocation is in the payment's currency, which is the document's. A contact
  filter is now applied **in SQL** on the invoice, bill, expense and voucher-line queries (a second `Where`, phase 33),
  not after loading the whole organization. Ageing reports given a contact get the same rows, faster.
- `Payments/Allocation/OpenAllocationTargets.cs`: that reader, as of `DateOnly.MaxValue` (a payment may settle a
  document dated after it, as it always could), narrowed to invoices (receipt) or bills (supplier payment) with a
  positive balance, the caller's granted locations for the view key (`LocationAccessScope`), and the currency; ordered
  by due date, date, number.
- `ListOpenAllocationTargetsQuery` (`GET /payments/open-allocation-targets`). Suggest is a FIFO fill over the same rows.
- `GetPaymentQuery`'s allocations carry `TargetCode`, so a target that has since been settled (every target of an
  approved payment, usually) still shows its number. The draft forms keep a saved target selectable when it is no
  longer open.
- Angular: `PaymentsService.listOpenAllocationTargets`, `previewPaymentGlPosting(org, request)`, `getDefaultAllocations`
  with the currency. Both payment forms reload the picker on contact change (`onContactChange`), on currency change (the
  currency control bound one-way plus `(currencyCodeChange)`), on load, and on the prefill from `?contactId=`. A
  request counter drops a reply to an older question. The Allocate screen loads the row's targets when *Apply* opens
  it, and choosing one sets Amount to `min(credit left, document left)`. The forms' caption shows only on a draft.
- The Supplier Payment form's *new* reset now resets the currency and rate, as the Customer Payment form always did.
  Before this, a new supplier payment opened after viewing a USD one stayed in USD.

## 2. Scope decisions

**A–C and the Suggest re-base** are the user's, above.

**D. The keys are the target type's view keys, not a payment key.** The picker was built from `GET /invoices` and
`GET /purchase-bills`, which need `Sales.Invoice.View` and `Purchasing.PurchaseBill.View`. Gating the replacement on
the same keys leaves who sees what unchanged: the same people see the same documents, now only the open ones. A payment
key would have widened it, since a clerk with `Payments.Payment.View` but no `Sales.Invoice.View` would see invoice
numbers and balances. Suggest returns the same data, so it takes the same key; a prefill is no easier door than the list
it reads (phase 32b's `GetBomTemplateQuery` reasoning). Admin and Member hold both keys, so nobody's screen changes.

**E. Location: narrowed, not filtered.** Both queries narrow to the caller's granted locations for the key, as the
lists they replace did. They offer no branch filter, because a payment settles a document at any branch (phase 35b).
`LocationReadPathSweepGuardTests` names both in `ListFilterExempt` with that reason.

**F. Due date first, as the reference product does.** Read live: both vendor screens sort by `due_date`. Suggest
used to order by document date. With no credit term the two orders agree.

**G. The vendor's row shape was not copied.** The vendor's payment form is a table of every open document with a
*This Allocation* input per row. Ours stays a list of rows each picking one document, now offering only open ones
with their balances. The difference is layout, not what is offered.

**H. The vendor's 100-document cap was not copied.** Its Allocate detail asks for `limit: 100`, its form pages at 20.
One contact's open documents is a short list, so ours returns them all.

**I. Expenses are not targets.** The shared reader lists expenses and journal vouchers as outstanding on the supplier
side, but a payment cannot be allocated to either (`AllocationTargetRates`: Invoice and PurchaseBill only), so the
list filters to the allocatable type. Unchanged.

**J. Decision G of phase 87 stands.** A debit note's Inventory Adjustment, Rounding, the stock catch-up and journal
voucher allocations stay today's.

## 3. Bugs found and fixed

1. **Suggest was readable across organizations** (since phase 5): § 1 A.
2. **The preview ignored currency** (since phase 28): a USD 100 receipt at 132 previewed as Cash 100 / Receivable 100.
   It is NPR 13,200 with a 200 forex line.
3. **The preview ignored allocations** (since phase 87): one control line where Approve posts two.
4. **The pickers listed settled documents and dropped anything past the organization's 200th** (since phase 17 on the
   Allocate screens, since phase 5 on the forms).
5. **Suggest ignored journal-voucher settlements and currency** (since phase 17 and phase 28).
6. **A new supplier payment kept the last viewed payment's currency** (since phase 28): § 1 C.
7. **Preview rows tracked by account id**, a duplicate key the moment one account appears twice.

## 4. Evidence

**Live read (2026-10-12, abcagro1, read-only; XHR/fetch wrapped to refuse non-GETs, none attempted).** The Customer
Payment form, on choosing *Cash Customer*, asked `GET /transaction-allocations?allocated=false&contact_id=…&
is_allocator=false&sorts=due_date&status=Approved`. The supplier form asked the same for *Ram Hari Sharma* and listed
`PB0001/HO/83-84` with `source_total 162, allocated_total 0, remaining_amount 162`. Its table shows TYPE, DATE (and due
date), AMOUNT, LEFT TO ALLOCATE, THIS ALLOCATION, FIFO. The client bundle's Allocate detail queries are
`{limit: 100, is_allocator: false, allocated: false, source_total_$gt: 0, contact_type, sorts: ["due_date"], status:
"Approved"}`. Both Allocate lists were empty on this tenant; no currency parameter appears in either query.

**Unit tests (+10).** `PaymentGlPreviewTests` (4): preview equals approve with the Receivable moved between two invoices
(3 lines); a USD receipt at 132 against an invoice at 130 shows `(cash, 13200, 0)` and `(gain, 0, 200)` and equals
approve; a payment on account previews the old two-line entry; the validator refuses allocations over the amount, a
missing list, and NPR at rate 2. `OpenAllocationTargetsTests` (4): one customer with a fully paid invoice, a part-paid
one (plus a draft receipt naming it), one settled by a journal voucher, another customer's invoice, and a USD invoice.
NPR lists only the part-paid one at 500 of 600, USD lists only the dollar one at 100 (unfolded), Suggest fills only the
part-paid one, an empty contact lists nothing, and both validators name `CurrencyCode`. `OrganizationScopeSweepGuardTests`
(2). The four existing Suggest tests pass on the new arithmetic, changed only in construction. **Bites:** with the
preview's allocations replaced by `[]`, both preview-equals-approve tests fail; with Suggest's markers removed, the
guard fails.

**Angular (+3).** `payment-detail-page.spec.ts` (new): choosing a customer asks
`('org-1', 'acme', 'Received', 'NPR')`, the option reads *INV-0001 … 578.00 left*, a currency change asks again with
USD and empties the picker; the preview sends the currency, rate 1 and only the complete allocation row, and renders
both lines of a split on one account. `allocate-payment-page.spec.ts` (+1): Apply asks for the row's contact and
currency, the option shows *100.00 left*, choosing it sets Amount to 100 (the receipt has 164.40), and Confirm sends 100.

**Fresh-org E2E** (org `2206ec51…`, *Phase 88 E2E 181840*; Python `urllib` with a cookie jar, every status printed).
Seed: 15 accounts, defaults, a Service product at 600 + 13%, *Himal Traders*, *Everest Supplies*. Invoices 0001–0003
(678 each); receipt 0001 settles 0001, receipt 0002 pays 78 of 0002. **Every default moved** to *Receivable 2083* and
*Payable 2083*, then invoice 0004. Bills 0001, 0002 (452 each); supplier payment settles bill 0001.

- `open-allocation-targets` (customer, NPR): `[0002 600, 0003 678, 0004 678]` by due date. USD: `[]`. Supplier:
  `[0002 452]`.
- Suggest 1000: `0002 600, 0003 400`. Without `currencyCode`: **400** `{"CurrencyCode": ["'Currency Code' must not be
  empty."]}`.
- Preview 1000 with `0002 600, 0004 300`: `Cash 1000 Dr, Receivable 2083 400 Cr, Receivable 600 Cr`. Without
  `allocations`: **400** naming `Allocations`. The same payload saved and approved (receipt 0004) posted the identical
  three lines, read from `GET /payments/{id}` and again in SQL. Its allocations came back as `targetCode` `0002`, `0004`.
- **403-not-404.** A role *Clerk 88* copied from Member minus `Sales.Invoice.View`. As Admin first, Suggest for a
  contact that does not exist: **200 `[]`**. After moving the user onto the clerk role, the same request: **403
  "You do not have permission to perform this action (Sales.Invoice.View)."** Open targets (Received): **403** naming
  the same key. In the same run, same user: open targets (Paid) **200** `['0002']`, Suggest (Paid) **200**. Admin restored with
  `UPDATE tenancy.OrganizationMemberships` (the clerk role cannot edit roles), and read back.
- **SQL:** `payments.Payments.UnallocatedControlAccountId` is *Receivable* for receipts 0001/0002 and *Receivable 2083*
  for 0004; `sales.Invoices.ReceivableAccountId` is *Receivable* for 0001–0003 and *Receivable 2083* for 0004.

**Browser and keyboard pass** (cookie via `web/public/e2e88.txt`, deleted at once; `erp-web-ssl` + `erp-api`). On a new
Customer Payment, arrow keys on *Customer* asked the picker once per keypress, the last for *Himal Traders*. *Cash In
Hand* was chosen by arrow keys, 1000 typed, *Add Row* focused and pressed with Enter. The row's picker offered exactly
*0003 — due 03-10-2026 — 678.00 left* and *0004 — due 04-10-2026 — 378.00 left*; 0001 and 0002, both settled, were
gone. Rows 0003 678 and 0004 300, then *Preview GL Posting* (Enter): Cash 1,000.00 / Receivable 2083 322.00 /
Receivable 678.00. Saved (Enter), the row set to 322, previewed again (same three lines), saved and **approved by
Enter** (receipt 0005). SQL: allocations `0005 → 0003 678, 0004 322`; GL `Cash 1000 Dr, Receivable 2083 322 Cr,
Receivable 678 Cr`, the preview exactly. The approved page lists *0004 322.00, 0003 678.00* by number though both are
settled now. On Allocate Customer Payment, *Apply 0004* (the receipt with 100 left) focused and pressed, the picker had
focus and offered only *0004 — 56.00 left*. Arrow down filled Amount with **56**; Tab to *Confirm*, Enter: *Applied 56.00
from 0004.*, focus on the heading. SQL: `0004 → 0004 56`; no move entry (the target and the unallocated part are both
on *Receivable 2083*); *Receivable* 0.00, *Receivable 2083* −44.00, the receipt's credit left on account.

## 5. Left open, and for later phases

- **The vendor's per-row *This Allocation* table** (Decision G): one input per open document, FIFO filling them in place.
  A layout change, not asked for.
- **A foreign-currency allocation on the vendor** is unread: no currency parameter appears in its queries, and the
  tenant has no foreign document.
- **`OutstandingDocumentReader`'s settlement queries stay organization-wide** (phase 42's choice). With a contact the
  candidates are narrowed in SQL now, but the allocations and credit notes it nets are not. Measure on `tools/scale`
  before narrowing them.
- **Carried unchanged:** phase 87 § 5's documents from before phase 87, Decision G and expenses; phase 86 § 5's ERP
  return page warehouse; phase 84 § 5's money receipt on the roll and the annex exports in BS (phase 79 § 5, phase 71
  § 5); phase 83 § 5, phase 81 § 5, phase 80 § 5, phase 79 § 5's other items, phase 77 § 5, phase 76 § 5, phase 75 § 5,
  phase 72 § 5, phase 71 § 5's other items, phase 70 § 5, phase 69 § 5, phase 67 § 5, phase 66 § 5 and phase 62 § 5.

## Writes made to the vendor tenant

None. The pass was read-only, with non-GET requests refused in the page; none was attempted.
