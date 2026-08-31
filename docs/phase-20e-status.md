# Phase 20e — Alert Scheduler (FR-11.1)

## TL;DR

This codebase's **first background-job infrastructure** ships: a hand-rolled
`AlertSchedulerHostedService` (Infrastructure) that ticks a `PeriodicTimer` and hands every tick to
`IAlertDispatcher` (Application), which decides what is due and sends it. `AlertDefinition`
(tenant-scoped, `ITenantLookupEntity`) plus an `AlertSendLog` ledger, an Angular **Alert Scheduler**
Configurations screen including the reference product's **Email Logs** view, and a new Admin-only
`Configuration.AlertDefinition.View`/`.Manage` + `Configuration.AlertSendLog.View` permission set.

**The new-authentication-bypass-surface the roadmap warned about was not built, because it turned
out not to be needed.** The dispatcher sends **no MediatR request at all**: it reads through
`IAlertContentBuilder` implementations that take an explicit `OrganizationId` and query
`IAppDbContext` directly. `CurrentUserService` still throws outside an HTTP context, exactly as it
did before this phase, and no system principal, ambient-user fallback, or "runs as this user" field
exists anywhere. See **Decision B** — this is the most load-bearing call in the phase.

Three sub-decisions carried by the design:
- **Decision A — hand-rolled `BackgroundService`, not Hangfire/Quartz/Coravel.** Everything a
  scheduler library sells (durable schedule state, catch-up, multi-instance locking) is already
  provided here by tenant data plus one unique index this phase needs regardless.
- **Decision C — at-most-once delivery.** The ledger row is committed *before* SMTP is called, under
  a unique index on `(AlertDefinitionId, OccurrenceDate, Recipient)`. That single ordering choice
  buys idempotency across restarts, multi-instance safety, and bounded catch-up all at once.
- **Nepal-local scheduling (UTC+05:45).** Live-confirmed: the reference product's time picker
  defaulted to 21:55 while UTC was 16:10. Occurrence dates are local dates too.

Confirm-live also produced a screen this phase would otherwise have missed entirely — **Email
Logs**, hidden behind the Alert Scheduler panel's own kebab menu — and ruled out both a "Run now"
action (the reference product has none) and `CustomTemplateType.Email` (no template picker exists on
the alert form; alert bodies are system-generated).

Tests: Domain.UnitTests **177** (+18), Application.UnitTests **351** (+32), Angular 7 specs
(unchanged). `dotnet build` / `ng build` / `tsc --noEmit` clean. Manual E2E against a fresh
Organization with the real API, real SQL Server and **real SMTP**, covering the fire, the local-time
correctness, no-double-send, restart idempotency, the SMTP failure path, and the unique index.

---

## Step 2 — confirm-live findings (Tigg UAT, Configurations > Apps > Alert Scheduler)

Every one of the module scan's open questions was closed by opening the real screen. The scan
(§15) had recorded "Medium (Email only)" and "Schedule (Daily confirmed)" without establishing
whether those lists were *exhaustive*; they are.

| Question | Answer, confirmed live |
|---|---|
| Alert Type options | **Exactly two** — Daily Transaction Summary, CRM Report. The dropdown holds nothing else, so the phase is not "wire two and defer six". |
| Medium options | **Exactly one** — Email. No SMS, despite `ISmsSender` existing here since Phase 18. Not built. |
| Schedule options | **Exactly one** — Daily, paired with an HH:mm time picker. No Weekly/Monthly/Hourly, no cron expression anywhere. |
| Recipients control | A **plain free-text input**, not a chip list or multi-select; the grid column is the singular "RECIPIENT". |
| Row actions | **Edit / Delete / Mark As Inactive**. Plus a "Show Inactive" checkbox on the panel header. |
| A "Run now" / "Send test" action | **Does not exist.** See Decision D. |
| Timezone of the picker | **Tenant-local Nepal wall clock.** The Create dialog defaulted to 21:55 at an instant when UTC was 16:10 — a 5h45m difference, not zero. The list renders "Daily (19:57)" in the same frame. |
| Is the body a `CustomTemplate`? | **No.** The Create New Alert dialog has no template field of any kind. Bodies are system-generated. `CustomTemplateType.Email` remains unconsumed and is *not* wired to alerts. |
| Anything the scan missed | **Yes — "Email Logs".** The panel header's kebab menu opens an "Email Details" list: one row per send, reading "**sent** email to &lt;address&gt;" with the alert type and a timestamp. |

The Email Logs finding is the one that changed the plan. The brief had hypothesised a send ledger as
"the usual way to make delivery semantics checkable"; the reference product turns out to already
have one and to surface it as a user-facing screen. So the ledger is not scaffolding invented for
testability — it is a feature, and it got built as one.

A smaller observation, recorded because it is the sort of thing that reads as a bug later: the UAT
tenant's most recent Email Logs rows are from **May 2026** while alerts remain active and the date
was August. Whatever that says about the reference deployment, it is not something to imitate.

---

## Step 1 — the three architecture decisions

### Decision A — the job runner: hand-rolled `BackgroundService` + `PeriodicTimer` + `TimeProvider`

**Alternatives weighed:** Hangfire, Quartz.NET, Coravel, and this.

The case for a real scheduler library is always the same four things: durable schedule state across
restarts, catch-up for missed windows, multi-instance coordination, and operational visibility. Each
was checked against what this phase actually needs:

- **Durable schedule state** — already durable, and not as runner state: the schedule *is* tenant
  data (`AlertDefinitions.ScheduleTime`). There is nothing for a job store to persist that the
  product's own table does not already hold.
- **Catch-up** — Decision C defines it as "at most one late send per definition per local day", which
  is a property of the ledger, not of a scheduler. A job store's replay semantics would have to be
  constrained *back down* to this.
- **Multi-instance coordination** — solved by the unique index on the ledger, which this phase needs
  regardless of runner (see Decision C). A distributed lock would be a second, weaker mechanism
  layered on top of a stronger one.
- **Operational visibility** — the Email Logs screen is the visibility, and it is a screen the
  reference product has and users expect. A Hangfire dashboard would be a *second* place to look,
  and one that needs securing (it is an authenticated admin surface with job-triggering powers, on a
  codebase whose auth is a cookie-borne JWT plus a CORS allow-list).

Against that, the costs are real: a new package, a second schema in the tenant database, a
deployment/migration story, and a dashboard endpoint to lock down. QuestPDF was taken in Phase 20d
because rendering PDFs by hand was not sensible; this is the opposite situation — the machinery a
library provides is machinery this design does not use.

**Decision: hand-roll it**, with the split that matters more than the runner choice:
`AlertSchedulerHostedService` owns the timer, the DI scope and the process lifetime, and contains
**no business decision**; `AlertDispatcher` owns every decision and takes `TimeProvider`. That is
what makes the phase testable with `FakeTimeProvider` and no `Task.Delay` anywhere.

Three things in the hosted service are the standard ways a first hosted service in a codebase goes
wrong, and all three are handled explicitly (they are commented in the file, not just here):

1. **A scope per tick.** `IAppDbContext` and `IEmailSender` are `AddScoped`; a singleton
   `BackgroundService` cannot inject either, and capturing one would pin a `DbContext` for the
   process lifetime. `IServiceScopeFactory.CreateScope()` inside the loop.
2. **`IOptionsMonitor`, not `IOptions`.** `IOptions` caches at first resolution and a singleton never
   sees a later user-secrets change — precisely the trap `phase-20g` hit. The poll interval is
   re-read every iteration and applied to `PeriodicTimer.Period`, which is exactly how the manual
   E2E shortened it.
3. **A failing tick never kills the loop.** An unhandled exception in `ExecuteAsync` would stop the
   scheduler for the rest of the process's life while the app kept serving HTTP perfectly happily.

### Decision B — how a jobless command authenticates: **it doesn't, because it isn't a command**

This is the phase's security decision and it deserves the space.

The wall is real and was verified: `CurrentUserService` (`src/Api/Services/CurrentUserService.cs`)
throws `InvalidOperationException` when there is no HTTP context, and both `AuthorizationBehavior`
and `AuditBehavior` read `ICurrentUserService.UserId`. Any MediatR request sent from a background job
throws before reaching its handler. Since Phase 20f the pipeline is six behaviors deep, so a
job-invoked command would additionally have to satisfy `FeatureGateBehavior`'s `IOrganizationScoped`
requirement and `LockDateBehavior`.

**The alternatives, and what each costs:**

| Option | Blast radius |
|---|---|
| **A system/service principal** — a fixed `Guid` the job runs as | Needs a real `OrganizationMembership` row per tenant with granted permissions, or `AuthorizationBehavior` rejects it anyway. That is a permanent, invisible super-user in every tenant's membership table. Anything that ever resolves `ICurrentUserService` outside a request then inherits it. |
| **An ambient job-scoped user** (`AsyncLocal`, or a swapped `ICurrentUserService`) | Same problem plus a worse one: it makes "who am I" ambient and settable, so a future bug that forgets to clear it leaks an identity across scopes. |
| **A per-tenant "alert runs as this user" field on the definition** | Turns the alert form into a privilege-selection UI — an admin picks whose permissions the outbound feed inherits. Also breaks when that user is deactivated. |
| **Send no MediatR request at all** | Nothing to authenticate. |

**Decision: the last one.** `IAlertContentBuilder` takes an explicit `organizationId` and reads
`IAppDbContext` directly. The dispatcher never calls `ISender`. `CurrentUserService` is untouched and
still throws outside HTTP — there is **no authentication-bypass surface introduced by this phase**,
which is a materially better outcome than "we introduced one and made it narrow".

**Where the access control actually lives, then.** Entirely at *definition* time:
`Configuration.AlertDefinition.Manage` gates creating and editing an alert. That is the right place,
because the risk this feature carries is not "the job read too much" — it is that **an alert is an
outbound data feed to addresses nothing ever permission-checks**. The Recipients box is free text; a
typo mails the tenant's figures to a stranger.

Two further mitigations follow from taking that seriously:

- **The content is bounded to aggregates, deliberately.** `DailyTransactionSummaryContentBuilder`
  emits counts and totals per document type; `CrmReportContentBuilder` emits counts plus one revenue
  figure. No contact names, no PAN, no document codes, no per-transaction rows. The worst case of a
  mis-typed recipient is a leaked daily turnover number, not a customer list. Private Deals and
  private WorkTasks are *counted*, never listed, which is why the builder does not need an identity
  to decide what it may see. Widening this later is a scope decision to take on purpose, and both
  builders say so in their doc comments.
- **`CreatedByUserId` is stored on the definition** — not an authorization input (nothing reads it
  for a decision), but an alert is an outbound feed and who set it up is worth keeping.

**What the dispatcher's cross-tenant query means.** `AlertDispatcher.DispatchDueAsync` queries
`AlertDefinitions` with no `OrganizationId` filter, which is the one deliberate exception to
CLAUDE.md's "every handler filters by OrganizationId" rule. The rule exists because a handler acts
for one signed-in user in one organization; the dispatcher is not a handler and has no caller — it
serves every tenant's schedule. Each definition's own `OrganizationId` is then the only scope its
content build ever sees. A unit test asserts this directly rather than trusting it: two organizations,
two alerts, and each recipient's body must contain its own organization's name **and not the other's**.

### Decision C — delivery semantics: at-most-once, ledger-first, bounded catch-up

**The mechanism, in one sentence:** the dispatcher inserts an `AlertSendLog` row with status
`Pending` and **commits it before calling `IEmailSender`**, under a unique index on
`(AlertDefinitionId, OccurrenceDate, Recipient)`.

Everything else falls out of that ordering:

- **Idempotency across restarts.** The row survives the process; a new dispatcher over the same
  database finds the occurrence claimed and skips it. Proven by a unit test that throws the
  dispatcher away entirely and rebuilds it, and again live across a real API restart.
- **Multi-instance safety.** Two instances ticking simultaneously: the second insert violates the
  unique index, `TryClaimAsync` catches `DbUpdateException`, detaches the entry and skips the
  recipient. No distributed lock, no leader election, no configuration. Verified directly against
  real SQL Server (see E2E #6).
- **At-most-once, chosen not defaulted.** A crash between the commit and SMTP leaves a `Pending` row
  that is **never retried**. A send that throws is recorded `Failed` and **not retried within the
  occurrence**; the next day's occurrence is a fresh row. The reasoning: a duplicate daily summary to
  a real customer is worse than a missing one, and a missing one is *visible* — the Email Logs screen
  shows the Pending or Failed row. At-least-once would trade a silent duplicate for a visible gap,
  which is the wrong way round for unattended mail.
- **Bounded catch-up.** Each tick considers **only today's local date**, and treats a definition as
  due once the local clock has passed its `ScheduleTime`. So a slot missed because the process was
  down still fires when the process comes back *the same local day*; and a three-day outage produces
  **one** send on restart, not three, because yesterday's occurrence is simply never revisited. A unit
  test asserts both halves.
- **Editing an alert takes effect tomorrow.** The ledger key ignores the time, so retiming an alert
  to a later slot today cannot resurrect an already-fired occurrence. Same behaviour as editing a
  cron entry, and the only one that cannot double-mail anyone. Noted in the Update handler.

**Failure-path positions, stated explicitly:**

| Situation | Behaviour |
|---|---|
| SMTP throws | Row → `Failed` with the exception message (truncated to 1000 chars, never allowed to fail the SaveChanges that records it). Other recipients of the same occurrence still go. |
| Retries | **None.** Not within the occurrence, not on a later tick. |
| A permanently failing alert | Keeps producing one `Failed` row per day. It does **not** disable itself — silently deactivating a tenant's alert is a worse surprise than a visible daily failure row. |
| Malformed recipient address | Rejected at **definition** time by `AlertDefinitionValidation` (`MailAddress`, not a regex, so it agrees exactly with what the sender can accept). The dispatcher still tolerates one, since a definition could predate a validator change. |
| Empty recipient list | No-op, no ledger rows. Unreachable through the API (validation), so not an error. |
| Tenant had no activity | **Sends a zero-figure summary.** A daily summary that silently stops arriving is indistinguishable from a broken scheduler. Unit-tested. |
| Content build throws | Recorded as `Failed` against every pending recipient rather than swallowed — otherwise a permanently broken builder would retry on every tick forever with nothing visible in Email Logs. Unit-tested. |

### Decision D — no "Run now" action

The brief offered "Run now" as both a feature and the thing that makes manual E2E possible without
waiting a day. Confirm-live settled the first half: the reference product's row menu is Edit /
Delete / Mark As Inactive and nothing else.

Building one anyway would add an authenticated endpoint whose effect is *make the server send email
right now* — a spam surface with no requirement behind it. Declined. Manual E2E instead shortens
`AlertScheduler:PollInterval` and schedules a slot two minutes out, which exercises the real timing
path rather than bypassing it, and is a stronger test for it.

### Timezone: a fixed offset, not `TimeZoneInfo`

`Domain/Common/NepalTime.cs` is the single place UTC meets the tenant's wall clock. Nepal has
observed UTC+05:45 continuously since 1986 and has never observed DST, so there is no rule for a tz
database to add value over — and a fixed offset behaves identically on Windows and Linux, whereas
the id differs (`Nepal Standard Time` vs `Asia/Kathmandu`) and a `FindSystemTimeZoneById` miss throws
at runtime on whichever platform was not the one it was written on. `Organization` carries no
timezone field and this is a Nepal-only product, so there is exactly one tenant timezone.

`NepalTimeTests` is written so that a naive "local == UTC" implementation fails every assertion, and
so is `AlertDispatcherTests.Uses_the_Nepal_local_day_and_time_not_UTC` — see the testing section.

---

## Permission-key derivation

Three new keys, all **Admin-only** for both roles (`RolePermission` rows `00000000-0000-0000-0002-
000000000133`..`138`, seeded through `RolePermissionConfiguration.HasData` before the migration was
scaffolded, per phase-9's lesson):

| Key | Admin | Member | Why |
|---|---|---|---|
| `Configuration.AlertDefinition.View` | granted | denied | The list discloses *where the tenant's figures are being sent*. |
| `Configuration.AlertDefinition.Manage` | granted | denied | Creating one makes the server mail trading figures to arbitrary unvalidated addresses on a schedule. Strictly a larger capability than any Phase 20d control-plane lookup. |
| `Configuration.AlertSendLog.View` | granted | denied | Separate key, not folded into the definition's View: it is a distinct screen in the reference product and exposes strictly more — every address actually mailed, per occurrence, with failures. |

This follows Phase 20d's Admin-only control-plane bar rather than the `CreditTerm`/`CostTerm`
Member-View-by-default norm, and unlike 20d it is not a judgment call — nothing here populates a
Member-facing picker on any document form, and the capability is outbound data egress.

**No key exists for "the scheduler runs an alert"**, because there is nothing to gate at send time:
the dispatcher sends no request and has no acting user. That absence is deliberate and is noted in
`PermissionKeys.cs` so a future reader does not read it as an oversight.

---

## What was built

**Domain** (`src/Domain/`)
- `Common/NepalTime.cs` — the one UTC↔Nepal conversion point.
- `Configuration/AlertDefinition.cs` — `ITenantLookupEntity`, so the generic
  `ListLookupsQuery<T>`/`DeleteLookupCommand<T>` pair covers list and delete for free (the
  `PrintingTemplate`/`CustomTemplate` precedent). Recipients stored as one comma-separated string
  (matching the real control), parsed by `RecipientAddresses` — trimmed, semicolon-tolerant,
  case-insensitively de-duplicated, and explicitly `Ignore()`d in EF so EF Core 8+'s primitive-
  collection mapping cannot conjure a phantom column.
- `Configuration/AlertSendLog.cs` + `AlertSendStatus.cs` — the claim ticket / history row.
- `Configuration/AlertMedium.cs`, `AlertType.cs`, `AlertScheduleFrequency.cs` — each documenting that
  its member list is a live-confirmed fact, not a placeholder.

**Application** (`src/Application/`)
- `Alerts/IAlertContentBuilder.cs` + `DailyTransactionSummaryContentBuilder` + `CrmReportContentBuilder`
  — one strategy per `AlertType`, resolved from the injected `IEnumerable`, the `IGlPostingRule<T>`
  registration shape. Adding a type is a class plus one DI line, with no dispatcher change.
- `Alerts/IAlertDispatcher.cs` + `AlertDispatcher.cs` — the whole decision surface.
- `Configuration/Commands/{Create,Update,SetAlertDefinitionActive}` + `AlertDefinitionValidation.cs`.
  `SetAlertDefinitionActive` is its own single-field command rather than "Update with everything else
  unchanged", because the row action has no form open and a read-modify-write would clobber a
  concurrent edit (the `SetDefault*` precedent from 20d).
- `Configuration/Queries/ListAlertSendLogs` — newest-first, genuinely paginated (this is the one
  Configuration table that realistically reaches NFR-5.1's framing), and **left-joined** to the
  definition so deleting an alert does not erase the proof that mail was sent; the row renders
  `(deleted alert)`.

**Infrastructure** (`src/Infrastructure/`)
- `Alerts/AlertSchedulerHostedService.cs` + `AlertSchedulerOptions.cs` (`Enabled`, `PollInterval`,
  bound lazily via `AddOptions().Bind()`).
- EF configurations for both entities; `TryAddSingleton(TimeProvider.System)`.
- Migration `20260831162552_AddAlertScheduler` — two tables, six permission rows, and the unique
  index. Read before applying; purely additive, no column drops, nothing to reorder.

**Api** — six routes under the existing configuration group: `GET/POST /alerts`,
`PUT /alerts/{id}`, `PUT /alerts/{id}/active`, `DELETE /alerts/{id}`, `GET /alert-send-logs`.

**Angular** (`web/`) — `alert-list-page`, routed at
`organizations/:id/configuration/alerts` and linked from the Configurations shell. Mirrors the real
screen: the same five grid columns, Show Inactive, per-row Edit / Mark As Inactive / Delete, and
Email Logs as a togglable section (this app's card layout rather than the reference product's
slide-over). Medium and Schedule render as disabled fields showing "Email" and "Daily" — they are
sent as constants, because a dropdown with one option is a control the user can never use.

---

## Testing

**Automated:** Domain.UnitTests 177 (+18), Application.UnitTests 351 (+32), Angular 7 (unchanged).
`dotnet build`, `ng build`, `npx tsc --noEmit` all clean.

**No `Task.Delay` or `Thread.Sleep` anywhere.** Every scheduler test drives `AlertDispatcher`
directly with `FakeTimeProvider` (`Microsoft.Extensions.TimeProvider.Testing`, added to
Application.UnitTests); the hosted service — which holds no decision — is never instantiated. The
clock seam was designed in on day one rather than retrofitted, specifically because of
`phase-19-status.md` bug #2, where real-clock coupling produced a silently all-zero result rather
than a loud failure.

The boundary cases the brief asked for, each with the test that proves it:

| Property | Test |
|---|---|
| Fires once the local clock passes the slot | `Fires_once_the_local_clock_has_passed_the_scheduled_time` |
| Does not fire early | `Does_not_fire_before_the_scheduled_local_time` |
| **Timezone, discriminating** | `Uses_the_Nepal_local_day_and_time_not_UTC` — at 18:30 UTC the Nepal clock reads **00:15 on the next day**; a 20:00 alert must not fire *and* a 00:10 alert must, logged against the next local date. A UTC implementation passes the first half by luck and fails the second. |
| Never twice in one local day | `Does_not_fire_twice_in_the_same_local_day` (three ticks across the day) |
| Fires again tomorrow | `Fires_again_on_the_next_local_day` |
| Missed slot fires late; no multi-day backfill | `Fires_a_missed_slot_later_the_same_day_but_never_backfills_earlier_days` |
| **Restart idempotency** | `Sends_exactly_once_across_a_simulated_process_restart` — dispatcher and sender discarded and rebuilt over the same database |
| Inactive definitions skipped | `Skips_inactive_definitions` |
| **Tenant isolation of content** | `Each_tenants_alert_carries_only_that_tenants_data` — asserts each body contains its own org name and *not* the other's |
| One email and one log row per recipient | `Sends_one_email_per_recipient_and_logs_each_separately` |
| SMTP failure recorded, not retried, others still sent | `Records_a_failed_send_and_does_not_retry_it_within_the_occurrence` |
| Empty period still sends | `Sends_a_zero_summary_when_the_tenant_had_no_activity` |
| Content-build failure recorded once | `Records_a_content_build_failure_rather_than_retrying_it_on_every_tick` |
| Right builder per alert type | `Resolves_the_content_builder_matching_the_alert_type` |

**What the unit suite deliberately cannot prove.** The EF Core InMemory provider does not enforce
unique indexes, so `TryClaimAsync`'s `DbUpdateException` path — the multi-instance backstop — is
unreachable there. The first line of defence (the already-claimed pre-check) is fully covered and is
what protects the single-instance and restart cases; the index itself was verified against real SQL
Server in E2E #6 rather than assumed. This is stated in the test class's own doc comment so a future
reader does not mistake the gap for coverage.

### Manual E2E

Fresh Organization **"Phase 20E Alerts Ltd"** (`033a2b6b-…`) created via curl + cookie jar with the
reusable `Testing:*` admin; browser clicks reserved for this phase's own screen. Real API, real SQL
Server, real SMTP (`smtp.gmail.com`), `AlertScheduler:PollInterval` temporarily set to 15s (the
secret was removed again afterwards).

1. **Negative permission proof.** `PUT .../organizations/1111…/configuration/alerts/2222…` — a
   nonexistent alert id in an organization the user is not a member of — returned
   **403** `You do not have permission to perform this action (Configuration.AlertDefinition.Manage).`
   403 rather than 404 proves `AuthorizationBehavior` fired before the handler could look anything up.
2. **Validation negative.** Creating an alert with `recipients: "not-an-email"` returned **400**
   `Every recipient must be a valid email address, separated by commas.`
3. **The scheduler fires at the right local minute.** Three alerts scheduled for 22:22 and 22:23
   Nepal. `sqlcmd` shows ledger rows created at **16:37:03Z** (= 22:22:03 Nepal) and **16:38:03Z /
   16:38:06Z** (= 22:23:03/06 Nepal), all with `OccurrenceDate = 2026-08-31` (the local date). The
   server log shows `Alert scheduler dispatched 1 alert email(s).` then `... 2 ...`.
4. **No double send.** Three further ticks over the following 45s left `totalCount` at 3.
5. **Restart idempotency, live.** The API process was killed and restarted (twice, across the SMTP
   swap below) and kept ticking every 15s; the ledger stayed at its existing row count and no
   occurrence was re-sent.
6. **The unique index, directly.** A hand-written `INSERT` reproducing an already-claimed
   `(AlertDefinitionId, OccurrenceDate, Recipient)` — exactly what a second app instance would
   attempt — was rejected: `Msg 2601 … Cannot insert duplicate key row … with unique index
   'IX_AlertSendLogs_AlertDefinitionId_OccurrenceDate_Recipient'.`
7. **The SMTP failure path, with real SMTP.** `Email:SmtpServer` was temporarily pointed at
   `smtp.unreachable-phase20e.invalid` and the API **restarted** (required — `IOptions` caching,
   CLAUDE.md's phase-20g gotcha). The next due alert produced a `Failed` row reading
   **"No such host is known."**, the loop continued, and further ticks did not retry it. This is also
   what proves the earlier `Sent` rows were real SMTP submissions and not a stub. The secret was
   restored and the API restarted again.
8. **The screen.** Alert Scheduler renders the five reference columns; Email Logs shows Sent/Failed
   badges; Mark As Inactive greys the row and Show Inactive reveals it; Edit round-trips name, type
   and time. No console errors.
9. **Persisted state via `sqlcmd`.** After editing an alert through the UI (type CRM Report →
   Daily Transaction Summary, time 22:23 → 07:15, left inactive), the database holds
   `DailyTransactionSummary | 07:15:00 | IsActive=0` — matching the screen exactly. This is the
   `[selected]`-not-`[value]` check the select-race gotcha demands.

**One thing was not independently verified, and should be stated plainly:** whether the messages
*landed in the inbox*. SMTP accepted all of them (that is precisely what a `Sent` row means here, and
E2E #7 proves a real SMTP conversation is in the loop), but the recipient mailbox is the tenant's own
Gmail account and this session cannot read it. Anyone re-running this should open that mailbox and
confirm the subject line `Daily Transaction Summary - Phase 20E Alerts Ltd - 2026-08-31`.

---

## What Phase 21 inherits, and what it still needs

The roadmap promises Phase 21's async import/export (NFR-4.3) reuses this infrastructure. Concretely:

**Inherited, ready to use:**
- The **hosted-service shape**: `BackgroundService` + `TimeProvider`-built `PeriodicTimer`, a
  **DI scope per tick**, `IOptionsMonitor` for hot-reloadable settings, and a tick that logs and
  swallows so the loop survives. Copy this, do not re-derive it.
- The **thin-runner / fat-service split** (`IAlertDispatcher`): every decision in an Application-layer
  service driven by an injected clock, so it is testable with `FakeTimeProvider` and zero waiting.
- The **"claim a ledger row under a unique index, then do the side effect"** idiom, which is the
  general answer to idempotency and multi-instance safety for any job that has an external effect.
- **Decision B's precedent**: a job does its own scoped reads through a purpose-built Application
  service taking an explicit `OrganizationId`, rather than acquiring an identity. Import/export is a
  *write* path, so this will be the harder question there — but the default to beat is "no ambient
  identity", not "pick a principal".
- `Domain/Common/NepalTime` for anything date-boundary-shaped.

**Still to build for Phase 21 — this phase deliberately did not pre-build any of it:**
- A **work queue**. Import/export jobs are on-demand, not on a schedule; they need a job table with
  a claim/lease, not a "what is due right now" query.
- **Progress and cancellation.** `IAlertDispatcher` has neither and does not need them; a long import
  needs a progress percentage and a user-initiated cancel.
- **Payload storage** for the uploaded spreadsheet and the generated export (`IFileStorage` from
  Phase 18 is the obvious home).
- **Completion notification** to the initiating user (NFR-4.3) — which, unlike an alert, *does* have
  a specific user to notify, so it may want the identity question re-opened for that narrow purpose.
- Whether one shared hosted service polls several job kinds or each gets its own. Not decided here;
  one runner for one job is what this phase needed.

## Deferred / not built (mechanical follow-up)

- **SMS medium.** `ISmsSender` exists (Phase 18) and an `AlertMedium.Sms` member plus a branch in the
  dispatcher would be small — but the reference product's Medium dropdown has one option, so there
  is nothing to mirror.
- **More alert types.** The dropdown has exactly two and both are built. A third is a new
  `IAlertContentBuilder` plus one DI line.
- **Weekly/monthly recurrence.** Not offered by the product. Adding one means extending
  `AlertScheduleFrequency` and the occurrence key, which is a real design change, not a config flag.
- **A pager on Email Logs.** The query is paginated server-side and the endpoint takes
  `page`/`pageSize`; the screen currently renders the first page only. A visible pager is UI work.
- **Retry/backoff for failed sends.** Explicitly out of scope per Decision C, not an oversight.
