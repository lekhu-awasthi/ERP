# Phase 59 kickoff — re-planning, from a fresh read (the forward plan is empty)

Phase 58 is complete. **Commit it before starting** if the user has not (its commit message was handed
over at the end of that session), then start here and do not continue its thread.

**There is no scheduled phase 59, and that is the honest statement.** Phase 58 built the last two of
the vendor's 22 document types. The census that produced 54–57 was reproduced exactly on 2026-09-21,
and every deferral with a cheap start condition has now been promoted. So this session **plans**, as
phase 53 did. It does not build unless the plan it writes says a small, fully evidenced piece should
be built in the same session.

---

## Read first (targeted, not whole files)

- `docs/roadmap.md` → *Forward plan — empty (2026-09-25, after phase 58)*, *Outside the sequence*,
  and *Deferred beyond this roadmap*.
- `docs/phase-58-status.md` → TL;DR, Decision G (the surface divergences), and **"Carried into a
  later phase"** (eight items).
- `docs/phase-53-status.md` → TL;DR and the census method; `docs/phase-57-status.md` → "Step 1 — the
  census re-run" (how 166 was reproduced to the row: 162 plus the four `-alter` keys, byte-identical
  bundle).
- `docs/erp-module-scan.md` → the 2026-09-24 appendix *Physical Movement, read and written*, and the
  2026-09-02 confirm-live appendix.
- `docs/phase-lessons.md` → the phase 53, 57 and 58 paragraphs.
- `docs/known-gotchas.md` headings: *A census is evidence only when it reproduces to the row*, *A
  feature gated on a flag the tenant lacks is invisible to a screen pass*, *A setting that looks like
  a behaviour switch may only pick a default*, *A route is not a feature*.

## Step 0 — the tenant

*Hamro Samaan* (`hamrosamman.tigg.app`) is a 15-day trial created on or about 2026-09-24. It was left
on **Physical Movement**, with Negative Item Balance back on Do Nothing, and it is cleared for writes.
Check that it is still reachable and not expired before relying on it. The user signs in, and no
credentials are ever entered. Moonbeam has not been touched since phase 57.

## Step 1 — the census, re-run

1. Re-fetch the vendor's bundle and compare its byte size with 7,288,492. If it is identical, the
   key catalogue has not moved. Say so and move on.
2. If it changed, re-derive the `<feature>-<verb>` keys with phase 57's regex plus the `-alter`
   ones, and diff against 166. **A number that does not reproduce to the row is not evidence** (phase
   57).
3. Re-derive the report catalogue from the bundle's own `{id, url, name}` list and diff against 51.
   Run it **with Physical Movement on**, because Hamro Samaan is the first tenant where that flag is
   on and a screen-based pass could not see behind it before.
4. Record the result as a dated appendix in `erp-module-scan.md`, even if it is "no change".

## Step 2 — the candidates, each judged on evidence

| Candidate | Evidence in hand | Open question |
|---|---|---|
| Phase 58's carried items: the Mode filter on Movement / Ledger / Ageing, partial receipts and deliveries (per-line counters), Mark as Delivered / Processed, batch/serial and a per-line warehouse on DN/GRN | The vendor's shape was **read** in phase 58, and the payloads are recorded | Is this one "physical-movement completion" phase, or two? Partial receipts touch phase 6's caps-net-of-reversals rule and are the heaviest. |
| The Invoice availability gate summing the entered `Quantity`, not `PrimaryQuantity` (phase 58, bug 9) | Found in code and filed as a separate task | Check whether that task already fixed it. If not, it is a bug fix, not a phase. |
| Warehouse Transfer in Custom Fields | Seen live on 2026-09-24; phase 27a had recorded it absent | Re-read it on Moonbeam too: is it a vendor change, or was 27a wrong? |
| The auto-match suggestion engine | The vendor returns `account_suggestions: null` on every row | This is a **product decision**. Ask the user what "suggest a match" should mean before scoping anything. |
| NVDA hour / traceability reports' columns / full-text search | Unchanged | Each needs an actor or a decision; say so and leave them. |

## The decisions to make

- **Whether there is a phase 59 at all.** "Nothing is worth building yet" is a legitimate outcome and
  is phase 53's own rule against padding.
- **If there is, its scope and its start condition**, written as a roadmap *Forward plan* entry in the
  phase-53 shape: evidence, open confirm-live questions, decisions, and exit bar.
- **Whether any carried item needs a live re-read** before it can be designed. For example, partial
  receipts' counters were read from one PO, and a second receipt was never made.

## Exit bar

- The census appendix, dated, with its numbers reproduced to the row (or "unchanged, byte-identical").
- `docs/roadmap.md`'s Forward plan rewritten from that evidence, each entry with a start condition.
- A `docs/phase-59-status.md` recording what was read, what was decided, and why. It gets a TL;DR
  like every other phase.
- CLAUDE.md's Current status refreshed, and a phase-index line added **only if** something was
  built.
- No commit. Hand over a ready commit message, and end with the next session's kickoff prompt.
