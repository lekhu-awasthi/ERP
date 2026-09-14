# Phase 41 — The subscription and plan model, and the price list that falsified a decision

**Roadmap heading "41. Subscription and plan model — needs a product decision before it is a phase."**

## TL;DR

The phase opened with a question — *is this product selling plans at all?* — and the answer came from
a document nobody had read: **the vendor's own public price list**. Tigg sells three metered tiers,
and the three fields phase 33 recorded as dead (Subscription Amount, the two quotas, IRD Verified)
are not dead at all. **They are the product.** Both tenants phase 33 read were free trials, which is
what `0.00` and `Standard ( 0 Txn, 0 Products)` mean.

So phase 41 built the plan catalogue, the commercial terms on `TenantSubscription`, **quota
enforcement on both axes**, and the shell banner that makes an ending subscription foreseeable
instead of a 409 mid-approval.

**Seven things worth carrying forward.**

1. **The cheapest confirm-live experiment was not a tenant — it was the seller's price list.**
   Phase 32's lesson says a field dead on the tenant you looked at is a fact about that tenant, and
   the remedy it prescribes is *another tenant, account or plan*. None was available. The public
   pricing page settled it in one page load, and it settled more than a third tenant would have: it
   gave the exact ceilings, the exact prices, the add-on catalogue, **and a one-sentence definition of
   the metered unit**. Before asking for another tenant, read what the vendor publishes.
2. **Phase 33 Decision D was wrong, and it was wrong in the way a careful decision fails.** It did not
   guess — it reasoned correctly from phase 31's rule ("a tenant-level field is only reachable if you
   can name the command that writes it") to "don't build columns nothing can write". What it got
   wrong was the *premise*: it read two samples of the same kind and generalised. This is phase 30's
   "a list sampled from a few screens becomes a wrong list — find the rule", one level up: **a value
   sampled from two tenants of one kind becomes a wrong fact about the field.**
3. **The metered set is not a judgement call; it is the vendor's Terms applied.** *"Active
   transactions refers to all transactions in which accounting entry are affected."* That sentence
   selects exactly the transactional types that post to the GL — eleven — and excludes Quotation,
   SalesOrder, PurchaseOrder and ProductionOrder for a stated reason rather than a feeling. Proved
   live in the E2E: a Quotation approves fine while the tenant sits at its transaction ceiling.
4. **Zero is the "not metered" sentinel, and it is the most dangerous number in the phase.** Read as
   a ceiling instead, it tells every trial tenant — which is every new tenant — that it is out of
   allowance on day one. Phase 31's credit limit set the precedent (a stored 0 means no limit,
   confirmed live), and it is what makes the observed `( 0 Txn, 0 Products)` coherent.
5. **Usage is derived, never cached, and the cost is paid only by tenants who bought a ceiling.**
   One `SubscriptionUsageReader` behind both the gate and the screen, so the bar a user sees and the
   refusal an Approve gets are the same number *by construction* — phase 26b's rule, which phase 36
   had to enforce the hard way when two ageing reports were patched into agreeing by coincidence.
   A quota of 0 short-circuits before any query, so the common path costs nothing.
6. **A quota is only a ceiling if someone other than the buyer sets it — and here nobody else can.**
   `Tenancy.Subscription.Manage` is seeded to the tenant's own Admin role, so the party the ceiling
   constrains can raise it with one `PUT`. That is not a wiring oversight: this codebase models
   **exactly one actor**, and a subscription is inherently a two-party record. Named loudly in the
   behavior, the command and here rather than left to be discovered. See Decision G.
7. **The browser pass found a defect the whole test suite could not.** The shell banner and the
   Subscription screen show one fact and only one of them changed it, so recording a Standard plan
   left the banner saying *"366 days remaining in your trial"* on the very page that had just changed
   it. Phase 34b's rule one surface over — *anything global a screen both shows and changes must
   reload when it changes* — and it took `SubscriptionStore` to fix.

Tests: Domain **660 (+20)**, Application.UnitTests **1081 (+21)**, Api.IntegrationTests **29 (+0)**,
Angular **440 (+14)**. `dotnet build` / `dotnet test` / `ng build` / `ng test` all clean.

---

## Step 1 — The decision, and the evidence that made it

The roadmap posed three coherent answers (no / yes-but-sold-elsewhere / yes-in-product). The kickoff
added a method note from phase 40 Decision F: **check whether the question's premise is false before
answering it.** It was, twice over.

### The premise check: "selling plans" is two questions, not one

A subscription has **two parties** — the vendor who sells and the tenant who buys. Every item in
phase 41's scope is really *"which party owns this field?"*, and this codebase models one party:
every table carries an `OrganizationId` discriminator, every permission is a tenant role permission,
there is no cross-tenant entity and no platform-administrator concept anywhere.

So the question splits cleanly:

- **Are plans real, priced and metered?** — a question about the product.
- **Does this codebase have anywhere to put a vendor's decisions?** — a question about the
  architecture.

The answers are **yes** and **no**, and phase 41 is the consequence of that pair.

### The evidence (tiggapp.com/pricing, read 2026-09-14)

| | BASIC | STANDARD | PROFESSIONAL |
|---|---|---|---|
| **Annual price** | Rs 15,000 | Rs 20,000 | Rs 32,000 |
| **Products** | 1,000 service items | 5,000 | 10,000 |
| **Transactions / year** | 30,000 | 50,000 | 200,000 |
| Multiple currency | ✓ | ✓ | ✓ |
| Inventory tracking | ✗ | ✓ | ✓ |
| Multiple warehouses | ✗ | ✓ | ✓ |
| Landed cost calculation | ✗ | ✓ | ✓ |
| Production feature | ✗ | ✗ | ✓ |
| POS (Retail/Restro) | ✗ | ✗ | ✓ |
| Daraz integration | ✗ | ✗ | ✓ |
| Developer API | ✗ | ✗ | ✓ (support fee) |

**Add-ons:** IRD Billing Rs 15,000 one-time; Production Rs 5,000/yr; Billing Location Rs 5,000 per
location per year; Products Rs 1,000 per additional 1,000; Transactions Rs 1,000 per additional
10,000; SMS Rs 0.99 per credit.

**Terms, verbatim and load-bearing:** *"Active transactions refers to all transactions in which
accounting entry are affected."* Also: *"Read-only access to data will be charged at 25% of the
subscription fee"* and *"13% VAT is applicable in all prices unless otherwise specified."*

Three further readings fell out of that page:

- **The seven entitlement flags this codebase already has ARE the tier matrix.** `TrackInventory`,
  `MultipleWarehouses` = Standard and up; `Manufacturing`, `PosRetail`, `PosRestaurant` =
  Professional (or the Production add-on); `MultipleLocations` = the per-location add-on. And
  `MultiCurrency` is in **every** tier — which is finally the explanation for phase 20f's puzzled
  observation that Multi-Currency was *the only user-operable switch on the whole Features page*. It
  is not an entitlement Tigg sells, so it is a preference.
- **Nothing is sold inside the app.** Every "Get Started" button, on all three paid tiers, points at
  `me.tiggapp.com/erp/#/register` — the free-trial signup. There is no checkout, cart or payment page
  anywhere on the site. This corroborates the in-app evidence the scan already held: the trial
  banner's action is **CONTACT US** (Cadehi, 2026-09-10). Buying a plan is a phone call and a bank
  transfer, after which a human provisions the tenant.
- **"Read-only access" is a product, not a fallback.** Phase 31 derived read-only-for-documents from
  the roadmap's wording and recorded it as derived. The Terms show the natural end state *is*
  read-only — and that it is **sold at 25%**. So 31's derivation was closer to right than it knew,
  while being about something the vendor charges for.

### The decision, and what the user chose

Put to the user with the evidence, framed as four depths (record only / record and enforce / build the
vendor side first / just fix the dishonest bits). **The user chose "record it AND enforce the
quotas"**, having been told in the option text that enforcement stays self-liftable while
`SubscriptionManage` sits on tenant Admin. That caveat is therefore a known, accepted limitation, and
this phase's job was to make it loud rather than silent (Decision G).

---

## Scope decisions

### Decision A — `SubscriptionPlan` is a seeded catalogue with no command behind it

The first aggregate in this codebase with **no `OrganizationId`**. A plan is the vendor's data: the
same three rows for every tenant, which no tenant may edit.

*Why seeded rather than tenant master data.* The kickoff's middle answer ("plans are sold outside the
app, so the catalogue is master data") is half right — the catalogue is not transactional, but making
it *tenant* master data would let each tenant invent its own plans and its own prices, which is
exactly the two-party confusion the phase exists to resolve. `HasData` in
`SubscriptionPlanConfiguration` means changing the price list is a migration. That is an honest
representation of the fact that nobody inside the product may change it, and a far better failure
mode than a screen that lets a tenant write a plan called "Unlimited".

*It does not trip `TenantIndexConvention`.* That convention skips any entity with no `OrganizationId`
property at all (`TenantIndexConvention.cs:121`), so the new table needs no exemption entry — checked
before writing the configuration, because CLAUDE.md records that the convention *throws* for an
entity it cannot classify.

*The `...Included` columns are descriptive, not operative* — see Decision F.

### Decision B — the metered set is the vendor's own definition, applied

`DocumentMechanisms.MeteredTransactions`: **eleven** types. Invoice, CreditNote, Payment,
PurchaseBill, Expense, DebitNote, JournalVoucher, CashTransfer, WarehouseTransfer,
InventoryAdjustment, ProductionJournal.

*What is excluded and why.* Quotation, SalesOrder, PurchaseOrder and ProductionOrder approve without
posting — no accounting entry is affected, which is the Terms' own test. `OpeningBalance` and
`OpeningStock` do post (they are 2 of `GlSourceDocumentResolver`'s thirteen) but are **opening setup,
not trade**: keyed by their own natural key, edited in place, no Draft/Approve lifecycle to meter.
Charging a tenant's annual allowance for entering its own opening position would bill it for
migrating in.

*Marked on Approve, not Create.* That is where the number is assigned and the GL entry posted. It
also means a tenant does not burn allowance on drafts it abandons.

*A third marker, not a reuse.* Phase 31's `SubscriptionExpiryBehavior` reuses
`ILockDateSensitiveDocument` because the set it wanted happened to be exactly that set. This set is
not: it excludes every Create and Update, excludes **Void** (voiding must not cost a second unit, and
must stay possible at the ceiling or a tenant at its limit could never correct a mistake), and
excludes the four non-posting types. Phase 32b's rule — *reuse a marker by reading it, not merging
it, when the member sets differ.*

*Guarded in three directions.* `MeteredTransactionSweepGuardTests` asserts the list against the
commands implementing `IMeteredTransaction` both ways, **and** against `GlSourceDocumentResolver`'s
own thirteen with the two exclusions written down — so a fourteenth GL-posting type fails the build
until someone decides which side it falls on. Proved to bite by removing the marker from
`ApproveCreditNoteCommand`: 2 of 8 assertions failed; restored and verified **by hash**
(phase 34a's rule — never `git checkout --`, which would revert the phase's own work on that file).

### Decision C — zero means "not metered", on both axes

Phase 31's credit limit, confirmed live there: **a stored 0 means no limit.** It is what makes a
trial usable, it is what every pre-phase-41 tenant carries after the migration, and it is the reading
under which the live `Standard ( 0 Txn, 0 Products)` is coherent rather than mysterious.

The asymmetry worth noting: **a `SubscriptionPlan` may not carry a zero quota.** Every published tier
states both ceilings, so a catalogue row with a zero would be a plan that silently sells unlimited
use — the one mistake in this aggregate that costs money. The Domain factory throws.

The banner's spec pins this directly (`treats a zero quota as no limit rather than as an exhausted
one`), because a naive `used >= quota` passes every other test in the file.

### Decision D — usage is derived, never cached

A stored counter would be one row to read, and it drifts. It has **two writers** (approve, and any
direct data fix), so phase-21a's rule against a concurrency token on a two-writer row applies, and
nothing would reconcile it. Counting means the number is always the truth.

The cost is bounded by the thing that makes it safe: **a quota of 0 short-circuits before any
query**, so the count never runs for a trial, and the behavior asks for a figure only when a ceiling
exists to compare it to.

*Distinct source documents, not GL entries.* Phase 36 retired "one GL entry per approved document":
a void posts its reversal as a second entry against the same source. Counting rows would charge a
tenant twice for approving and then voiding one invoice. The count is over distinct
`(SourceDocumentType, SourceDocumentId)`, so **a voided document consumes one unit** — an accounting
entry *was* affected, which is the Terms' test.

*One reader, two consumers.* `SubscriptionUsageReader` backs both `SubscriptionQuotaBehavior` and the
`GetTenantSubscriptionQuery` DTO, so the meter on the screen and the refusal on Approve cannot
disagree. Phase 26b's agree-by-construction; phase 36's correction that two call sites deriving "the
same" figure merely coincide.

**Known cost, stated:** on a tenant at the Professional ceiling the count scans up to 200k
`GlJournalEntry` rows per approve. `(OrganizationId, PostedAt)` exists via `TenantIndexConvention`,
and phase 34c's discipline says re-measure rather than assume — this was **not** measured against the
50k dataset. See carried item #3.

### Decision E — `TermStartsAt`, a second date

The quota is "transactions / year" against a term that is bought and re-bought.

- Counting from `TrialStartsAt` would meter a five-year-old tenant against the allowance it purchased
  *this* year plus everything it has ever done.
- Counting back a fixed year from the end date would silently re-open the allowance whenever an end
  date moved.

The honest window is the term actually paid for, which needs both ends stored. `TrialStartsAt` keeps
its phase-31 meaning (the tenant's origin, never moved); `TermStartsAt` is what a renewal advances.
Confirmed live in the browser pass: after recording a Standard plan, usage read `0 of 50,000` even
though three invoices had been approved minutes earlier — because they fall before the new term.

`TrialEndsAt` keeps its name despite now describing paid terms too. Renaming it is a sweep across the
Domain, the DTO, the Angular model and every consumer; deliberately not taken on — carried item #2.

### Decision F — a plan change still does not touch the entitlement flags

Phase 31's rule, unchanged, and now worth restating because a plan row carries its own `...Included`
columns and the obvious next step is to copy them across on renewal.

It must not. A billing event is not a re-negotiation of what the tenant may model, and letting it
flip `TrackInventoryEnabled` would let a tenant with stock already in a FIFO ledger turn inventory off
underneath it. So the plan's columns are **descriptive** — they say what the published tier includes,
so the picker can show the same ticks and crosses the website does — and a mismatch between a tier and
a tenant's actual flags stays real and visible rather than silently reconciled.

Reconciling them is a *vendor* action. See Decision G. `SetPlan_leaves_every_entitlement_flag_alone`
pins it with a tenant holding Manufacturing and POS on a Standard plan that includes neither.

### Decision G — the missing actor, named rather than worked around

**`Tenancy.Subscription.Manage` is seeded to the tenant's own Admin role**
(`RolePermissionConfiguration.cs:1272`). So the party a quota or an expiry constrains can lift it:
one `PUT /subscription` sets any plan, any end date and any ceiling, for free, forever.

This is not fixable inside this phase's scope, and it is worth being precise about why. It is not a
mis-seeded permission — there is **no other role to give it to**. Every actor this codebase can
express is a member of the tenant. Moving the key to a "vendor" role would mean inventing a
cross-tenant bounded context, a separate identity, and a permission model that is not
`OrganizationId`-scoped: a phase of its own, arguably larger than 41.

What phase 41 does instead is refuse to let the gap be discovered by accident. It is written into
`SubscriptionQuotaBehavior`'s doc comment, `SetTenantSubscriptionCommand`'s doc comment, and here.

**The honest characterisation:** these ceilings are an accurate record of what was sold and a real
guard against a tenant drifting past it *unnoticed*. They are not a control that survives an
adversary. For the actual product that is close to sufficient — the commercial relationship is a
phone call, and a customer who edits their own quota to avoid a Rs 1,000 add-on has done something a
vendor can see in the data — but it should never be described as enforcement against a motivated
tenant.

**Re-entry condition:** a vendor-side console, or any second actor outside `OrganizationId`. At that
point `SubscriptionManage` moves to it and every line of this phase keeps working unchanged.

### Decision H — the trial banner, and the store the browser pass forced

Before this phase, trial status rendered on **exactly one screen**, four clicks deep in
Configurations. Expiry has been enforced since phase 31, so a tenant's first knowledge of it was a
409 on an invoice they were mid-approval on. The reference product does not do that: its dashboard
carries a persistent *"12 days remaining in your trial account. Upgrade now…"* banner with a
**CONTACT US** action (Cadehi, 2026-09-10).

`app-subscription-notice` lives in the shell header and states whichever of five things is most
urgent, in a decided order: **ended > allowance spent > trial or approaching end > allowance low**.
A trial always speaks (read from the reference tenant, whose banner showed at 12 of 15 days); a paid
plan speaks only inside 14 days or at 90% of a ceiling.

**No live-region role, deliberately.** Phase 40's rule cuts both ways: a region created already
holding its text announces nothing, *and* `role="alert"` on furniture that renders on load interrupts
whatever is being read. This banner is furniture. `app-status-banner` remains the right tool for the
other case, and the Subscription screen still uses it.

`SubscriptionStore` is the fix for the defect the browser pass found — see Bugs, below.

### Decision I — permission keys: none added

`SubscriptionView` (Admin + Member) already gates reading the subscription, because the Angular shell
reads it to decide which feature-gated nav entries to render. Seeing *what plans exist* is strictly
less than that, so `ListSubscriptionPlansQuery` reuses it. Writing is `SubscriptionManage` (Admin
only), which already existed. **No new `PermissionKeys` constants and no permission-seed migration** —
which also means none of phase 9's hand-written-seed-migration trap applies here.

The catalogue query is `IOrganizationScoped` even though its data is global. Nothing is protected —
price lists are public — but `AuthorizationBehavior` is the only thing in this codebase that verifies
org membership at all, and a query that skipped the interface would be reachable by any authenticated
user of any tenant. Scoping it keeps it from becoming the exception that teaches the next one to skip
it (phase 12's lesson).

### Decision J — what was left out

- **Quota enforcement on the other add-on axes** (billing locations, SMS credits, AI scans). The
  price list meters these too. Products and transactions are the two the reference screen displays
  and the two that separate the tiers; the rest are add-on lines without an observable ceiling.
- **A "usage history" or per-month breakdown.** Nothing in the reference product shows one.
- **Any payment flow.** Settled by the evidence, not deferred for effort: there is no checkout in the
  reference product or on its website.
- **Renaming `TrialStartsAt`/`TrialEndsAt`.** Carried item #2.

---

## What was built

**Domain.** `SubscriptionPlan` (new aggregate, no `OrganizationId`);
`TenantSubscription.PlanId`/`TermStartsAt`/`SubscriptionAmount`/`ProductQuota`/`TransactionQuota`/
`IrdVerified`; `Renew` widened into `SetPlan`; `DocumentMechanisms.MeteredTransactions`.

**Application.** `IMeteredTransaction`/`IMeteredProduct` markers;
`SubscriptionQuotaBehavior` (the **sixth** pipeline behavior); `SubscriptionUsageReader` +
`SubscriptionUsage`; `SubscriptionQuotaExceededException` + `SubscriptionQuotaKind`;
`ListSubscriptionPlansQuery`; `SetTenantSubscriptionCommand` widened; `TenantSubscriptionDto` gains
plan, term start, amount, IRD Verified and usage; the eleven Approve commands and
`CreateProductCommand` marked.

**Api.** `GET /organizations/{id}/subscription-plans`; `PUT /subscription` widened (every optional
member restated on the Api's own request record — phase-27b's trap); the `quotaKind` / `quotaUsed` /
`quotaLimit` ProblemDetails extensions.

**Infrastructure.** `SubscriptionPlanConfiguration` with the three seeded tiers; one migration with a
**hand-written `TermStartsAt` backfill**.

**Angular.** The Subscription screen reworked — amount, IRD Verified and term-start rows, two usage
meters, a plan picker with the tier's tick-list, and an Advanced panel for a negotiated price and
add-ons; `app-subscription-notice` in the shell header; `SubscriptionStore`.

---

## Step 4 — Manual E2E (fresh Organization, seeded by curl, every status code printed)

A fresh organization per phase, seeded entirely through the API: 5 account groups, 11 accounts, the
accounting defaults, warehouse, unit, category, a **Service** product (a Goods line consumes stock
regardless of `TrackInventory` — phase 30) and a customer. All 201/200.

| # | What | Result |
|---|---|---|
| A | `GET /subscription-plans` | 200 — Basic Rs 15,000 / 1,000 / 30,000; Standard Rs 20,000 / 5,000 / 50,000; Professional Rs 32,000 / 10,000 / 200,000, each with its tick-list |
| B | Fresh org is a trial | `planId=null planName=Trial amount=0 usage 0/0, 0/0` |
| C | `PUT /subscription` with Standard, then **re-read** | 200 / 200 — every field survives the round trip |
| D | Add-ons as overrides, then re-read | amount 23,000, quotas 80,000 / 6,000, `irdVerified=true` |
| E | `transactionQuota=2`, approve three invoices | 200, 200, **409** `quotaKind=Transactions used=2 limit=2` |
| F | Approve a **Quotation** at the ceiling | **200** — the unmetered set is right |
| G | `productQuota=1`, create a second product | **409** `quotaKind=Products used=1 limit=1` |
| H | Back to an unmetered trial term, approve again | 200 — and usage resets, because the term moved |
| N1 | `PUT /subscription` on a **nonexistent** org | **403** naming `Tenancy.Subscription.Manage` — not 404, so the gate fired before the handler |
| N2 | A real, accepted **Member** on the real org | **200** on `GET /subscription` and `GET /subscription-plans`; **403** naming `Tenancy.Subscription.Manage` on `PUT` |

N2 is the conclusive negative: the same user's 200 on the same organization proves membership, so the
403 is about the *key*, not about the tenancy. No custom role was needed — Member is seeded
`SubscriptionManage = false`.

**Browser pass** (the phase-31 bar's other half: *name the screen that calls the command*).
Configurations > Subscription & Features, driven live: the plan picker listed all three real tiers
with their prices; selecting Standard rendered its description, ceilings and tick-list (with
`visually-hidden` "Included:"/"Not included:" text, so the ✓/✗ is not colour alone); **Save** changed
the heading to "Standard plan", the amount to Rs 20,000.00 per term, and both meters from
"no limit on this plan" to `0 of 50,000` and `1 of 5,000`.

---

## Bugs and traps hit

1. **The stale shell banner — found only by driving it.** After recording a Standard plan, the shell
   still read *"366 days remaining in your trial"* on that very page. Two components read one fact
   and only one refreshed. This is phase 34b's rule one surface over: *anything global a screen both
   shows and changes must reload when it changes, from the first version.* Fixed with
   `SubscriptionStore` — the banner reads a shared signal, and the screen **saves through the store**,
   which folds the response straight in (the save returns the same DTO the read does, by construction
   since phase 31 extracted `ToDto` for exactly that, so nothing re-fetches). Pinned by
   `re-renders when a term is recorded through the store`. **No unit test would have found this**;
   both components were individually correct.
2. **The migration scaffolded phase 31's `DueDate` trap, verbatim.** `TermStartsAt datetimeoffset NOT
   NULL DEFAULT '0001-01-01'` — back-dating every existing tenant's current term to the year 1 and
   leaving a stray default constraint. Rewritten by hand as add-nullable → `UPDATE ... SET
   TermStartsAt = TrialStartsAt` → alter-to-NOT-NULL. Phase 37's refinement decided the other five
   columns: **a default is safe exactly when it is the truth about the rows already there**, and for
   those it is — every pre-existing tenant is on no plan, charged nothing, unmetered on both axes and
   not IRD-verified. So five keep their scaffolded defaults and one did not.
3. **A locked build from a stale `ErpApp.Api` dev server.** `MSB3027`/`MSB3021` on every
   `ErpApp.*.dll` copy into `src/Api/bin`. `git status` was checked first to rule out a concurrent
   session editing the tree (it showed only this phase's own files) before the process was stopped.
4. **`sqlcmd -S localhost` against a named instance returns nothing, silently.** The verification-code
   read came back empty, `POST /auth/verify-email` 400'd, and the failure surfaced three steps later
   as a 401 on accept-invitation. The instance is `DESKTOP-H0R00ME\SQLEXPRESS`. Same family as every
   other "the seed script hid its own failure" trap — the fix was printing every status code, which
   is what made the 400 visible at all.
5. **E2E step ordering: `accept-invitation` is authenticated.** Registering does not sign you in, so
   the invited member must `POST /auth/login` *before* accepting. Accepting first returns 401 and
   leaves the membership `Invited` — after which the Member's 403s look like key denials but are
   really "not a member", which would have made the negative proof vacuous. Caught because the
   subsequent `GET /subscription` was also 403 where it should have been 200: **the positive half of
   a negative test is what proves the negative half means anything.**
6. **A `document.querySelector('button.btn-primary')` in the browser pass hit the date-range picker's
   Apply button**, which precedes the form in the DOM. Nothing failed — the save simply never
   happened, and the page looked unchanged. Use the accessibility-tree `ref`, not a class selector.
7. **"The Standard plan allows 1 products".** Caught in the E2E output, not by a test. Pluralisation
   now follows the quota and the verb follows the count.

---

## Known limitations and follow-ups

1. **The ceiling is self-liftable** (Decision G) — the headline carried item. `SubscriptionManage`
   sits on tenant Admin because there is no other actor. Re-entry: a vendor-side console.
2. **`TrialStartsAt` / `TrialEndsAt` are now misnamed** — they describe any term, trial or paid. A
   rename crosses the Domain, the DTO, the Angular model and every consumer; deliberately deferred.
3. **The usage count was never measured against phase 34c's 50k dataset.** At the Professional
   ceiling it scans up to 200k `GlJournalEntry` rows per approve. `(OrganizationId, PostedAt)` exists,
   but phase 34c's own lesson is that an index added for one path changes the plan for others —
   **re-measure before quoting a number.**
4. **Only two of the metered axes are enforced.** Billing locations, SMS credits and AI scans are
   sold by the unit and unmodelled (Decision J).
5. **A plan change does not reconcile the entitlement flags** (Decision F), by design. A tenant on
   Basic can still hold Manufacturing if it was created with it. Visible, never silently corrected.
6. **The quota race allows a small overshoot.** Two concurrent approves can both read *n−1* and both
   proceed. Acceptable for a commercial ceiling — phase 20e's do-exactly-once rigour is for external
   side effects, not for a soft allowance — but it is not a hard limit.
7. **Expired-tenant behaviour is still derived** (phase 31 #5), though better evidenced: the Terms
   price read-only access at 25% of the subscription fee, so read-only *is* the natural end state and
   *is* something the vendor sells. No expired tenant was observable.
8. **The plan catalogue cannot be changed without a migration** (Decision A) — deliberate, and the
   honest consequence of having no vendor actor.

---

## Tests

| Suite | Before | After | Added |
|---|---|---|---|
| Domain.UnitTests | 640 | **660** | `SubscriptionPlanAndTermTests` — the catalogue's invariants, `SetPlan`'s five refusals, zero-as-sentinel, and that a plan change leaves every entitlement flag alone |
| Application.UnitTests | 1060 | **1081** | `SubscriptionQuotaTests` (13) — the sentinel, distinct-document counting, term windowing, tenant isolation, the `>=` boundary, product ceiling, renewal reopening the allowance, add-on overrides, the 404 on an unknown plan, and that the displayed and enforced figures are one number. `MeteredTransactionSweepGuardTests` (8) — both directions plus the GL-posting cross-check |
| Api.IntegrationTests | 29 | **29** | — |
| Angular | 426 | **440** | `subscription-notice.spec.ts` (14) — thresholds, the zero sentinel, warning precedence, the absent live-region role, and the store regression from bug #1 |

`dotnet build` / `dotnet test` / `ng build` / `ng test` all clean. `ng build` still warns the initial
bundle exceeds its 500 kB budget (pre-existing, 651 kB). All prior guards green: `a11y-sweep-guard`,
`sweep-guard`, `SearchSweepGuardTests`, 32b's location sweep guard, 35a's
`LocationReadPathSweepGuardTests`, 35b's two, 36's `product-picker-location-sweep-guard.spec.ts`,
34b's nav guard, 39's `RichTextWritePathSweepGuardTests` / `RichTextSharedCasesTests`.
