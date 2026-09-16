# Phase 49 status — The expiry decision, and phase 46's own surfaces

**Roadmap heading "49. The expiry read, and phase 46's own surfaces."**

## TL;DR

The phase existed to answer one question the 2026-09-16 live read had opened and could not close:
**what should this product do when a tenant's term ends?** The reference product's answer, read on a
trial, is that the organization *disappears* — `GET /api/v1/me/namespaces` returns `total: 0` and the
owner is shown first-run onboarding. This phase decided **not to match that**, and the reasoning is
Decision A. Then it closed the two of phase 46's three UI gaps that were still open.

- **The divergence is deliberate and now provable.** An expired organization stays in the owner's
  list, stays readable, and is **marked** — `OrganizationSummaryDto` carries `TermEndsAt` and
  `IsExpired`, and the picker renders *"Expired — read-only"*. The reason is not "ours is better": it
  is that the read is **one sample of trial behaviour and zero samples of paid behaviour** (phase
  41's rule), that retiring a lapsed *customer's* workspace is a different product decision from
  retiring a free trial's, and that this codebase's `AuthorizationBehavior` verifies membership on
  every single request — so "the membership disappears" would make an expired tenant unreadable,
  which contradicts the read-only promise the product already makes in its own 409.
- **The gap this closed was live on 17 organizations.** The dev database holds 139 organizations
  accumulated across 49 phases; 17 of them had already passed their trial end and were read-only with
  **nothing anywhere saying so** until you tried to write and got a 409. That is the defect the
  divergence obliged us to fix: the reference product's choice is brutal but unmissable, and ours was
  gentle and invisible. Diverging is only defensible if the state is legible.
- **`IsTrialActive` → `IsActive`.** Phase 43 renamed `TrialEndsAt` → `TermEndsAt` on the column and
  left the DTO flag that answers "has the term ended" still saying *trial*. The phase whose whole
  subject is expiry is the phase that should not ship a flag lying about which subscriptions it
  describes.
- **A term now ends at the end of its day in Kathmandu, not in UTC.** Putting the same date on a
  second screen exposed it: the Subscription form wrote `T23:59:59Z` and prefilled by
  `slice(0, 10)`, while `NepaliDatePipe` — correctly, since phase 48 — renders an instant's **Nepal**
  day, which for `23:59:59Z` is the *next* one. Two screens naming different days for one term is the
  defect; fixing only the read would have ratcheted the term forward a day on every save.
- **Phase 46's gaps: one built, one upheld, one already done.** SMS has a balance row on the
  Subscription screen (#7, built — read through the SMS module's own query so the
  `Crm.SmsCreditLedger.View` boundary stays where phase 18 put it). `LocationQuota` staleness (#6)
  stays declined, with phase 47's reason re-read and one new one. The AI-scan surface (#5) was built
  in phase 47 and was verified rather than re-scoped.

**Four things worth carrying forward.**

1. **"The reference product does X" is evidence about the reference product, never an answer about
   ours.** The kickoff said so and it was right to: the temptation on reading a clean, surprising
   behaviour is to implement it. What settled this was not taste but the *sample*: two trials are one
   sample of trial behaviour, and a vendor retiring a free trial's workspace while keeping a lapsed
   customer's data is entirely ordinary. Phase 41's rule (*a field dead on two free-trial tenants is
   one sample, not two*) generalises past fields to **behaviours**.
2. **A deliberate divergence owes a surface.** Deciding not to copy a behaviour is cheap; what is not
   cheap is that our alternative had never been shown to anyone. Seventeen live organizations were
   silently read-only. **Recording a divergence in a doc is half the work; the other half is making
   our own answer visible where theirs was.**
3. **Putting an existing value on a second screen is a date audit.** Nothing about `termEndsAt` had
   changed since phase 41. Rendering it in one more place, through the pipe phase 48 made
   instant-aware, is what made the UTC-vs-Nepal disagreement visible — and the round trip (read *and*
   write) had to move together, because fixing the read alone is a one-day-per-save ratchet no test
   would have caught.
4. **A permission boundary is a reason to leave a read where it is.** The obvious way to put SMS on
   the Subscription screen was to fold the balance into `TenantSubscriptionDto`. That DTO is read by
   *every* role, because the shell reads it to decide which nav entries to render; the SMS balance is
   `Crm.SmsCreditLedger.View`. Reading it through the SMS module's own query instead means a role
   without that key sees no row, decided by the server, and phase 18's split survives.

---

## Step 1 — the decision, and what it rests on

The evidence is `docs/erp-module-scan.md`, "An **expired** trial tenant, read against a live one
(2026-09-16)". It is one A/B on one endpoint with the control run: an expired trial's
`/me/namespaces` is `{"data":[], "meta_data":{"total":0,…}, "error":false}` and a live trial's is one
row. It also killed one inference on the spot (`payload.namespace = ""` looked like corroboration and
is carried by the live token too).

**No new live read was taken this phase.** Cadehi expires 2026-09-22, six days after this session, so
the confirming second sample of *trial* behaviour was not available; the two cheap extras named as
re-entry conditions (flipping Cadehi's Mode of Inventory Tracking to read Delivery Note / GRN, and
re-reading Product Batch / Product Serial No from an account holding those keys) both need the user
at a browser and are carried to phase 50's kickoff, where phase 51 needs the second one.

### Decision A — an expired tenant keeps its organization, and the divergence is recorded

**We do not remove the tenant from the owner's list.** Four reasons, in the order they carry weight:

1. **The sample.** Both tenants read were trials (`subscription_status: "Demo"`, `amount: 0`). That is
   one sample of trial behaviour and **zero** of paid behaviour. A vendor may reasonably retire a free
   trial's workspace and keep a lapsed customer's books; the two are different products sharing an
   expiry date field. Implementing the trial behaviour for *every* tenant would generalise a sample of
   one across the case it was never measured on.
2. **It contradicts a promise this product already makes.** `SubscriptionExpiryBehavior`'s own 409
   says *"Existing records can still be viewed, printed and exported"*, and phase 31 argued
   read-only-means-readable explicitly. The vendor's own Terms price read-only access at 25% of the
   subscription fee, which says the end state they sell is **readable**. A tenant that vanishes from
   the list cannot be read at all.
3. **It is architecturally much larger than a guard.** This codebase has no concept of a membership
   disappearing. `AuthorizationBehavior` verifies org membership on *every* request — it is the only
   mechanism doing so — so "the tenant is gone" would have to mean either a membership status nothing
   models or a filter in one query that every other request ignores. The first is a schema and
   pipeline change across the app; the second is a lie the picker tells while every deep link still
   works.
4. **It is a product decision with support consequences, not a technical one.** A customer whose data
   silently vanishes at term end raises a ticket; a customer looking at a *"Expired — read-only"*
   badge knows what happened and what to do. This codebase also models exactly one actor (phase 41
   Decision G): the tenant's own Admin holds `Tenancy.Subscription.Manage`, so the person who sees the
   badge is the person who can act on it.

**What is recorded as divergence**, so a later phase can revisit it deliberately: *on a trial, the
reference product removes an expired tenant from the owner's namespace list; this product keeps it,
marks it and leaves it readable.* The sentence is in `OrganizationSummaryDto`'s doc comment, in the
Angular model, in the template comment, and asserted in both directions by
`ExpiredTenantVisibilityTests` — because a divergence recorded only in a document is a divergence that
will be "fixed" by the next person who reads the scan.

### Decision B — the divergence is surfaced on the organization picker, not only in a doc

`OrganizationSummaryDto` gains `TermEndsAt` (nullable) and `IsExpired`, computed from the term end and
the clock rather than stored — `SubscriptionUsageReader`'s reason: a second writer of one fact is a
second chance to disagree with the behavior that enforces it. Both surfaces that render the list are
updated: the picker row gets the badge, and the dashboard's **Switch** `<select>` gets *"— expired"* in
its option text, because an `<option>` carries no badge and a switcher that dropped a user into a
read-only tenant unannounced would be the same defect in a smaller box.

**A tenant with no subscription row is reported live, not expired** — identical to the reading
`SubscriptionExpiryBehavior` has always taken of a missing row (a fixture, or a partially migrated
tenant). Kept the same on purpose: a picker that marked a tenant expired while every write succeeded
would be worse than saying nothing, which is why the unit test pins that case beside the other two.

The live term end is rendered too (*"Until 20-09-2026"*), which is what exposed Decision C.

### Decision C — a term ends at the end of its day on the Nepal wall clock

The Subscription screen wrote `${chosenDay}T23:59:59Z` and prefilled `termEndsAt.slice(0, 10)`. Both
halves agreed with each other and with nothing else: `NepaliDatePipe` takes an instant's **Nepal**
day (phase 48, correctly), and 23:59:59 UTC is 05:44 the next morning in Kathmandu. So the moment the
same value appeared on the picker, the two screens named different days for one term.

The write becomes `${chosenDay}T23:59:59+05:45` and the prefill becomes `instantToNepal(...).date`.
**Both halves had to move together**: fixing only the read would have pushed the term end forward one
day on every save, silently, on a screen whose Save button is routinely pressed without editing the
date. Phase 20e's rule — anything dated for a tenant uses the Nepal wall clock — applied to the one
tenant-level date that had escaped it.

*Existing rows are untouched.* The stored instants do not move, so nothing about enforcement changes;
their **displayed** day shifts by one, in the tenant's favour, and the first re-save writes the
Nepal-anchored instant. Proved in the E2E: a renewal for 2027-09-16 stores `2027-09-16T18:14:59Z`,
whose Nepal day is 2027-09-16.

### Decision D — `IsTrialActive` becomes `IsActive`

Phase 43 renamed the columns (`TrialStartsAt`/`TrialEndsAt` → `OriginatedAt`/`TermEndsAt`) for a
stated reason: *a column whose name says trial is a standing invitation to reason about it as one.*
It stopped at the Domain. The DTO flag answering "is this term still running" — read by the shell
banner to decide whether to say *"subscription has ended"* — still said `IsTrialActive`, for every
tenant, including paid ones. Renamed across the DTO, the Angular model, three templates and five
specs. No behaviour change; the phase about expiry is the right one to pay this.

### Decision E — SMS gets a row on the Subscription screen, read through its own query (46 #7)

Phase 46's reason for omitting it was right and is preserved in the wording: SMS is **a balance, not
an allowance against this term** — bought and spent down through the ledger phase 18 built, which is
why it is a line of text and not a meter. What was wrong was leaving a reader who compares this
screen against the vendor's price list to conclude SMS is not metered at all.

**Read through `ListSmsCreditLedgerQuery` (page size 1), not folded into `TenantSubscriptionDto`.**
`Tenancy.Subscription.View` is held by *every* role — deliberately, because the shell reads that query
to decide which feature-gated nav entries to render — while the balance is `Crm.SmsCreditLedger.View`.
Folding it in would have widened one key by the other quietly. Keeping the read separate makes the
refusal the server's: a role without the key gets a 403 the screen swallows, and renders **no row**,
which is phase 47 Decision H #1's rule (a counter that says nothing is worse than no counter) applied
to a permission instead of a loading state. The balance itself is computed server-side over the whole
ledger, never summed over a page — phase 16c's footer-total rule, which phase 18 had already obeyed.

### Decision F — `LocationQuota` staleness stays declined (46 #6), and phase 47's reason is re-read

Phase 47 Decision H #2 declined it: *"A prompt to reconcile a number against which nothing is
enforced would be an alert about a discrepancy with no consequence, which is how a banner teaches
people to ignore banners."* Re-read and upheld, with one thing added that phase 47 did not say:
**the prompt already exists, in the only place that can act on it.** When the counts disagree the
Subscription screen's own copy reads *"the purchased count below is probably out of date"* —
immediately above the Advanced input that corrects it. What 46 #6 asks for is that message somewhere
*else*, and everywhere else is a place where nothing can be done about it.

Re-entry unchanged: a vendor-side actor, at which point the count means something and staleness costs
something.

### Decision G — the AI-scan allowance surface (46 #5) was already built, and was checked not re-scoped

Phase 47 Decision H #1 built it: the Document inbox reads the shared `SubscriptionStore` and says
*"N of 20 AI scans used today — M left"*, rendering nothing when `quota == 0`. Verified present before
scoping anything, per the kickoff. Nothing to do — recorded so the item is closed rather than
carried a third time.

### Decision H — the `inactive` / `inactive_by_id` / `inactive_at` triple is not modelled

The live namespace row carries that triple, and the natural reading is that expiry sets it and the
list filters on it. It is **consistent with the schema and not observed** — the expired row cannot be
seen at all, which is the whole point. Not modelled, not inferred, and named here so it is not
quietly promoted to a mechanism by a later reader. (It would also model the thing Decision A declined
to do.)

### Decision I — permission keys: none added

`Tenancy.Subscription.View` / `.Manage` and `Crm.SmsCreditLedger.View` cover everything.
`MyOrganizationsQuery` is cross-organization and has never carried a key — it *is* the membership
list, so gating it on a per-organization key would be circular. No new `PermissionKeys` constants, so
none of phase 9's hand-written-seed-migration trap applies. **No migration in this phase at all.**

---

## What was built

**Application.** `OrganizationSummaryDto` gains `TermEndsAt` / `IsExpired`;
`MyOrganizationsQueryHandler` takes a `TimeProvider` and projects the term end through a correlated
subquery, deciding "has it ended" in memory beside the sentence `SubscriptionExpiryBehavior`
enforces; `TenantSubscriptionDto.IsTrialActive` → `IsActive`.

**Api.** Nothing — `/mine` returns the Application DTO directly, and the subscription endpoints were
already pass-through.

**Angular.** `OrganizationSummary` gains the two fields; the picker row renders the expiry badge or
the term end through `NepaliDatePipe`; the dashboard switcher marks expired options; the Subscription
screen gains the SMS balance row and the Nepal-anchored term-end round trip; `isTrialActive` renamed
throughout.

**Tests.** `ExpiredTenantVisibilityTests` (3, new), `MyOrganizationsQueryHandlerTests` (+2
assertions), `organization-list-page.spec.ts` (3, new), `subscription-features-page.spec.ts` (+4).

---

## Step 2 — Manual E2E (fresh Organization, seeded through the API, every status code printed)

Against the running API on `https://localhost:7104`, driven by a Python `urllib` cookie jar, against
a **fresh organization** (`Phase49 Expiry ad86eb6c`). Every assertion passed; the script is in this
session's scratchpad and its ten steps are:

| Step | What it proves | Result |
|---|---|---|
| 1–2 | A fresh organization is listed live, carrying its term end | `201`, then `200` with `isExpired: false`, `termEndsAt: 2026-10-01T06:14:05Z` |
| 3 | Master data writes while the term runs | `201` on `POST /contacts` |
| 4 | Expired **through the database**, never through the aggregate | `TermEndsAt = 2026-09-01T06:14:05Z` |
| 5 | **The divergence** — still listed, and marked | `200`, row present, `isExpired: true` |
| 6 | Read-only means readable | `200` subscription (`isActive: false`), `200` contact list |
| 7 | ...and the gated writes are refused | `409` naming the end date |
| 8 | The renewal is the one write that still works | `200`, `isActive: true`; the list says live again |
| 9 | The SMS balance the Subscription screen reads | `200`, `balance: 0` |
| 10 | A Member: `200` beside a `403` **naming the key**, same user, same run | `200` on GET subscription; `403` *"You do not have permission to perform this action (Tenancy.Subscription.Manage)."* |

**Reaching the expired state.** `TenantSubscription.SetPlan` refuses an end date at or before the
start and `CreateTrial` starts now, so an expired subscription cannot be constructed through the
aggregate's own API — only the passage of time produces one. Phase 31's rule: never weaken a Domain
invariant so a test can reach a state only time produces. The unit tests reach through EF's change
tracker; the E2E does the same thing one layer down, with `sqlcmd`.

**Why step 10's 403 is a real proof.** The Member PUTs a **nonexistent** `planId`. The handler 404s on
an unknown plan (`"That subscription plan does not exist."`), so a `403` can only have come from
`AuthorizationBehavior` before the handler ran — the 403-not-404 idiom, on a command that has no
document id of its own. The `200` on `GET .../subscription` from the same user in the same run is the
other half: the Member genuinely holds `Tenancy.Subscription.View`.

## Step 3 — Browser pass

Dev cert exported, `erp-web-ssl` profile, curl's `erp_auth` cookie transplanted via `document.cookie`
(phase 25's recipe). Against the real app:

- The picker renders **`Expired — read-only`** on the phase's organization and `Until DD-MM-YYYY` on
  live ones. Contrast of the badge measured in the browser: **10.22:1** (`#58151c` on `#f8d7da`), well
  past 4.5:1.
- **17 of the 139 organizations** in the dev database were already expired. Every one of them had been
  read-only, invisibly, for as long as it had been expired.
- The Subscription screen renders the shell's *"This organization's subscription has ended…"* banner,
  the **SMS credits — 0 remaining** row with its balance wording and link, and an **Ends on** box
  showing `2026-09-13`, the Nepal day of the stored `2026-09-13T06:15:07Z`.
- **Decision C proved end to end, in the browser, on the real write path.** *Ends on* set to
  `2027-09-16` and **Save** pressed (at the user's request): the API stored `2027-09-16T18:14:59Z`,
  whose Nepal day is `2027-09-16` — the day that was picked. Before this phase the same click would
  have stored `2027-09-16T23:59:59Z` and read back as the 17th. The rest of the write is what the
  panel promises and nothing more: `Basic`, Rs 15,000.00, 1,000 products, 30,000 transactions, 20
  scans, entitlement flags untouched.
- **And the loop closes**: the shell banner disappeared, the status badge went to **Active**, and the
  picker row flipped from *Expired — read-only* to *Until 16-09-2027*. That is also the clearest
  demonstration of phase 41 Decision G, which limitation #4 records: the Admin who saw the badge is
  the Admin who lifted it, with one `PUT` and no second party anywhere in the transaction.

---

## Bugs and traps hit

1. **A UTC end-of-day is the next Nepal day** — Decision C. Not a bug introduced this phase; a bug
   *exposed* by rendering an existing value through a second, correct, renderer. Had the badge been
   written with `slice(0, 10)` to match the form, the two screens would have agreed and both been
   wrong, and phase 23's guard does not look at output.
2. **A spec that injects a new service fails in every existing case, not the new one.** Adding
   `CrmService` to `SubscriptionFeaturesPage` breaks all nine existing tests at once, because the
   TestBed has no provider for it — the failure names dependency injection, not the feature. The
   provider belongs in the shared `render()` helper with its refusal case parameterised, which is what
   made "no row when the read is refused" a one-line test rather than a second harness.
3. **Python on Windows cannot import from an MSYS-style path.** `sys.path.insert(0, "/c/Users/...")`
   silently finds nothing and reports `ModuleNotFoundError`; the Windows spelling works. Costs one
   round trip each time.

---

## Known limitations and follow-ups

1. **Zero samples of paid expiry behaviour remain** (Decision A). Cadehi expires 2026-09-22 and would
   give a confirming second sample of *trial* behaviour, which is worth having and changes nothing on
   its own. Re-entry for the question that matters: a reference tenant on a **paid** term that lapses,
   which this project has never had access to.
2. **The two cheap live extras were not taken** — the Mode of Inventory Tracking flip (Delivery Note /
   GRN screens) and re-reading Product Batch / Product Serial No from an account holding their
   permission keys. Both need the user at a browser; the second is carried into phase 50's kickoff
   because **phase 51 needs those columns**.
3. **`LocationQuota` staleness stays open by decision** (Decision F), re-entry unchanged: a
   vendor-side actor.
4. **Every ceiling is still self-liftable** (phase 41 Decision G, phase 46 #1), and this phase
   demonstrated it from the other side: the same Admin who sees *"Expired — read-only"* can lift it
   with one `PUT`. Re-entry: a vendor-side actor.
5. **`SubscriptionExpiryBehavior`'s 409 renders its date as `yyyy-MM-dd`**, not through
   `RequestCalendar`. Left alone deliberately: `LockDateBehavior`'s message does the same, and the two
   are siblings — changing one at the end of a phase would make the pair inconsistent. Re-entry: a
   sweep over behavior messages, which is a phase's worth of decision about whether an error string is
   "server-rendered output" in phase 27b's sense.
6. **The quota race is unchanged** (phase 46 Decision I, phase 41 #6).

---

## Tests

| Suite | Before | After | Added |
|---|---|---|---|
| Domain.UnitTests | 674 | **674** | — (no Domain change; the aggregate was not touched) |
| Application.UnitTests | 1185 | **1188** | `ExpiredTenantVisibilityTests` (3) — the expired tenant still listed and marked *beside* the behavior refusing the same tenant's write, the live case in both halves, and read-only-means-readable through the gate itself; plus the no-subscription-row case pinned in `MyOrganizationsQueryHandlerTests` |
| Api.IntegrationTests | 30 | **30** | — |
| Angular | 554 | **561** | `organization-list-page.spec.ts` (3) — expired listed and marked *and still a link*, a live row's term end, and an organization with no subscription row. `subscription-features-page.spec.ts` (+4) — the SMS balance shown as a balance, no row when the ledger read is refused, and the Nepal-day term-end round trip in both directions |

`dotnet build` / `dotnet test` / `ng build` / `ng test` all clean. `ng build` does not warn; initial
bundle **643.39 kB** against phase 42's 680 kB budget — unchanged, because `NepaliDatePipe` was
already in the initial chunk. All prior guards green.
