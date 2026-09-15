# Phase 46 status — Metered add-on axes, and the three axes that turned out to be three different things

**Roadmap heading "46. Metered add-on axes and the subscription edges."**

## TL;DR

The roadmap said the three remaining axes were one shape — *"each new axis is a reader plus a ceiling
on the plan row"*. **Reading the vendor's published price list and the live tenant showed they are
three different kinds of thing, and only one of them is that shape.**

- **AI scans** are a published ceiling nobody told this project about: *"Scan with AI — Up to 20 scans
  per day"*, stated identically on all three tiers and on all three term-length tabs, with **no
  add-on** selling more. A per-**day** rate limit, not a per-term commercial lever. Built.
- **Billing locations** are sold at Rs 5,000 each — and **the reference product caps nothing**. Cadehi
  runs three locations on a plain *Enabled* flag, with an unbounded list, an Add New Location dialog
  naming no cap and no charge, and no location row on the Subscriptions screen at all. So the
  purchased count is **a record, not a ceiling**. Phase 43's product-to-location precedent, reached
  the same way.
- **SMS credits** were already metered, by the right mechanism, since phase 18. The live *Add SMS
  Credit* control is still a static *"Require more SMS credits? Contact us at 9801831190 or email
  support@tiggapp.com."* A ledger you spend down is what a purchased balance is; a quota on the plan
  row would have modelled something the vendor does not sell. Nothing built, and that is the finding.

The phase also fixed a **defect in phase 41's own model** that the same read exposed: every published
quota is per *year*, the 3-year tab states the identical figures beside a tripled price, and phase 41
counted transactions over the whole term. A tenant on a three-year term was being sold 50,000 a year
and given 50,000 for the three.

**Six things worth carrying forward.**

1. **The cheapest experiment was the same one as last time, and it had more in it than phase 41
   took.** The "AI scans / day: 20" row was already sitting in `erp-module-scan.md`'s own price-list
   appendix, recorded by phase 41 and then dropped from phase 41's summary table — which is why the
   roadmap wrote AI scans as an unpriced invention. **A fact recorded in the scan but not carried into
   the decision is not yet evidence.** Re-reading the page cost one page load.
2. **"A reader plus a ceiling" is a shape, and a shape is not a finding.** The roadmap's framing was
   a reasonable generalisation from the two axes that existed. Three axes later, one is a rate limit,
   one is a record, and one was already done by a different mechanism. Phase 30's *"a list sampled
   from a few screens becomes a wrong list — find the rule"*, applied to mechanisms instead of screens.
3. **A dated count must come from the append-only history, even when a perfectly good timestamp
   exists on the aggregate.** `UploadedDocument.ExtractionAttemptedAt` is overwritten by every
   re-scan, so it answers "when was this last scanned", never "how many scans today" — ten re-runs of
   one bill would have counted as one, and those ten are ten real paid calls. The count reads the
   `Audit` rows phase 22 already writes. Phase 26c's `StockMovement`-not-`StockLedgerEntry` rule, in
   a place that has nothing to do with stock.
4. **Zero-means-unmetered is right for an allowance somebody bought and exactly wrong for a cost
   control.** Phase 41's sentinel makes a trial usable. On the scan axis it would mean a free trial
   with uncapped spend on a paid API, so `CreateTrial` seeds the published 20 and the migration
   backfills every existing tenant to 20 rather than to the scaffolded 0 — which would have been the
   feature applying to nobody, silently, with the API bill as the only evidence.
5. **"Takes the dependency" is not "spends the allowance".** The first draft of the sweep guard
   looked for handlers injecting `IDocumentExtractor` and found three; two of them read
   `IsConfigured`/`ModelId` to render a settings panel and make no vendor call. Named as exclusions
   with reasons, plus a test asserting each named exclusion still exists.
6. **Two lists that describe the same thing in two vocabularies cannot be joined by name.** The plan
   tick-list speaks the price list ("Multiple currency", "POS (Retail/Restro)"); a tenant's features
   speak the signup wizard ("Multi-Currency Support", "Point of Sale (Retail)"). They are not even
   the same length. The first draft compared them in the browser by display name, which matches
   nothing and renders as *"no mismatches"* — phase 27a's ordinal enum bridge through a different
   door, failing the same way: quietly, and looking like agreement. The comparison moved to the
   server, where both sides are typed.

Tests: Domain **674 (+8)**, Application.UnitTests **1181 (+25)**, Api.IntegrationTests **29 (+0)**,
Angular **487 (+8)**. `dotnet build` / `dotnet test` / `ng build` / `ng test` all clean; `ng build`
does not warn, initial bundle **652.08 kB** against phase 42's 680 kB budget.

---

## Step 1 — the live read, and what it overturned

### The vendor's price list, re-read 2026-09-15 (tiggapp.com/pricing)

Verbatim on **all three tiers**: *"Scan with AI - Up to 20 scans per day."* The 3-year tab shows
Rs 35,000 / 50,000 / 80,000 against the 1-year tab's Rs 15,000 / 20,000 / 32,000 — and **every quota
line is byte-identical**: "Up to 5,000 products", "Up to 50,000 transactions / year", "Up to 20 scans
per day". The Lifetime tab likewise.

The add-on collection is unchanged: IRD Billing Rs 15,000 one-time; Production Rs 5,000/yr; Billing
Location Rs 5,000 per location per year; Products Rs 1,000 per additional 1,000; Transactions
Rs 1,000 per additional 10,000; SMS Rs 0.99 per credit. **There is no AI line.**

The Tigg AI page defines the unit: one uploaded invoice or bill, scanned and extracted — which is
exactly one `ExtractInboxDocumentCommand`.

### The tenant read (Cadehi Enterprises, `cadehi.tigg.app`, 2026-09-15)

Only Cadehi was available on the account; Moonbeam is on a different login. It was read after the
user signed in. Nothing was saved — one modal was opened and dismissed.

- **`#/config/tigg-subscription`** — a banner (*"currently active and will expire in 7 days"*) over
  seven read-only rows: Subscription Plan `Standard ( 0 Txn, 0 Products)`, Subscription Amount
  `0.00`, Expiry Date `22-09-2026`, Location Enabled `Yes`, Warehouse Enabled `Yes`, IRD Verified
  `No`, IRD Sync Enabled `No`. **No controls. No SMS row, no AI-scan row, no location count.** The
  banner's action is **CONTACT US**.
- **CRM > SMS > Overview** — `0` *Total Available Credits*, `0` *Total Credit Used*, and an **Add SMS
  Credit** link whose whole behaviour is to reveal *"Require more SMS credits? Contact us at
  9801831190 or email support@tiggapp.com."* Exactly what phase 18 recorded in 2026, still true.
- **Organization > Features** — *Billing Location: **Enabled***, `+ ADD NEW LOCATION`, and three rows
  (HO HeadOffice, 1002 POS Restaurant, 1003 POS Retail). **No count, no cap, no price anywhere.** The
  Add New Location dialog asks for Code, Name, Address and Warehouse and says nothing about a charge
  or a remaining allowance.
- **Workflow > Document** — the inbox, empty, with **no scan-allowance counter of any kind**.

**So the answer to the kickoff's question is: none of the three axes is metered in the product, and
nothing on the Subscriptions screen is editable by anyone.** Phase 41's Decision G characterisation —
*an accurate record of what was sold and an honest guard against drifting past it unnoticed, not a
control that survives an adversary* — now holds for all three axes on live evidence rather than by
inference.

### What that overturned

The phase was planned with a `LocationQuota` that **refuses** location N+1. The read killed it. Put
back to the user with the evidence, and the decision was **record the count, do not refuse** — which
is what shipped. This is phase 43's product-to-location moment repeated: an enforcement that seemed
obviously right, retired on the evidence rather than built.

---

## Scope decisions

### Decision A — AI scans are a rate limit, and the only new ceiling

`TenantSubscription.DailyAiScanQuota`, `SubscriptionPlan.DailyAiScanQuota` (seeded 20 on all three
tiers from one constant), `IMeteredAiScan`, and a third branch in `SubscriptionQuotaBehavior`.

*Why enforce a ceiling nobody sells.* The other two ceilings represent a purchase; this one
represents a **cost**. Extraction is the only action in this product that spends money outward per
call (phase 22's `IDocumentExtractor`, on the order of 1.5–2.5k tokens per page). A ceiling is worth
having even with no commercial lever behind it, and the exception message says so rather than
pointing at a larger plan that grants exactly the same 20.

*Per Nepal-local day.* `NepalTime.StartOfLocalDay` is new. UTC midnight is 05:45 in Kathmandu, so a
UTC-keyed window would hand every tenant a second allowance each morning and refuse them in the
evening against a day already over. Tested with a 23:30/00:30 local pair that a UTC window gets
wrong, per phase 20e's rule.

### Decision B — an *attempt* spends the allowance, not a success

Phase 22 made extraction failure an **outcome**, not an error: a timeout, a 429, garbage or a missing
credential all return 200 with the document's status set. `AuditBehavior` writes its row on handler
success, so all of those spend a unit.

That is deliberate. Counting only successes would let a tenant retry a failing document without
limit — and a retry is a real paid call, so the failing case is precisely the expensive one. "Up to
20 scans per day" reads as attempts, and attempts are what cost money.

### Decision C — the count reads the audit trail, not the document

See carried item #3 above. `UploadedDocument.ExtractionAttemptedAt` is updated in place. The
append-only source already exists because phase 22 audits every extraction — it being the one action
that sends a customer's document outward. `AuditBehavior` writes **after** the handler, so the count
a request sees is exactly "attempts before this one today", which is the figure a ceiling compares
against. Proved live: three documents uploaded, two extraction audit rows, one document still
`NotAttempted`.

### Decision D — `LocationQuota` is a record, never a ceiling

Stored, surfaced, and never compared in order to refuse. `CreateBillingLocationCommandHandler` keeps
phase 32's boolean cap-at-one as the only refusal, unchanged.

*Why store it at all.* Without it nothing says a tenant running three locations bought three, and the
commercial record — which is what this whole area is — is incomplete. The screen shows "3 in use,
1 recorded as purchased" and says in as many words that nothing is blocked.

`There_is_no_refusal_kind_for_locations_or_sms` pins the absence: `SubscriptionQuotaKind` has exactly
three members, so adding a location refusal fails a test that explains why it should not exist.

### Decision E — SMS is already metered, and nothing was built

The vendor sells SMS by the credit, not by a ceiling; the product has no purchase flow, only a phone
number; and this codebase has had the right mechanism since phase 18 — `SmsCreditLedgerEntry`,
append-only, balance = `SUM(ChangeAmount)`, with `SendSmsCommandHandler` refusing below zero and
`AdjustSmsCreditCommand` (Admin-only) as the "the vendor topped us up" entry point.

A plan-row quota on top would have been a second mechanism for one rule, and the one that breaks
first when the two disagree. Recorded as a decision with evidence rather than as an omission.

### Decision F — the transaction window is the allowance **year**

`SubscriptionUsageReader.CurrentAllowanceYear`: the anniversary year containing "now", running from
`TermStartsAt` and clamped to `TermEndsAt`. Anniversaries come from `AddYears`, so a 29 February term
lands on the 28th in common years rather than drifting.

A one-year term is one allowance year, so nothing changes for the common case — which is what makes
it safe to apply to every existing tenant without a data migration. Proved live: a term ending
2029-09-15 reports an allowance year of 2026-09-15 .. 2027-09-15.

### Decision G — the expiry gate covers master-data **creation**, not every write

Phase 31 carried item #6, narrowed rather than taken whole. `IExpirySensitiveMasterData` — a third
marker, not a merge (phase 32b's rule, third time) — on the twelve Create/Update commands for
Product, Contact, Account, AccountGroup, Warehouse and BillingLocation.

*What phase 31 argued, and what it actually allowed.* Its reason for excluding configuration writes
was that an expired tenant should not be locked out of **fixing** a wrong account before renewing.
Good reason, still honoured: settings edits, every query and `SetTenantSubscriptionCommand` all still
work. But none of those six aggregates is a document, so none carried a lock-date marker, and the
same gap let an expired tenant build an entire chart of accounts and product catalogue indefinitely
and for free. *Fixing something* and *setting up a new business* were one permission and only the
first was ever argued for.

*Still derived, not observed.* No expired tenant has ever been visible; Cadehi had 7 days left. The
Terms price read-only access at 25% of the subscription fee, which says the end state is read-only
and that it is a product, but says nothing about which writes stop. Recorded as an inference.

### Decision H — the entitlement mismatch is surfaced, and compared on the server

Phase 41 carried item #5. `TenantSubscription.SetPlan` still does not touch the entitlement flags and
still should not (phase 31's reason: flipping `TrackInventoryEnabled` off under a tenant with stock
already in a FIFO ledger). So a mismatch is a legitimate state that nothing anywhere named.

`EntitlementMismatchDto`, computed in `GetTenantSubscriptionQueryHandler` from a typed pairing of six
comparable entitlements. Four are deliberately absent, each for a stated reason: Landed Cost and
Developer API are published per tier with no tenant flag; Multiple Locations has a tenant flag but no
tier includes it (it is the add-on); and POS is one plan column against two tenant features.

See carried item #6 for why this is not in the browser. Proved live: a tenant created without
Multi-Currency, recorded on Standard, reports *"Part of this plan, but not switched on here —
Multi-Currency Support."*

### Decision I — the quota race stays documented

Unchanged from phase 41 #6 and the kickoff's recommendation. Two concurrent approves can both read
*n−1*. Phase 20e's claim-row-under-a-unique-index rigour is for external side effects with
do-exactly-once semantics; an overshoot of one on a commercial allowance is not a correctness
failure. Named here rather than fixed.

The scan axis is worth a sentence of its own: its overshoot is bounded by concurrency and each unit
is one API call, so the exposure is "a few extra calls", not "unbounded spend".

### Decision J — permission keys: none added

`Tenancy.Subscription.View` / `.Manage` cover everything, exactly as phase 41 left them. The scan
ceiling rides `Workflow.InboxDocument.Extract`, which already existed and is already Admin-only. No
new `PermissionKeys` constants, so none of phase 9's hand-written-seed-migration trap applies.

---

## What was built

**Domain.** `TenantSubscription.DailyAiScanQuota` / `.LocationQuota` / `.DefaultDailyAiScanQuota`;
`SubscriptionPlan.DailyAiScanQuota`; `SetPlan` widened by two; `NepalTime.StartOfLocalDay`.

**Application.** `IMeteredAiScan`; `IExpirySensitiveMasterData`; `SubscriptionQuotaKind.AiScans`;
`SubscriptionUsageReader.CurrentAllowanceYear` / `.CountAiScansTodayAsync` / `.CountLocationsAsync`,
with `CountTransactionsAsync` and `ReadAsync` now taking the clock; a third branch in
`SubscriptionQuotaBehavior` (which gained a `TimeProvider`); a third marker in
`SubscriptionExpiryBehavior`; `EntitlementMismatchDto` +
`GetTenantSubscriptionQueryHandler.CompareEntitlements`; twelve master-data commands marked.

**Api.** `SetTenantSubscriptionRequest` gains `DailyAiScanQuota` and `LocationQuota` — restated on
the Api's own record, phase-27b's trap.

**Infrastructure.** One migration, with the `DailyAiScanQuota` backfill hand-written (see Bugs #1).

**Angular.** The AI-scan meter, the billing-location record row, the entitlement-mismatch panel, two
Advanced inputs, the transaction label corrected to "this subscription year", and the allowance-year
window rendered.

---

## Step 4 — Manual E2E (fresh Organization, seeded through the API, every status code printed)

**19 / 19.** Organization `692c3df0-545b-4a70-882b-0d2275e1b4c1`.

| # | What | Result |
|---|---|---|
| A | `GET /subscription-plans` | 200 — Standard `dailyAiScanQuota=20`, the published figure |
| A | `PUT /subscription` with `dailyAiScanQuota=2`, `locationQuota=1` | 200, round-trips |
| B | Upload three PNGs, extract | 200, 200, **409** `quotaKind=AiScans used=2 limit=2` |
| C | Create billing locations 2 and 3 against a purchased 1 | **201, 201** — a record refuses nothing |
| C | Re-read | `locationsUsed=3 locationQuota=1`, mismatch `Multi-Currency Support` |
| D | Record a **three-year** term, re-read | allowance year `2026-09-15 .. 2027-09-15` — one year wide |
| N1 | `PUT /subscription` on a **nonexistent** org | **403** naming `Tenancy.Subscription.Manage`, not 404 |
| N2 | `GET /subscription` on the **real** org, same user, same run | **200** — which is what makes N1 mean anything |

**`sqlcmd` predicates** (`DESKTOP-H0R00ME\SQLEXPRESS`):

| Predicate | Value | False under |
|---|---|---|
| `DailyAiScanQuota=2, LocationQuota=1` on the org | ✓ | either column not being written |
| uploaded documents | **3** | — |
| `DocumentExtraction` audit rows | **2** | the gate running *after* the handler (would be 3) |
| documents still `NotAttempted` | **1** | the same |
| billing locations | **3** | any refusal on the location axis |

And for the migration, before any of that: **156 / 156** existing `TenantSubscriptions` at
`DailyAiScanQuota = 20`, **0** at `0` — a predicate that reads exactly backwards under the scaffolded
migration.

**Browser pass**, driven live on the same organization at
`/organizations/{id}/features`: the AI-scan meter reading *"2 of 2"* with its own no-add-on copy; the
billing-location row reading *"3 in use, 1 recorded as purchased"* above *"Nothing is blocked"*; the
mismatch panel naming Multi-Currency Support; the transaction meter labelled *"Transactions this
subscription year"* over *"Counted from 2026-09-15 to 2027-09-15"*; and both new Advanced inputs
present, each with a real `<label>` bound to its control.

---

## Bugs and traps hit

1. **The migration scaffolded the wrong default, for the third phase running, with a new symptom.**
   `dotnet ef` emitted `DailyAiScanQuota int NOT NULL DEFAULT 0` on `TenantSubscriptions`. Zero on
   that column is the **not-metered sentinel**, so every existing tenant would have come out with an
   *unlimited* daily allowance and the phase's whole point would have applied to nobody. Phase 31's
   `DueDate` and phase 41's `TermStartsAt` were the same trap with a visibly wrong value; this one
   scaffolds a *plausible* value that means the opposite of what is wanted. Rewritten by hand as
   add-nullable → `UPDATE … SET 20` → alter-to-NOT-NULL. `LocationQuota`'s scaffolded 0 was kept,
   because phase 37's refinement applies: a default is safe exactly when it is already the truth
   about the rows that are there, and no existing tenant has recorded a purchased location.
2. **The mismatch comparison was written in the browser and would have matched nothing.** Caught
   before it shipped, by reading what the two lists actually contain rather than by running it —
   which is the only way it *would* have been caught, since "no mismatches" is also the correct
   output most of the time. Moved server-side. Carried item #6.
3. **The sweep guard's first predicate was too broad.** "Handlers taking `IDocumentExtractor`" found
   three, two of which only read `IsConfigured`/`ModelId` for the settings panel. Named as
   exclusions with reasons, plus `Every_unmetered_extractor_consumer_still_exists` so a rename cannot
   leave a stale excuse behind.
4. **`SubscriptionPlanDto` never carried the new column.** The Domain had it, the seed had it, the
   Angular model had it, and the API returned a plan without it — the E2E died on `KeyError:
   'dailyAiScanQuota'` at step A. Phase 32's *"a list query returning the aggregate exposes a new
   field for free, but a DTO drops it silently"*, and only the E2E saw it.
5. **The transaction meter's label was left saying "this term"** after the window stopped being the
   term, and the two allowance-year dates were populated and rendered nowhere — phase 23 bug #1 in
   both directions at once. Found in the browser pass, not by any test.
6. **A stale `ErpApp.Api` locked the build twice** (`MSB3027`, phase 41 bug #3). `git status` was
   checked first each time to rule out a concurrent session before stopping the process.
7. **Node was on v16 and `ng build` died with `availableParallelism is not a function`.** Switched
   with `nvm use 24.11.0` — not a PATH override — and the junction was verified afterwards, since
   `nvm use` from a shell that cannot create the symlink deletes it and reports success.
8. **The E2E's inbox routes were guessed and 404'd.** They are
   `/organizations/{id}/workflow/inbox-documents/` and `/organizations/{id}/ai-document-extraction`.
   Visible immediately only because every status code is printed.

---

## Known limitations and follow-ups

1. **Every ceiling is still self-liftable** (phase 41 Decision G), now confirmed live on all three
   axes: the reference product shows no quantity a tenant could edit, and in this codebase
   `Tenancy.Subscription.Manage` still sits on the tenant's own Admin. Re-entry: a vendor-side actor.
2. **Expired-tenant behaviour is still derived** (Decision G). No expired tenant has ever been
   observable. Cadehi's trial ends 2026-09-22 — **the first genuinely observable expiry this project
   has had a date for.** Re-entry: read it after that date.
3. **The quota race is unchanged** (Decision I).
4. **A failed scan spends a unit** (Decision B). Defensible, and the opposite choice is defensible for
   the no-credential case specifically. Re-entry: a tenant complaining that an outage cost them a day.
5. **Nothing surfaces the scan allowance where scanning happens.** The meter is on the Subscription
   screen; the Document inbox shows no "3 of 20 today" anywhere, so the first a user knows of the
   ceiling is a 409. The reference product shows nothing either, which is why this was not built —
   but it is the obvious next improvement and it is ours to make, not theirs.
6. **`LocationQuota` is written only from the Advanced panel** and defaults to what is already
   recorded, so it can silently go stale as locations are added. The screen says so when the counts
   disagree; nothing prompts anyone to correct it.
7. **The SMS axis has no usage row on the Subscription screen**, though the ledger balance is on the
   SMS Overview tab. Deliberate — it is a balance, not an allowance against this term — but a reader
   comparing this screen to the price list will find SMS missing from it.

---

## Tests

| Suite | Before | After | Added |
|---|---|---|---|
| Domain.UnitTests | 666 | **674** | The scan ceiling's positive-quota rule, the published 20, a trial metered on scans but nothing else, the purchased-location count and its negative refusal, and zero-as-deliberate-exemption |
| Application.UnitTests | 1156 | **1181** | `SubscriptionMeteredAxisTests` (15) — the ceiling under and over, re-scanning one document, the Nepal-midnight turnover, tenant isolation, ordinary audits not counting, the allowance year across a 3-year term and a leap day, and locations reported-not-refused. `MeteredTransactionSweepGuardTests` (+4) — the scan marker both ways, the named metadata-only consumers, and the absent location/SMS refusal kinds. `ExpiredTenantMasterDataTests` (6) |
| Api.IntegrationTests | 29 | **29** | — |
| Angular | 479 | **487** | `subscription-features-page.spec.ts` (8) — the scan meter and its no-add-on copy, the zero sentinel, locations over-purchased and unrecorded, and the mismatch panel in both directions plus its absence |

`dotnet build` / `dotnet test` / `ng build` / `ng test` all clean. `ng build` does not warn; initial
bundle 652.08 kB against phase 42's 680 kB budget. All prior guards green.
