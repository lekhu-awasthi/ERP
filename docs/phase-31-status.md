# Phase 31 — Credit control, dead settings, and the small carried items

**Roadmap heading "31. Credit control, dead settings, and the small carried items."**

## TL;DR

Customer **credit limits** enforced at Invoice Approve through the tenant's own **Credit Limit
Exceeds** policy; a **Configurations > General** screen that makes all five behaviour settings
reachable for the first time; **Negative Cash Balance** and **Suggest Selling Price / Product Price
Basis** finally read by something; a **stored Due Date** on Invoice and Purchase Bill; a **bounced
cheque** that unwinds the payment it settled; **subscription expiry** as read-only-for-documents plus
the renewal that lifts it; and the **Include Credit Note In Calculation** toggle.

**Six things worth carrying forward.**

1. **A setting with no command behind it is not a "dead setting" — it is an absent feature.** The
   roadmap called three `TenantSettings` fields dead. In fact **four of the five had no command, no
   endpoint and no screen at all**, and the fifth (`NegativeStockBalanceAction`) was *read* by
   `FifoStockAvailabilityPolicy` and still could never be moved off its seed. So the phase's first
   deliverable was not enforcement, it was reachability. Phase 29's rule ("grep `web/` for the field
   name before calling it done") generalises one step further: **grep for the command too.**
2. **The confirm-live pass paid for itself three times** (Decision A). It cost four deliberate writes
   on a shared UAT tenant and it settled: enforcement is at **Approve**, not save; the dialog's
   number is the **projected balance**, not the excess; a stored **0 means no limit**; and — flatly
   contradicting phase 26c — the **Include Credit Note In Calculation toggle is not inert**.
3. **When a second confirmable warning joins a first, they cannot share an override flag**
   (Decision D). An invoice can trip both a stock shortfall and a credit-limit breach. One shared
   `OverrideWarning` would mean acknowledging the stock dialog silently waived a credit breach the
   user was never shown. Two flags, plus a `warningKind` on the 422's ProblemDetails so the client
   knows which one it was shown.
4. **Reuse the marker set, don't invent a third** (Decision F). `SubscriptionExpiryBehavior` gates
   exactly what `ILockDateSensitive`/`ILockDateSensitiveDocument` already mark — which is precisely
   "a create, update, approve or void of a transactional document" across all fifteen types. No
   sweep, and the two gates can never drift apart.
5. **A second permission whose applicability depends on the requested *value*** is phase-27a's
   `AttachmentAccess` pattern for the second time. Bouncing a cheque needs `PaymentVoid` — but only
   when the linked payment is Approved, which `IRequirePermission.PermissionKey` cannot express.
   Proven both ways in the E2E: the same Member gets **404** on a nonexistent cheque (so they do hold
   `ChequeManage`) and **403 naming `Payments.Payment.Void`** on a real one.
6. **A non-nullable column added to a populated table needs its backfill written by hand.**
   `migrations add` scaffolded `DueDate NOT NULL DEFAULT '0001-01-01'`, which would have back-dated
   every historical invoice to the year 1 and left a stray default constraint. Rewritten as
   add-nullable → `UPDATE ... SET DueDate = Date` → alter-to-NOT-NULL.

Also: phase-23's `sweep-guard.spec.ts` caught a raw `<input type="date">` this phase introduced on the
Subscription page — the guard test earning its keep two phases after it was written.

---

## Step 1 — Confirm-live pass (2026-09-06, Moonbeam UAT tenant)

Recorded in full in `erp-module-scan.md`'s "Appendix, 2026-09-06". **Unlike every prior pass this one
included four deliberate writes**, with the user's explicit approval, because all three
Reject/Warn/DoNothing settings sat on **Do Nothing** and nothing could be observed read-only. Both
settings were restored afterwards and the state re-read to confirm it matched exactly. The test
invoice was **voided**, not left.

Four questions were open. All four were answered, and **two changed the plan**.

### 1. Credit Limit Exceeds fires at Approve, and the number is the projected balance

Saving a breaching draft produced **no warning at all**. Approve raised a dialog titled **"Crossed
Credit Limit"**, one row per offending contact — `Adhitya Bhandari (Rd0005)  Nrs.26,800` — with
**Dismiss / Continue**, the same shape as the Negative Stock Balance dialog the 2026-08 pass
recorded. The contact stood at 21,800 DR and the invoice was 5,000: **26,800 is the projected closing
balance**, not the 26,700 excess over the limit of 100.

That settles both the enforcement point and the arithmetic, and it is why
`ContactCreditLimitPolicy` returns a `ProjectedBalance` rather than an overage.

### 2. A stored zero means "no limit"

Every existing contact reads `credit_limit: 0`, and the contact above sat at 21,800 DR under that 0
with nothing firing; the dialog appeared only once 100 was saved. A nullable decimal would have
modelled the same thing, but 0-means-unlimited is what the live number input round-trips, and
matching it keeps an imported tenant from acquiring a limit of zero on every contact at once.

### 3. The credit block is per-contact, and a Lead has none of it

Contact Group is **Name / Under Group / Description only** — there is no group-level default to
inherit. And the toggle is **one field with two labels**: "Accept Purchase" while Type is Customer,
"Accept Sales" while it is Supplier. On a **Lead the whole additional block disappears** — no PAN, no
toggle, no Credit Terms, no Credit Limit.

### 4. "Include Credit Note In Calculation" is not inert — correcting phase 26c

Clearing it took the Sales Register from **19 rows to 8** and re-totalled it (taxable 71,324.41 →
139,280.06, tax 9,272.18 → 18,106.41). It removes the credit-note rows from the row set entirely, not
merely from the footer. Beside it sits a second, previously unrecorded toggle: **Group By Bill**
(not built — see Known limitations).

### Incidental findings

- **Configurations > General has no Save button** — a radio click saves immediately. This screen does
  not copy that; see Decision C.
- The **Invoice form has no Credit Terms field** at all (the Quotation form does), and its Due Date is
  its own required, editable input defaulting to the document date. Invoice Age shows due dates
  diverging from document dates by intervals no configured term could produce (03-06-2026 →
  27-08-2026). **Credit terms → due date prefill was not provable**: none of the seven customers
  sampled had a term set. What *is* proven is that Due Date is a stored column.
- The line picker calls `products-minimized?...&get_recent_selling_price=true` — Suggest Selling Price
  is decided **server-side**, at the picker.
- Contact Overview displays no credit limit anywhere.

---

## Scope decisions

### Decision A — the three Reject/Warn/DoNothing settings share a shape, not a mechanism

The roadmap asked whether they are one mechanism or three. **Three**, deliberately, sharing the
`BalanceAction` enum and the Ok|Warn|Reject result shape but nothing else:

| Setting | Policy | Consulted at | Warn override |
|---|---|---|---|
| Negative Item Balance | `IStockAvailabilityPolicy` (phase 7) | Invoice, Production Journal Approve | `OverrideWarning` |
| Credit Limit Exceeds | `ICreditLimitPolicy` (new) | Invoice Approve | `OverrideCreditLimitWarning` |
| Negative Cash Balance | `ICashBalancePolicy` (new) | Payment / CashTransfer / JournalVoucher Approve | `OverrideNegativeCashBalanceWarning` |

They ask different questions of different data at different documents, and a single
`IBalancePolicy<T>` would have been an abstraction over three unrelated queries whose only common
factor is a three-valued enum. What *is* shared is deliberate: the enum, the result shape, the 422
status, and `CashBalanceGuard`, which exists only because three approve handlers make the identical
Reject/Warn branch and the wording of a hard block versus a confirmable warning drifts once copied.

**Negative Item Balance's Warn and DoNothing branches remain partly fictional, and that is unchanged
from phase 26c.** `StockLedgerService.ConsumeAsync` throws on an oversell, so a shortfall that gets
past the setting still fails at the ledger. Making those branches real means allowing a negative
stock balance, which is a **Domain invariant change**, not a setting — it would need negative FIFO
layers, a costing answer for them, and a rework of every report that reads `QuantityRemaining`. Out
of scope, and the General page says so on its face rather than pretending otherwise.

### Decision B — credit limits are enforced for Customers, at Invoice Approve, and nowhere else

The tenant setting's own wording is "when a **Customer's** balance is about to exceed it's credit
limit", and the only live dialog is on Approve. So:

- `ContactCreditLimitPolicy` returns Ok for any non-Customer contact.
- A **supplier still carries a CreditLimit** because the live form offers one on a Supplier — but
  nothing reads it. There is a test that says so by name, so the field is not later mistaken for a
  half-built feature.
- A Quotation or Sales Order approving does not check: neither moves the receivable.

**The balance comes from `ContactLedgerReader`, not from the general ledger.** That is phase-26b's
shared-reader discipline: the figure the warning quotes is by construction the same figure Contact
Overview and Customer Statement show, so a user told "26,800 exceeds your limit" can open the contact
and find 26,800. Deriving it from `GlLine` would have produced a second answer to one question.

**Currency**: the document's total is compared un-converted, because `ContactLedgerReader` sums its
events un-converted too. Converting only the new document would compare a base-currency figure
against a mixed-currency running total and match neither report. This is inherited from phase 28's
carried limitation, not a new one, and it is exact on a single-currency tenant.

### Decision C — the General page keeps an explicit Save

The reference page auto-saves each radio on click. This one does not. One radio can turn a hard stop
into a silent allow across the entire organization; every other settings screen here (Accounting
Defaults, Lock Date) has a Save; and the whole form is one command over one row, so per-field
commands would have meant five endpoints, five validators and five authorization checks for an
aggregate written perhaps twice in a tenant's life.

**Inventory Tracking Mode is on the command but not on the form.** The reference product removed that
control from this page and this codebase ships `AccountingMovement` only — but dropping it from the
command would make the field permanently unwritable again, which is the exact fault this phase
exists to fix. The page sends the loaded value straight back, and there is a test pinning that.

### Decision D — two warnings, two flags, and a `warningKind` on the wire

An invoice can trip the stock warning and the credit-limit warning independently, and the live
product shows two dialogs with their own Dismiss/Continue. Sharing `OverrideWarning` would let a
Continue on the stock dialog waive a credit breach the user never saw.

So: `CreditLimitWarningException` is its own type with its own flag, and
`ExceptionHandling` adds a `warningKind` extension (`StockAvailability` | `CreditLimit` |
`NegativeCashBalance`) to the 422's ProblemDetails. The client sets only the flag matching the kind
it was just shown, so the second dialog appears on the next attempt exactly as it does live.

### Decision E — credit-limit enforcement lives in the handler, not on a pipeline behavior

Phase-20f's precedent is explicit: a conditional gate cannot ride a marker-interface behavior.
`LockDateBehavior` and `FeatureGateBehavior` work because everything they need is on the request.
This check needs the document's **lines** (for its total), the contact's whole approved ledger, and
the tenant's setting, and it must run after the handler has loaded the document but before it draws
a number and posts. It is also the shape phase 7 chose for the identical problem — and having two
members of the same setting family behave differently would be worse than either choice alone.

### Decision F — the subscription gate reuses the lock-date markers

`SubscriptionExpiryBehavior` is the fifth pipeline behavior and gates exactly the set
`ILockDateSensitive`/`ILockDateSensitiveDocument` already mark. That set is precisely "a create,
update, approve or void of a transactional document" across all fifteen types — the same set a lock
date freezes, for the same reason: it is what "the books" means.

What still works past expiry, deliberately: **every query** (read-only has to mean readable),
**configuration edits** (an expired tenant should not also be locked out of fixing a wrong account),
and **`SetTenantSubscriptionCommand`**, which is the one command that can lift the expiry. That last
exclusion is not luck — the renewal carries no document date and could not implement either marker.

`TenantSubscription.Renew` moves the **plan name and end date only**. The entitlement flags stay as
immutable as phase 20f made them: a renewal is a billing event, not a re-negotiation of what the
tenant may model, and letting it flip `TrackInventoryEnabled` would let a tenant with stock in a FIFO
ledger turn inventory off underneath it.

**No live evidence exists for what expiry actually does** — the reference Subscriptions screen shows
"expire in 59 days" and no expired tenant is observable. Read-only-for-documents is derived from the
roadmap's wording and recorded as derived.

### Decision G — Due Date is a stored value, not a formula

Confirmed live: the Invoice form has its own required, editable Due Date and no Credit Terms field,
and Invoice Age shows due dates no configured term could produce. So the contact's `CreditTermId` is
a **prefill source consulted once**, when a contact is picked, and never again. Non-nullable,
defaulting to the document's own date — which is exactly what the ageing reports were already
improvising, and exactly what the migration backfills.

Taking it closed three things at once: phase-26b's carried item, phase-30's `$[DUE_DATE]$` (one line
in `EmailMergeValueReader`), and — with the JV-allocation fix beside it — phase-26b's follow-up #4,
so `ContactAgeingSummaryQueryHandler` and `DocumentAgeQueryHandler` can no longer disagree about the
same outstanding bill.

### Decision H — Product Price Basis applies to the product's price, never to a recent sale's rate

The live setting says so in its own words ("whether the price defined in the *product's details* is
inclusive or exclusive of VAT"), and the reasoning holds independently: a recent sale's rate is a
stored `InvoiceLine.Rate`, which this codebase already keeps VAT-exclusive, so back-calculating it
again would strip 13% from a figure that never carried it. There is a test for exactly that.

"Recent" means the most recent **approved Invoice** line. Quotations and Sales Orders are excluded —
a quoted price that was never accepted is the wrong thing to propagate — and Credit Notes for the
mirror reason.

### Decision I — permission keys

| Key | Grant | Reasoning |
|---|---|---|
| `Configuration.GeneralSettings.Manage` | Admin only | **One key, not a View/Manage pair**, following `AccountingDefaultsManage` exactly: same kind of thing, one screen behind it, and its values are read by handlers rather than by a caller who needs a View grant. A Member has no use for reading "Negative Cash Balance = Reject"; they experience it as a 409. A View key here would be checked by nothing. Admin-only because one radio changes how every document in the tenant behaves at Approve — the flat-per-tenant-control-plane end of the derivation rule, the same bar as LockDate. |
| `Tenancy.Subscription.Manage` | Admin only | It decides whether the organization is read-only, so a Member holding it could lift their own tenant's expiry. |

**No new key for credit control.** Setting a customer's credit limit is editing a contact
(`Contact.Manage`, Member-granted); enforcing it is approving an invoice (`Sales.Invoice.Approve`).
A separate key would be a weaker gate standing in front of a stronger one — phase-24's reasoning. And
the disclosure argument does not bite: a Member who can open a contact already sees that contact's
full ledger balance on the Overview tab, so the limit adds nothing new.

**The one genuinely new gate is not a key at all**: bouncing a cheque additionally requires
`Payments.Payment.Void`, re-checked inside the handler because whether it applies depends on the
requested status and on the linked payment's own status — neither visible to the pipeline. Phase-27a's
`AttachmentAccess` pattern, second use, down to throwing the identical `ForbiddenException` shape.

### Decision J — a bounced cheque *voids* the payment rather than posting a bare reversal

Phase-17 decision #4 recorded "no automatic reversal" as a safe default. Closing it, the temptation
was to post a reversing entry and leave the Payment Approved. That would have produced a document
whose status and whose ledger disagree — and worse, a later Void of that same Payment would reverse
it a **second** time. Reusing `Void` + `GlJournalEntry.PostReversalOf` makes the double-reversal
unrepresentable, because Void is also what moves the status away from Approved.

A cheque whose payment is still Draft, or already Void, bounces with no ledger effect and needs no
second permission.

### Decision K — what was left out, and why

- **Import for Account / Product Category / Account Group / Contact Personnel / variants**
  (phase-21a and 24's deferred lists) and **export date range + extra categories** (21b): each is a
  template shape plus a row reader, and together they are a phase, not a tail. Explicitly deferred
  with the user.
- **Group By Bill** on the Sales Register: found live this pass, not in the roadmap, and it changes
  the report's row *grain* rather than its filter. Recorded, not built.
- **Negative Item Balance's Warn/DoNothing branches** — see Decision A.

---

## What was built

**Domain.** `TenantSettings.CreditLimitExceedsAction`; `Contact.CreditLimit` / `CreditTermId` /
`AcceptsReverseTransactions` (all three forced off for a Lead, in the aggregate);
`Invoice.DueDate` and `PurchaseBill.DueDate`; `TenantSubscription.Renew`.

**Application.** `ICreditLimitPolicy`/`ContactCreditLimitPolicy`;
`ICashBalancePolicy`/`GlCashBalancePolicy` + `CashBalanceGuard`; `SuggestProductRateQuery`;
`Get`/`UpdateGeneralSettings`; `SetTenantSubscriptionCommand`; `SubscriptionExpiryBehavior` (5th
pipeline behavior); `CreditLimitWarningException` and `CashBalanceWarningException`; the bounce
unwind in `TransitionChequeStatusCommandHandler`; `SalesRegisterQuery.IncludeCreditNotes`; real due
dates through `DocumentAgeQueryHandler`, `ContactAgeingSummaryQueryHandler` and
`EmailMergeValueReader`.

**Api.** `GET`/`PUT /general-settings`, `PUT /subscription`, `GET /products/{id}/suggested-rate`; the
`warningKind` ProblemDetails extension; override flags on four approve endpoints; `dueDate` and the
three credit fields on the Invoice / Purchase Bill / Contact request records.

**Infrastructure.** One migration: the six columns, four permission-seed rows, and a hand-written
`DueDate` backfill.

**Angular.** A **Configurations > General** page (route, nav card, spec); the credit block on the
Contact form with its two-label toggle and its Lead branch; Due Date + credit-term prefill on the
Invoice and Purchase Bill forms; server-side rate suggestion on all four sales screens; `warningKind`
routing on five approve screens; the Sales Register toggle; the Renew control on the Subscription
page; the bounce confirmation and its outcome message on the Cheque Register.

---

## Step 4 — Manual E2E (fresh Organization, seeded by curl, every status code printed)

Thirteen proofs against the real API and database. Highlights:

| # | Proof | Result |
|---|---|---|
| 1 | All five General settings round-trip | GET → PUT → GET identical |
| 2 | Product Price Basis, product price 113 @ 13% | Inclusive → **100.0**; Exclusive → **113.0** |
| 3 | Due Date stored / defaulted | explicit `2026-02-09`; omitted → `= date` |
| 4 | Credit Limit = Warn | **422** `warningKind: CreditLimit`, "…takes Acme Traders (0001) to **1400**, past their credit limit of 1000"; override → **200** |
| 5 | Credit Limit = Reject | **409** *even with* the override flag set |
| 6 | Credit limit 0 | a **9,999,999** invoice approves |
| 7 | Lead | `creditLimit=0 creditTermId=None acceptsReverse=False` |
| 8 | Negative Cash Balance | Reject → **409** "…take Everest Bank (0015) to **-250**"; Warn → **422** `warningKind: NegativeCashBalance`; override → **200** |
| 9 | Include Credit Note toggle | 6 rows → 5 rows, total moves |
| 10 | Invoice Age | invoice `0001` dated 10-01 with due 09-02 ages **10 days** while every other invoice ages 38–39 from its own date |
| 11 | Cheque Bounced | `voidedPaymentCode: "0003"`, payment `Void`, **2** GL entries, **every account nets to 0.0000** in `sqlcmd` |
| 12 | Subscription expiry (forced past in SQL) | create → **409**, `GET invoices` → **200**, `GET general-settings` → **200**, renew → **200** with entitlements intact, same create → **201** |
| 13 | Negative path, as an **accepted Member** | see below |

**The negative path, in both directions.** The Member is refused
`Configuration.GeneralSettings.Manage` (GET *and* PUT) and `Tenancy.Subscription.Manage`, each 403
naming the exact key. Then the pair that proves the in-handler gate rather than the route:

- the same Member transitioning a **nonexistent** cheque gets **404 "Cheque not found."** — so they
  do hold `Configuration.Cheque.Manage` and the route works for them;
- the same Member bouncing a **real** cheque whose payment is Approved gets **403 "You do not have
  permission to perform this action (Payments.Payment.Void)."**, and the payment is still `Approved`
  on re-read.

**Browser pass** (dev-cert SSL profile, curl's `erp_auth` transplanted per phase 25's Step 3):
the General page renders 13 radios matching the live page and its Save persists (verified back
through the API); the Contact form's toggle reads **Accept Purchase** on a Customer, **Accept Sales**
on a Supplier, and the whole block vanishes on a **Lead** — reactive in a zoneless app; picking Acme
Traders on a new invoice moved Due Date from 06-09 to **05-10** (Net 30); picking the product filled
the rate with **9,999,999**, the most recent approved sale, proving the server-side suggestion
end-to-end; Approve raised the confirm with "…to 2189, past their credit limit of 1000" — **Dismiss**
left it Draft with the message on the page, **Continue** approved it as `0006`; and the Sales
Register toggle took 7 rows to 6 with the total moving by the credit note's 200.

---

## Bugs and traps hit

1. **The scaffolded `DueDate` migration would have back-dated every historical document to year 1.**
   `migrations add` emits `NOT NULL DEFAULT '0001-01-01'` for a non-nullable column on a populated
   table, and leaves the default constraint behind. Rewritten by hand as three steps.
2. **`sweep-guard.spec.ts` caught a raw `<input type="date">`** added on the Subscription page —
   phase-23's guard test firing two phases later, exactly as intended. Replaced with
   `app-bs-date-input`.
3. **A duplicate `ConfigurationService` import** on the Purchase Bill page: the patch script inserted
   one without noticing the file already had it 23 lines further down. `ng build` caught it; `tsc
   --noEmit` would not have (phase 28).
4. **An expired subscription cannot be built through its own aggregate.** `Renew` refuses an end date
   before the start date and a trial starts *now*, so the unit test reaches through EF's change
   tracker rather than weakening the invariant to make itself easier to write.
5. **`POST /api/organizations` needs `industry` and a non-empty `turnstileToken`** — neither was in
   the carried trap list. Any non-empty token passes against the dummy secret.
6. **`accept-invitation` has no organization segment**: it is
   `/api/organizations/memberships/{id}/accept-invitation`. Calling it with an org id returns a 404
   that reads exactly like a bad membership id, and the membership silently stays `Invited` — which
   would have made the whole 403 proof meaningless (a non-member is refused for a different reason).
7. **Two seed routes are not where they look**: units are `/units-of-measurement` (field
   `shortName`, not `symbol`), and credit terms are under `/configuration/credit-terms`.
8. **A `cat > file <<'EOF'` heredoc truncated a 190-line Angular template at line 178** — CLAUDE.md's
   documented gotcha, hit again. Used the Write tool.

---

## Known limitations and follow-ups

1. **Negative Item Balance's Warn and DoNothing branches are still partly fictional** — see
   Decision A. Real support needs negative FIFO layers, which is a Domain invariant change.
2. **A supplier's credit limit is stored and never enforced** (Decision B), matching the live form.
3. **The credit-limit comparison does not convert currency** — inherited from phase 28's carried
   limitation on the whole contact-ledger family, not introduced here.
4. **Credit terms → due date is a client-side prefill only.** A document created straight through the
   API with no `dueDate` gets the document date, even for a contact with a term. The live product
   behaves the same way (its Invoice form has no Credit Terms field), but it was never *proved*
   live — no sampled customer had a term set.
5. **Subscription expiry behaviour is derived, not confirmed** (Decision F): no expired tenant is
   observable on the UAT instance.
6. **The expiry gate does not cover configuration writes.** An expired tenant can still edit its
   chart of accounts and its contacts. Deliberate (Decision F), but it means "read-only" is
   "read-only for the books", not for everything.
7. **Group By Bill** on the Sales Register is unbuilt (Decision K).
8. **Import and export breadth** (21a/24/21b's deferred lists) remain open (Decision K).
9. **The Renew control has no plan catalogue** — the plan name is free text, because there is no plan
   model to pick from and inventing one would be modelling a billing system this product does not have.

---

## Tests

Domain **398**, Application.UnitTests **830** (+34), Api.IntegrationTests **18**, Angular **207**
(+8). `dotnet build` / `dotnet test` / `ng build` / `ng test` all clean.

The new Application suites live under `tests/Application.UnitTests/CreditControl/`:
`GlCashBalancePolicyTests`, `CreditLimitEnforcementTests`, `SuggestProductRateQueryHandlerTests`,
`GeneralSettingsAndSubscriptionTests`, `ChequeBounceReversalTests`, `DocumentDueDateTests`, over the
shared `CreditLimitTestSeed`. Client-side: `general-settings-page.spec.ts` and `api-error.spec.ts`.

Two of those tests exist to pin things that are *not* features, so they are not later mistaken for
half-built ones: `A_suppliers_credit_limit_is_stored_but_never_enforced` and
`An_account_that_is_not_Bank_or_Cash_kind_is_never_checked`.
