# Phase 29 — Landed cost (FR-6.15, Cost Terms' other half)

## TL;DR

**The roadmap's "decisive experiment" never had to be run.** It called for approving a Purchase Bill
carrying a Freight row on the reference tenant and reading its GL Transactions, to settle whether
the additional cost credits the supplier or a separate payee. Two **already-approved** bills on that
tenant carry Additional Cost rows, so the whole question was answered read-only — and the answer was
neither option: **it posts nothing to the general ledger at all**, is not in the bill's Grand Total,
and the supplier is credited the goods total only. There is no payee field on a row to name anyone
else with. Meanwhile the cost **is** fully capitalised into stock: `SSSS (P0597)` shows In
100 @ **209** = 20,900 for a bill of 100 @ 200 plus 900 of additional cost, and on the two-line bill
`Classis 350 cc` reconciles to the rupee inside a 39,000,900 total. That tenant is *periodic* in the
GL (its Goods lines debit "Purchase Goods", a Direct Expense — the same fact phase 25 found), so its
landed cost lives only in a stock-costing subsystem.

**We post anyway, and that is this phase's Decision B — phase-25 Decision A's argument, unchanged.**
This codebase is *perpetual*: since the post-Phase-19 fix a Goods line debits
`DefaultInventoryAccountId`, so that account is meant to track the FIFO ledger. Posting nothing would
leave it understating stock by exactly the capitalised cost, permanently and silently. The credit
cannot be the supplier (live: it isn't) and cannot be a payee (live: there is none), so it is a new
**Landed Cost Clearing** liability the carrier's own bill later nets to zero.

**The conservation law is the phase, and it is proven in SQL, not asserted in prose:**

```
goods line amounts  +  allocated additional cost  =  FIFO layer value created  +  residue
```

Against real SQL Server, on a bill of Motorbike 10 @ 600,000 and Helmet 5 @ 1,200 (plus a Service
line that correctly received nothing), carrying Freight 660 by **Value** and Custom Duty 300 by
**Quantity**:

```
GoodsAmount   Allocated   LayerValue          Capitalised   Residue   LeftSide      RightSide           Law
6006000.0000  960.0000    6006960.00050000    960.0005      -.0005    6006960.0000  6006960.00000000    HOLDS
```

Layers were created at their **landed** unit costs — Motorbike 600,085.9341 against a bill rate of
600,000, Helmet 1,220.1319 against 1,200 — and the GL entry came back `BALANCED` with the Inventory
account's net (6,006,960.0005) equal to the FIFO ledger value **exactly**: `AGREES WITH LEDGER`.

**Three things that generalise.** (1) A blocked confirm-live is not the only failure mode — so is an
*unnecessary* one: check whether existing data already answers the question before asking for
permission to write. (2) The reference product offers **service lines** in its Additional Cost
product picker and we refuse to, because a service line creates no FIFO layer and a cost allocated
there would silently vanish; a row naming one is rejected rather than dropped. (3) Phase-24 bug #1
bit again, in a new place — allocations appended to already-tracked parents come back `Modified`,
not `Added`, and the fix was the documented one: have the Domain method **return what it created**.

---

## Step 1 — the confirm-live pass (Moonbeam UAT tenant, 2026-09-04)

Recorded in full as an appendix to `docs/erp-module-scan.md`. The headlines, and what each settled:

| Open question | Answer | Where it went |
| --- | --- | --- |
| Does the cost credit the supplier or a separate payee? | **Neither — no GL at all**, and the supplier is credited the goods total only | Decision B |
| Can a row name a payee? | **No.** A row is Cost Term × Product × Method × Amount and nothing else | Decision B |
| What does "Add product-wise" change? | It swaps the section from allocation *rules* to a **product × cost-term matrix** of hand-typed cells (plus an Import action) | Decision C |
| Is the allocation visible after approval? | **Yes** — that same matrix, on the bill's Overview under the totals | Decision C |
| Does a Debit Note unwind it? | Not observable there (periodic), so reasoned — see Decision E | Decision E |
| Can it be allocated to a service line? | The live picker **offers one** | Decision A |

Two further live facts that shaped the design: the additional cost is **excluded from Sub Total and
Grand Total**, and it **is** capitalised into stock valuation per line, confirmed by reconciling the
Inventory Movement report against both sample bills to the rupee.

The screen was functional throughout — the check phase 28's blocked pass taught us to make first.

---

## Scope decisions

### A. Goods lines only — a deliberate divergence from the live picker

The reference product offers service lines (verified by putting `AWS Consulting (Service) P0593` on
a draft and finding it in the list). It can afford to: nothing is posted and nothing is capitalised
into a ledger a service line never reaches. Here the entire point is capitalisation into a FIFO
layer, and a service line creates none — so an allocation there would have nowhere to go and would
vanish, breaking the conservation law. Therefore:

- `"All Product"` means all **goods** lines.
- A row naming a **service** product is **rejected** (409 at Create/Update, with the Domain guard as
  a backstop at Approve), never silently dropped. Proved live: the E2E's second bill came back
  `409 "An Additional Cost row names a service product. Additional cost is capitalised into stock,
  so it can only be allocated to goods lines."`
- A bill whose only lines are services cannot carry an additional cost at all.

The E2E bill deliberately carried a Service line alongside two Goods lines: it appears in the
document, is debited to Purchase Expense as always, and receives no allocation.

### B. We post; the reference product does not

Covered in the TL;DR. The entry, on top of the unchanged Phase 6/7 legs:

- **Debit Inventory** with `CapitalisedAdditionalCost`
- **Credit Landed Cost Clearing** with the same figure

Posted **gross as its own Inventory line** rather than folded into the goods debit (phase-25's
precedent), so the Journal report shows landed cost as the distinct thing it is. Net effect traced
per account, per phase-6 bug #3's discipline: Inventory receives goods + capitalised = exactly the
layer value created; Landed Cost Clearing carries that figure and nothing else; **Accounts Payable,
VAT Receivable and TDS Payable are untouched** — the supplier is owed the goods total and not a paisa
more, which is what the live pass found.

`TenantSettings.DefaultLandedCostClearingAccountId` is resolved **lazily** — only when the bill
actually carries an Additional Cost section — the same treatment phase 28 gave the forex accounts,
so a tenant that never uses landed cost never configures an account for it. It is resolved *before*
Approve creates a single layer, so a missing account is a clean 409 rather than a half-applied
approval (covered by a test that asserts no layer exists afterwards).

### C. The allocation is **stored**, not just derived

The live product renders the per-(product, cost term) breakdown on an approved bill, so the figure
has to survive. Two entities, which serve both live modes with one shape:

- `PurchaseBillAdditionalCost { CostTermId, ProductId?, Method, Amount }` — the row as entered.
- `PurchaseBillAdditionalCostAllocation { PurchaseBillAdditionalCostId, PurchaseBillLineId, Amount }`
  — written at Approve.

The **"Add product-wise" toggle is one bool on the bill** (`IsProductWiseAdditionalCost`), not a
second entity: a hand-typed matrix cell is simply a row that already names its product, so the
arithmetic is identical and the flag only decides how the section re-renders when the bill is
reopened. `Method` stays required in both modes — a product-wise cell still needs a rule if that
product sits on two lines of the same bill — and the client defaults it to `Value` there.

### D. The conservation law, and where the residue goes

```
unitCost_i = Round( (line.Amount_i + allocated_i) x ExchangeRate / quantity_i , 4 )
```

Rounded **once**, at the stock ledger's own scale, from the line's total landed value — so the
document and the ledger can never disagree about what a unit cost is (phase-25's rule). Two rounding
steps, each handled deliberately:

1. **Allocating a row across lines.** Shares are rounded to 4 dp and **the last line in scope takes
   the remainder**, so `sum(allocations) == row.Amount` exactly, for every row. Visible in the E2E:
   Freight 660 by value over a 1000:1 split gave 659.3407 and 0.6593.
2. **Rounding the unit cost.** What survives is the phase's residue, and it is *named*:
   `CapitalisedAdditionalCost` is what the layers actually absorbed and
   `AdditionalCostRoundingAdjustment` is the difference from what was entered. Both are returned by
   the Approve command and shown on the detail page. The E2E's was **-0.0005** — negative, because
   the layers absorbed marginally more than was entered, which is a legitimate direction.

**The GL is built from the values actually created**, not from what the user typed:
`capitalised = layerValueCreated - goodsAmountBase`. That is what makes the Inventory account equal
the FIFO ledger by construction rather than by luck.

### E. Void and Debit Note both unwind it — and the Debit Note needed a new leg

- **Void** needed no new code and is correct by construction: `ReverseIncrementAsync` zeroes the
  layers this bill created (which carry the landed cost), and `PostReversalOf` mirrors the whole
  entry including the new pair. A test traces Inventory, Landed Cost Clearing and Accounts Payable
  all back to zero across the original and its reversal.
- **A Debit Note did not.** `ConsumeAsync` relieves layers at their *landed* cost, while
  `DebitNotePostingRule` credited Inventory only the *return price* — so returning goods off a
  landed-cost bill would have left Inventory permanently above the ledger by the freight sitting in
  the returned units. This is phase-6 bug #3's trap in a new place. Fixed by releasing the returned
  units' share back out of Inventory and into the clearing account, matched to the source bill's own
  line on the same `(ProductId, Rate, VatRate, DiscountPct)` quadruple every other purchase-return
  path keys on, and proportional to quantity returned. Zero for every bill without landed cost, so
  nothing pre-existing changes. Two tests cover it, including a full return netting every account —
  clearing included — back to zero.

### F. The additional cost is in the **document's** currency

The live column header reads "Amount (NPR)", but that tenant has a single-currency list, so the
label is its base currency rather than evidence of a second denomination — **undecidable live**, and
recorded as such. Treating it like every other amount on the document keeps one rule, and the fold
happens exactly where phase-28 Decision D says it must: on the posting rule's inputs and on the unit
cost via `ToBaseUnitCost` (4 dp), never on finished GL lines.

### G. The FIFO layer's basis changed from `Rate` to `Amount / Quantity` — a fix, not a refactor

Approve previously built the layer at `ToBaseUnitCost(line.Rate)`, which ignores both the line and
header discount, while the GL debited Inventory the **discounted** `line.Amount`. On any discounted
bill the Inventory account and the FIFO ledger therefore drifted apart by the discount, permanently.
The conservation law does not permit that, so this phase closes it. **With no discount the two are
identical**, because `Amount == Quantity x Rate` exactly — so no undiscounted bill's behaviour
changes, and every pre-existing test stayed green.

### H. Permission keys — none added

An Additional Cost row is part of the Purchase Bill's own form and rides its Save, confirmed live
(it is not an independent post-creation action, which is the thing phase-20a/20b's lesson says to
check). So it rides `Purchasing.PurchaseBill.{Create,Edit,Approve}` and needs no new key, and no
migration touches `RolePermissionConfiguration`.

---

## What shipped

**Domain** — `AdditionalCostMethod`, `PurchaseBillAdditionalCost`,
`PurchaseBillAdditionalCostAllocation`; on `PurchaseBill`: `AdditionalCosts`,
`IsProductWiseAdditionalCost`, `AdditionalCostTotal`, `CapitalisedAdditionalCost`,
`AdditionalCostRoundingAdjustment`, `AllocationScale`, `AddAdditionalCost`, `ClearAdditionalCosts`,
`SetProductWiseAdditionalCost`, `AllocateAdditionalCosts`, `RecordAdditionalCostCapitalisation`,
`AllocatedAdditionalCostFor`; on `TenantSettings`: `DefaultLandedCostClearingAccountId`.

**Application** — `PurchaseBillAdditionalCostInput`; Create/Update commands, handlers and validators;
`PurchasingValidation.EnsureAdditionalCostsAreValidAsync`; the Approve handler's allocation,
capitalisation and conservation arithmetic; `PurchaseBillPostingInput`/`Rule` and
`DebitNotePostingInput`/`Rule`/resolver; the Get query's DTO; the accounting-defaults command and
query.

**Infrastructure** — two EF configurations, three new `PurchaseBills` columns, one
`TenantSettings` column, migration `Phase29PurchaseBillAdditionalCost` (additive only — no retype,
no reorder needed), and the `TestAppDbContext` restatements the InMemory provider requires.

**Api** — `PurchaseBillRequest` carries `AdditionalCosts` + `IsProductWiseAdditionalCost`;
`UpdateAccountingDefaultsRequest` carries the clearing account. Both on the **request record**, not
only the command — phase-27b's `Terms` warning.

**Angular** — the Additional Cost editor on the purchase-bill form (a `+ Add Additional Cost` link
that reveals the section, the "Add product-wise" checkbox, and rows of
`Cost Terms | Product | Method | Amount` defaulting to All Product / Value), the product-by-cost-term
matrix and residue on an approved bill, and the models behind them.

**A gap found and closed along the way.** The Accounting Defaults screen exposed only 10 of the
API's accounts: phase 25's `DefaultProductionCostAccountId` and phase 28's two forex accounts had
been added to the server and never to the client, so three server-side requirements had no way to be
configured through the app. Phase 29 needed a fourth, so all four were added. This is phase-23 bug
#1's shape in reverse — a field the server has and no screen can reach — and it is worth checking
for whenever a phase adds a tenant default.

---

## Manual E2E

Fresh Organization `Phase29 Landed Cost`, seeded entirely through the real API with curl + a cookie
jar. **Every status code printed** (phase-26c's lesson): login 200, organization 201, five account
groups 201, seven accounts 201, accounting defaults 200, warehouse/contact/category/unit 201, three
products 201, three cost terms 201.

Three seed-script traps, worth recording because each cost a round trip: `POST /api/organizations`
needs `workspaceName`, `accountingStartDate` and `turnstileToken` (the dev secret is Cloudflare's
always-pass dummy, so any token value works); an account takes **`groupId`**, not
`accountGroupId`; and Cost Terms live under **`/configuration/cost-terms`**, not at the organization
root. A fourth is a scripting trap rather than an API one: a helper that both prints a status and
returns an id via `$( )` has its status line swallowed by the capture — print it to **stderr**.

**The bill** — two goods lines and one service line, Freight 660 by Value and Custom Duty 300 by
Quantity, approved at `0001`. The SQL proof is in the TL;DR: layers at landed unit cost, the stored
allocation matrix, `ConservationLaw = HOLDS`, `EntryCheck = BALANCED`,
`InventoryCheck = AGREES WITH LEDGER`.

**Two negative paths at Create**, both proved live: a row naming the Service product → **409**; a
row naming the `ProductionCost` cost term → **404** ("...not an Additional Cost term"), which is
phase-20c's category split doing its job.

**The permission proof.** A second user was registered, verified (a registered user has **no**
verification code until `POST /api/auth/request-verification-code`; the code was read from
`[identity].VerificationCodes` — the brackets are mandatory, `identity` being a reserved word in
T-SQL), invited with the **Member** role and accepted. Member is seeded with
`Purchasing.PurchaseBill.Approve = false`, so:

- Member approving a **nonexistent** bill → **403** `"You do not have permission to perform this
  action (Purchasing.PurchaseBill.Approve)."`
- The same Member listing purchase bills → **200** (View is granted), so the 403 is about the key,
  not the session.
- **Admin** approving that same nonexistent id → **404** `"Purchase bill not found."` — which is what
  makes the 403 meaningful: `AuthorizationBehavior` fired before the handler ever looked for the row.

**Browser pass** over the new UI (dev-cert `erp-web-ssl` profile + the curl cookie transplanted via
`document.cookie`, the phase-25 Step 3 recipe). The approved bill renders the matrix
(`Products | Custom Duty | Freight`, with Helmet 100.00 / 0.66 and Motorbike 200.00 / 659.34 — the
Service line correctly absent), the line
`Capitalised into stock: 960.00 · rounding adjustment -0.0005`, a Grand Total of 60,11,000.00 that
excludes the additional cost, and the GL pair `Inventory 960.00 Dr / Landed Cost Clearing 960.00 Cr`.
On a draft, the editor offers `Cost Terms | Product | Method | Amount` with Custom Duty/Freight only
(Labour, a ProductionCost term, is correctly absent), `All Product` + the bill's goods products only,
`Value | Quantity` defaulting to Value — and ticking "Add product-wise" drops the `All Product`
option and pins the row to a product. No console errors.

---

## Bugs and snags hit

1. **Phase-24 bug #1, in a new place.** `AllocateAdditionalCosts` appended allocation rows to
   `PurchaseBillAdditionalCost` parents EF was already tracking, so they were detected as `Modified`
   rather than `Added` and six tests failed with
   `DbUpdateConcurrencyException: Attempted to update or delete an entity that does not exist in the
   store`. The documented remedy applied verbatim: the Domain method now **returns the allocations it
   created** and the handler `AddRange`s them through the child `DbSet`. Worth noting the symptom
   points at the handler and the cause is in the aggregate.
2. **A wrong expected value, not a wrong implementation.** The first Debit Note test expected
   Inventory to be credited 2,640 (4 x 660). The landed unit cost on that bill is 666, not 660 —
   `(6,000,000 + 660) / 10` — so 2,664 was right and the test was wrong. Recorded because the
   temptation on a red assertion in new arithmetic is to "fix" the code.
3. **`TestBed` allows one `configureTestingModule` per test**, so a spec calling the page helper
   twice fails with "the test module has already been instantiated" rather than an assertion. Split
   into two tests.
4. Two component-test stubs had to grow (`InboxService.listDocuments`,
   `OrganizationsService.listCurrencies`) — the purchase-bill page mounts phase-27a's source-document
   panel and phase-28's currency control, both of which fetch on render.

---

## Known limitations / follow-ups

- **No "Import" action on the product-wise matrix.** The live product offers one (a bulk paste of the
  grid); the matrix itself is fully editable cell by cell here.
- **The additional cost's own currency is assumed to follow the document's.** The live label says
  "Amount (NPR)" on a single-currency tenant, so this could not be settled — see Decision F.
- **No landed cost on any other document.** Only the Purchase Bill has the section live.
- **A Debit Note's release is proportional to quantity, not FIFO-exact.** It uses the source bill
  line's allocated cost per unit, which is exact for that bill; it does not attempt to follow which
  physical layer the return consumed when several bills supplied the same product.
- **The pre-existing Debit Note FIFO-vs-return-price gap is untouched.** Crediting Inventory the
  return price rather than the consumed FIFO cost is a Phase 6/7 modelling choice; this phase only
  ensures the *landed* portion no longer widens it.
- **No unwind path for the clearing account.** Nothing links the carrier's eventual bill to the
  clearing balance automatically — a tenant reconciles it by posting that bill against the same
  account, exactly as with any clearing account.
