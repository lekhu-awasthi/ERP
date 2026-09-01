# Phase 22 status — Document inbox (FR-10.3)

## TL;DR

Ships the whole of FR-10.3: an `UploadedDocument` aggregate in `Domain/Workflow` (reusing Phase 18's
`IFileStorage` and `AttachmentValidation` unchanged, exactly as `IFileStorage`'s own doc comment
anticipated), a `Workflow > Document` inbox screen with Pending/Done tabs beside the Transaction
Approval queue, a conversion flow into all four of FR-10.3's target types, a permanent viewable link
between the produced transaction and the scan it came from, **and** the AI extraction, built rather
than stubbed — the official Anthropic C# SDK behind an Application-layer `IDocumentExtractor` seam,
with a fake in every test and no test touching the network.

**Decision B is the phase's centre of gravity, and it is prefill-and-submit.** Converting creates
nothing. It navigates to the target's ordinary `new` route carrying `?inboxDocumentId=`; the page
fetches a server-computed prefill, the user reviews a normal form beside the scan, and *their* Save
runs the ordinary `CreateXCommand` through the whole six-behavior pipeline — numbering, validation,
lock-date, feature gates, posting rules and audit all unchanged and untouched by this phase. A second
call then records which transaction came out. **Carrying the document id in the URL rather than in
`PendingTemplateStore`** fixes that store's read-once, does-not-survive-reload cost, works uniformly
for the two targets it never covered (Expense, Quick Payment), and is what makes the link survive the
round trip at all.

**Decision C is the product decision, and it is written for a non-engineer in "What leaves the
tenant" below.** Nothing in this tree had ever sent customer data to an LLM. What leaves is *the
bytes of the one file a user clicked Extract on, plus a fixed prompt* — no contact list, no product
catalogue, no organization name, no user identity, no other document. Two independent gates, both
default-closed: the tenant opts in (`TenantSettings.AiDocumentExtractionEnabled`, **default off**,
its own command so a routine settings save can never re-enable egress) and the acting user holds
`Workflow.InboxDocument.Extract` (**default Admin-only**). Nothing runs automatically — upload never
triggers extraction. Failure is an *outcome*, not an error: a timeout, a 429, garbage, or no
credential at all returns 200 with the document's own status set and leaves it exactly as convertible
by hand as it was a second earlier. And the extraction is audited — `DocumentType.DocumentExtraction`
plus `"Extract"` in `AuditBehavior`'s prefix list — because it is the only action in the product that
sends a customer's business document outward and it leaves no other trace.

Four permission keys (Decision F): `Workflow.InboxDocument.View` / `.Manage` **Admin+Member** (the
inbox is a working queue for whoever photographs the bills, and every transaction it produces is
already Member-visible — the flat-register PAN argument does not transfer), `.Extract` and
`Configuration.AiDocumentExtraction.Manage` **Admin-only**. The *conversion* deliberately has no key
of its own: `GetInboxDocumentPrefillQuery.PermissionKey` resolves to the **target type's own Create
key** per request, mirroring `PrintDocumentQuery` — proven live, a user with the whole inbox but
without `Purchasing.PurchaseBill.Create` gets a 403 naming that exact key on a real document.

Tests: Domain 202 (+10), Application.UnitTests 477 (+25), Api.IntegrationTests 18 (+4), Angular 41
(+15); `dotnet build` / `dotnet test` / `ng build` / `ng test` / `tsc --noEmit` all clean. Manual E2E
against a fresh Organization on real SQL Server uploaded a real PNG through the real multipart
endpoint, proved the bytes round-trip and the blob genuinely exists on disk, converted it to a
**Draft** Purchase Bill with `sqlcmd`-verified zero GL/stock/payment rows, found the scan again *from
the transaction* and streamed it back byte-identical, and got 409s for convert-twice / delete / reopen
and 403s naming four exact keys. **The browser pass on the new screens is outstanding** and the reason
is stated plainly in "What was not verified" below.

---

## Step 2 — confirm live: not possible, and what that cost

Unlike Phase 21c, this phase was **not** starting blind: `erp-module-scan.md` line 110 opened the
Workflow `Document` tab during the original scan and recorded the Pending/Done tabs, the drag-and-drop
upload, the sixteen "+ ADD AS" targets with four marked AI-assisted, the thumbnail previews, the
per-row Label chips, and a speculative `UploadedDocument` data model. That is a genuine live-confirmed
shape, and every structural decision below traces to it.

What the scan recorded was a **menu and a data model, not a screen's behaviour**, and this session
could not open the live UAT tenant to close that gap. Five questions therefore stayed unanswered and
were decided from this codebase's own precedents instead:

1. **Is "+ ADD AS" per-row or a bulk action?** Built per-row. A bulk conversion would have to create
   transactions server-side without a human on each form, which Decision B rules out on its own terms.
2. **What happens immediately after picking a type** — navigate to a pre-filled form, or create
   something? Decided as navigate (Decision B), argued from first principles rather than observation.
3. **Is there a visible "extracting…" state, and what happens to a failed extraction?** Built as a
   synchronous call with a per-row spinner and a persisted status; a failure renders as a plainly
   worded "Nothing was read from this document" panel with a Try again button.
4. **Are extracted fields visually distinguished on the target form?** In this build they are called
   out in a banner above the form listing exactly which values a machine suggested, rather than by
   per-field highlighting. Per-field marking would be better and is a clean follow-up; the banner is
   what the honesty requirement actually demands and it is testable.
5. **Does the document move to Done automatically, and where is the source linked from on the
   resulting transaction?** Decided: yes automatically (Decision A), and via a `SourceDocumentPanel`
   on the transaction's own detail page (Decision E).

Also still outstanding from 21b and 21c and **not** closed here: `Organization > Developer Mode`,
`Organization > Documents`, `Organization > Migration`, and the browser pass on
`Configurations > Import / Export` and 21c's three screens.

---

## Decision A — what an `UploadedDocument` is, and what its lifecycle means

The invariant is written as an invariant at the top of `UploadedDocument`, because the next reader
will otherwise assume it is an `Attachment` with extra columns. Restated here:

> An UploadedDocument is **evidence**, never a transaction and never a posting. No document number,
> no Draft/Approve/Void lifecycle, no `GlJournalEntry`, no `StockLedgerEntry`, no `Payment`, no
> lock-date gate. Its Pending/Done status says only whether a person has finished dealing with it.

**Status.** Pending/Done, matching the reference product's two tabs, and *both* transitions exist for
a reason. Done is set automatically by conversion, **and** a user can set it by hand: a tenant who
files a receipt they never post needs a way off the Pending tab, and deleting the scan would destroy
the very record they kept it for. Reopen is refused once a transaction points at the document — Done
there is a statement of fact about the ledger, not a housekeeping flag, and un-setting it would leave
a linked document sitting in the inbox inviting a second conversion. **No Discarded/Ignored state**:
a scan nobody wants is deleted, and a third status would have needed a screen nobody asked for.

**The link, and why it lives on the document.** `(LinkedTransactionType, LinkedTransactionId)` on
`UploadedDocument`, not `ReferrerType`/`ReferrerId` on the transaction. The alternative was seriously
considered and rejected: those fields already mean something specific — document-to-document
conversion — and Phase 6's bug #4 is the catalogue of what that meaning *requires* and the fields do
not provide (a `Converted` status on the source, quantity caps net of prior reversals, Contact/TDS
consistency). An inbox scan is none of that: nothing to cap, no net effect to trace, no accounting
sense in which it is "converted". Putting the link here costs **zero change to any transactional
aggregate** and matches the reference product's own model (scan line 111). `DocumentType` is reused
for the type rather than a bespoke enum, because all four targets are already members of it and the
pair is only ever read back as "open this document".

**One document, one transaction.** `LinkTransaction` refuses a second link rather than overwriting or
accumulating. A single page genuinely can be the source of a Purchase Bill *and* the Supplier Payment
that settles it — the honest answer to that is **two uploads of the same page**, not a one-to-many
link that would leave "which transaction does the Done tab mean?" unanswerable. There is no reversal
path here to make an accidental second conversion undoable, and the refusal message says exactly what
to do instead. The prefill query refuses too, so the second conversion dies before the user has typed
anything.

**Deletion.** Refused once a transaction points at the document. In Nepal the scan is often the very
thing a tenant is required to retain, and deleting it would leave the posted bill's Source document
panel pointing at nothing. An unlinked document deletes row-then-blob, following
`DeleteAttachmentCommandHandler` exactly (a crash between the two orphans a file — harmless and
cleanable — rather than orphaning a row pointing at a file that no longer exists).

**And it is never swept.** Grounded finding #10 was the right question with the opposite answer to
phase-21b's Decision E: a job artifact is a derived convenience the tenant can regenerate, so it gets
a 7-day retention; an inbox scan is primary evidence behind a posted transaction, so there is no
`SweepAsync` here and deliberately never will be. Stated rather than left unstated.

## Decision B — what "convert" actually does

**Chosen: (i) prefill-and-submit.** Conversion navigates to the target's own `new` route with
`?inboxDocumentId=`. The page fetches `GetInboxDocumentPrefillQuery`, applies what it produced, shows
the scan side by side, and the user's Save runs the ordinary `CreateXCommand`. A second call,
`LinkInboxDocumentCommand`, records the result. **The conversion itself creates nothing** — a
conversion abandoned before Save leaves the inbox exactly as it was, which is asserted as a test.

**Why (ii) server-side create does not apply.** A `ConvertInboxDocumentCommand` would be creating a
transaction *from data a machine guessed at, without a human having pressed Save on it* — which is
precisely the thing FR-10.3's own framing and this phase's honesty requirement forbid. It would also
have to re-derive everything the four Create handlers already do (numbering at Approve, FK checks,
validation, lock-date, posting-rule prerequisites, audit attribution), which is four opportunities to
drift from the real thing and no upside beyond one fewer HTTP call.

**The cost of (i), paid rather than deferred to a bug report.** The link has to be established *after*
the transaction exists, which means carrying the document id through the form and back. Grounded
finding #4's warning about `PendingTemplateStore` is real — it is in-memory and read-once, so an
extraction carried that way dies on a page reload, and it exists for Invoice/CreditNote/PurchaseBill/
DebitNote but not for Expense or Quick Payment, two of the four targets. **So it is not used.** The
document id rides the **URL query string** instead, and the prefill is fetched from the server by that
id. That single change buys reload-survival, uniformity across all four targets including the Reactive
Forms `QuickPaymentPage`, and the return path for the link — for the cost of one extra round trip on a
screen the user is about to spend a minute on.

**Two consequences worth naming.** First, a failure to link must not lose the transaction the user
just saved: every target navigates to the saved document regardless and reports the link failure
there.

Second — and this became a change rather than a caveat — **`QuickPaymentPage` used to auto-approve on
save.** Phase 17 built it as one action: create, then immediately approve, then reset the form. That
was defensible for a screen someone types by hand, and indefensible the moment the Document inbox can
pre-fill it from a scan a machine read: it would post to the General Ledger with no review step, on
one click, from suggested values. The first cut of this phase merely warned about it in red. **On
review it was changed instead**, so Quick Payment/Receipt now matches every other document type:
Save Draft, then Approve.

The two steps stay **on that page** rather than navigating to `payment-detail-page`, and the reason is
the same one that gave the screen its own component in Phase 17's decision #7: that page's
`canApprove()` requires `allocations.length > 0 && remaining === 0`, so a zero-allocation Quick Payment
Draft sent there would have a permanently disabled Approve button. Keeping both steps here also buys
the thing the change was for — the Draft lands in the **Transaction Approval queue** like any other
document, so a second person can approve it there, which is real maker-checker rather than a warning
label. The form is disabled once the Draft exists (so it cannot drift out of sync with the row it
produced), the real code is read off the *approve* response rather than the create one (numbering
happens at Approve — phase-17-status.md's own bug #3), and a failed approval says the draft is saved
and can be approved from the queue. The conversion banner's red warning is gone, because it is no
longer true.

## Decision C — the AI boundary

### What leaves the tenant

When somebody clicks **Extract** on one document, the bytes of **that one file** and a fixed
instruction are sent to Anthropic's Claude API. Nothing else about the organization goes with it — not
its name, not the contact list, not the product catalogue, not the user's identity, not any other
document. The tenant's own data is used to *interpret* the answer afterwards, inside this system
(matching a party name against existing Contacts, a line description against existing Products); it is
never sent outward to help the model guess.

A scanned supplier bill contains a supplier's name, PAN, address, amounts and often a signature. That
is inherent in sending a scanned bill at all: it cannot be redacted out of an image without reading
the image first, which is the very thing being outsourced. So the mitigations are the ones actually
built rather than a promise of redaction that could not be kept — the same discipline as phase-21b's
Decision A refusing to print "Backup" on a button the product could not honour.

### Can a tenant decline?

Yes, and **declining is the default.** `TenantSettings.AiDocumentExtractionEnabled` starts `false` for
every organization, including every existing one (the migration's column default is `0`). Nothing about
the Document inbox is gated on it: upload, manual conversion, linking, viewing and the source-document
panel all work identically with it off — which is Phase 20f's lesson applied (check that a flag-off
tenant can still function; here they obviously can, because manual conversion *is* the feature).

It is deliberately **not** a `TenantFeature`. Those are captured once at Organization creation from the
signup wizard and are immutable afterwards, which is exactly wrong for a consent decision a tenant must
be able to withdraw. And it is its own command, not a field on a general settings save, so a routine
edit of the accounting defaults can never re-enable data egress as a side effect. Withdrawal takes
effect on the **next extraction**, not the next process restart — the flag is re-read on every run, and
that is a test.

### What a user sees when it fails

A plainly worded panel and a still-usable document. Concretely: **no credential on the server** →
"AI-assisted extraction is not configured on this server. The document can still be converted by
hand." (status `Unavailable`, so the screen can say *ask an Admin* rather than *try again*).
**Timeout** → "Extraction timed out after 90 seconds. Try again, or convert the document by hand."
**Anything else** → "The extraction service could not be reached. Try again later, or convert the
document by hand." The vendor's own error text is never shown, because it can echo request content and
these strings are rendered verbatim. In every case the response is **200**, the document keeps its
file, and "+ Add as" is still offered — asserted as a test for all four failure shapes including an
implementation that breaks the never-throw contract outright.

### Where the key lives, and sync vs job

`DocumentExtraction:ApiKey` in `dotnet user-secrets`, never `appsettings.json`, bound through
`IOptionsMonitor` (not `IOptions`, whose first-resolution caching is exactly the documented trap when
flipping a credential mid-session). **Deliberately no `.Validate(...).ValidateOnStart()`** — twice over:
extraction is optional by design so a deployment without a credential must still boot and serve the
whole inbox, and every `ValidateOnStart` option added to this tree has reddened all four host-booting
integration suites in CI. This one adds no key to any of them, and the integration suite asserts the
unconfigured state instead.

**Synchronous, with a 90-second timeout inside the extractor. No background job and no new job table.**
Phase 21c's Decision C test asked the right two questions and both answer no: an extraction is not a
spreadsheet of rows (every `ImportJob` row-count column would be permanently null), and its loop is not
a loop at all — it is one call for one document with a user watching. The status field on the aggregate
is what a poller would have read anyway.

### The honesty requirement

Non-negotiable and built three ways. **(1)** `ExtractedDocumentData`'s every field is nullable and the
prompt makes abstention the easy path — a null renders as an empty box the user obviously must fill,
whereas a wrong-but-confident value renders as a pre-filled box they may not re-read. **(2)** The
conversion panel above every pre-filled form says "Some fields below were read by AI", lists exactly
which values a machine suggested, names the model, flags a party name that matched no Contact, and
shows the document's own printed total for the user to compare against the total the form computes.
**(3)** `ClearInboxDocumentExtraction` throws the suggestion away entirely — gated on `.Manage`, not
`.Extract`, because discarding a machine's guess is ordinary housekeeping while producing one spends
money. Nothing extracted reaches a GL entry without a human pressing Save, and the test that proves it
is the one asserting the conversion alone creates no transaction at all.

### Model and cost

`claude-opus-5`, named explicitly in `DocumentExtractionOptions.ModelId` (overridable per deployment)
rather than defaulted by the SDK, so the id a tenant is shown on the consent card, the id recorded on
`UploadedDocument.ExtractionModelId`, and the id actually called are the same string. Rough per-document
cost at Opus 5 rates ($5/MTok in, $25/MTok out): a one-page scanned bill is on the order of 1.5–2.5k
input tokens with a few hundred out — roughly **US$0.02–0.03 per extraction**, i.e. a few rupees. That
is small per document and is exactly why `.Extract` is a separate, default-Admin key rather than riding
`.Manage`: the cost is in the *aggregate*, and an organization should decide who spends it.

### Why this was built rather than deferred

Deferral was explicitly available and was the right call in four earlier phases. It was not taken here
because the extraction turned out to be genuinely small once the seam existed — one options class, one
`IDocumentExtractor` implementation, two DI lines — and because the *hard* part of the AI half is the
product decision above, which had to be made and written down whether or not any HTTP call was made.
Shipping the decision with a fake behind it would have left the most consequential paragraph in this
document untested against a real API surface.

## Decision D — the four target types and the seam for a fifth

The four are not an arbitrary subset: `erp-module-scan.md` line 110 lists sixteen "+ ADD AS" targets
and marks exactly four ✨ — Quick Payment, Invoice, Expenses, Purchase Bill — which are precisely the
four FR-10.3 names. The other twelve are an additive seam, not this phase's work.

The seam is `InboxConversionTargets`: a `DocumentType` **allow-list** plus a per-type Create-permission
switch, modelled on `PrintDocumentPermissions`. A bespoke `InboxTargetType` enum was rejected because
`UploadedDocument`'s linked-transaction pair has to be a `DocumentType` anyway, and a parallel enum
would need a mapping nobody could keep honest. `DocumentType.Payment` covers Quick Payment — Phase 17
built that screen as a thin variant of the Payment aggregate with `Allocations = []`, not its own type.

**What a fifth target costs, in files:** one member in `InboxConversionTargets.Supported`, one arm in
`CreatePermissionFor`, one arm in `LinkInboxDocumentCommandHandler.TargetExistsAsync`, one arm in the
prefill's contact-direction switch, and the target page consuming the prefill it already receives in a
target-agnostic shape. No new table, no new command, no new permission key, no new DTO. The prefill DTO
is deliberately one shape for all four rather than four shapes, because they overlap almost entirely at
that level (a party, a date, a reference, some lines, a total).

One target-specific judgement worth recording: **Expense prefills no lines at all.** Its lines are GL
accounts, not products, and an extracted line description resolves to nothing an account picker could
use. Guessing an account would be putting a machine's choice into the General Ledger's own coding,
which is exactly the job the human is there for — so the document's total is shown in the panel and the
user splits it themselves.

## Decision E — where the image is viewable, and how

Two surfaces, both required by the brief. **During conversion**, `InboxConversionPanel` renders the scan
above the form (side by side on wide screens via the existing layout), because "with the image
side-by-side" is what makes typing from a scan bearable. **After conversion**, `SourceDocumentPanel`
sits on the transaction's own detail page — Invoice, Purchase Bill and Expense — and that is exit
criterion #2's real shape: it looks the document up *by the transaction it produced*, which is what the
`(OrganizationId, LinkedTransactionType, LinkedTransactionId)` index exists for.

**Inline preview, not a link**, and the mechanism matters. `IFileStorage` deliberately exposes no public
URL, so the preview points at `.../inbox-documents/{id}/content` — an authenticated, permission-checked
API route returning a stream with no `Content-Disposition`. An `<img src>` at that route sends the
httpOnly auth cookie automatically (the Api's CORS policy allows credentials), which is why it was
chosen over fetching a Blob and building an object URL: the preview stays declarative and needs no
manual object-URL lifetime management. The one wrinkle is that Angular sanitizes an **iframe**'s src as
a resource URL and blocks an interpolated string outright, so the PDF path goes through
`bypassSecurityTrustResourceUrl` — safe here because the URL is built entirely from our own API base
plus a route-parameter GUID, with no user-supplied text anywhere in it.

`SourceDocumentPanel` renders **nothing** when no document points at the transaction (the common case)
and **nothing** when the lookup fails — a user without inbox permission must still get a working
Purchase Bill page. Both are tests.

## Decision F — permission keys

Four keys, derived per CLAUDE.md's rule rather than by analogy. Full reasoning lives in
`PermissionKeys.cs`; the summary:

| Key | Admin | Member | Why |
|---|---|---|---|
| `Workflow.InboxDocument.View` | ✔ | ✔ | Routine daily-use working data |
| `Workflow.InboxDocument.Manage` | ✔ | ✔ | Upload / label / delete / mark done / link |
| `Workflow.InboxDocument.Extract` | ✔ | ✘ | Spends money, sends data outward |
| `Configuration.AiDocumentExtraction.Manage` | ✔ | ✘ | The tenant-wide consent decision |

**The Admin+Member call was the one that needed weighing.** The tempting analogy is the flat registers,
Admin-only because a single screen exposes every party's PAN at once — and it does not hold. The inbox
is a *working queue of unprocessed files*, not a register over the tenant's history, and whatever a scan
discloses about one supplier, the resulting Purchase Bill discloses too. `PurchaseBillView` is already
Member-granted, as is `ContactView`, which is what a Contact's own uploaded documents ride on. Making
the inbox Admin-only would have protected nothing and broken the feature for the one person it exists
for: whoever photographs the bills. One View/Manage pair rather than a View/Create/Edit/Approve split,
because there is no maker-checker step here — the approval that matters happens on the *transaction*,
under its own Approve key — and `WorkTask` set the Workflow-context precedent.

**`.Extract` is the only key in this codebase whose derivation rests on something other than data
sensitivity**: running it spends the deployment's money and sends a customer's document to a third
party, and neither is reversible after the fact. Separating it from `.Manage` is what lets an
organization have Members filling the inbox all day while extraction is used sparingly — and equally
lets one that wants the opposite grant it to Member in the role matrix in ten seconds. This is the key
most likely to be widened by a real tenant, and that is fine: default-deny for an outward-bound,
billable action, with an obvious grant path.

**The conversion has no key of its own, deliberately.** `GetInboxDocumentPrefillQuery.PermissionKey`
resolves to the target type's own Create key per request, exactly as `PrintDocumentQuery` resolves a
View key. That is not a second, weaker gate in front of the real one — it is the *same* gate moved one
step earlier, so the inbox can never become a side door around `AuthorizationBehavior`. Proven live:
a role with the full inbox but `Purchasing.PurchaseBill.Create` denied gets 403 naming that key on a
real document, while the same document under `Expense` (granted) returns a prefill.

Reading the AI setting is gated on `InboxDocumentView`, not the Admin-only manage key — every inbox
user needs to know why the Extract button is or is not offered, and "is this switched on?" is not
itself sensitive.

## Decision G — file types

**`AttachmentValidation` is reused wholesale, not forked**: same 10 MB cap, same extension allow-list
(`.pdf .png .jpg .jpeg .gif .doc .docx .xls .xlsx .csv .txt`), same explicit no-virus-scanning scope
note. `InboxDocumentValidation` delegates to it and adds one thing: `IsExtractable`, the images-and-PDF
subset.

The tempting narrowing — restrict the inbox to what extraction can read — would have been the wrong
cut. The inbox's base feature is *manual* conversion, which works for anything a human can open, and a
Nepali SME whose supplier emails a `.xlsx` bill would otherwise have nowhere to put it. So the narrower
need belongs on the Extract button, not on what may be uploaded: a non-extractable document uploads
fine, says "Extraction only works on images and PDFs. This document can still be converted by hand",
and offers "+ Add as" like any other row. `ExtractInboxDocumentCommand` refuses such a file with a 409
rather than burning a vendor call on it.

---

## Testing

**Domain 202 (+10)** — the aggregate's invariants: a second link refused with the first left intact,
Reopen refused once linked, a failed extraction discarding a previously stored suggestion (so a stale
guess cannot be pre-filled as if fresh), `NotAttempted` rejected as an attempt outcome.

**Application.UnitTests 477 (+25)** — the two exit criteria as tests, not a demo; convert-twice at both
the link and the prefill; tenant isolation at phase-21b's bar (org A's rows **absent** from B's answer,
not merely outnumbered); the four extraction failure shapes each leaving the document convertible;
contact resolution respecting the target's contact direction (an Invoice must not resolve a Supplier);
an unmatched party and product resolving to null with the raw text kept and **no Contact minted**;
consent withdrawal stopping the next run; and `Only_the_documents_own_bytes_are_handed_to_the_extractor`,
which is Decision C's central claim asserted rather than promised.

**Api.IntegrationTests 18 (+4)** — a real multipart POST against the real host, because that is the only
thing that proves `.DisableAntiforgery()` (an InMemory unit test never touches real Minimal API endpoint
metadata — the Phase 18 bug, and this is the second endpoint in the tree to need it); the byte round trip
through a real Kestrel response; over-size and disallowed-extension both 400; cross-tenant 404 *and*
cross-organization 403; and the extraction setting reporting off-and-unconfigured, which is exactly a CI
runner's state.

**Angular 41 (+15)** — rendering tests over the two things a screenshot would not catch: "+ Add as",
Delete and Reopen keying off `isLinked` rather than `status`, and the honesty promises actually being on
the page (the consent card naming what leaves the tenant *before* anyone clicks; an extraction labelled
"check every value before you save" with a Discard control; a failed extraction still offering the
conversion). Plus `SourceDocumentPanel` rendering nothing when unsaved, nothing when nothing is linked,
and nothing when the lookup fails.

**Manual E2E** against a fresh Organization on real SQL Server, master data seeded via curl + cookie jar:

- real PNG uploaded through the real multipart endpoint (201), bytes round-tripped byte-identical
  (md5 match), and the blob confirmed present on disk at the `sqlcmd`-read `StorageKey`;
- converted to a Purchase Bill through the ordinary create endpoint and linked — `sqlcmd` confirms
  `Code=DRAFT`, `Status=Draft`, and **0** `GlJournalEntries`, **0** `StockLedgerEntries`, **0**
  `StockMovements`, **0** `Payments`;
- the scan found again *from the transaction* via the linked-transaction filter and streamed back
  byte-identical — exit criterion #2 end to end;
- convert-twice refused at both the link (409) and the prefill (409); delete refused (409); reopen
  refused (409), each naming why;
- **four 403s naming their exact keys**: `Workflow.InboxDocument.Extract` and
  `Configuration.AiDocumentExtraction.Manage` against a **nonexistent id** (403-not-404 proves the check
  fired before the handler), and `Purchasing.PurchaseBill.Create` from the prefill on a real document
  under a purpose-built role — with the same role's `Expense` prefill returning 200, which is the pair
  that proves the key is per-target and not blanket;
- the four new keys confirmed auto-discovered by `PermissionKeyCatalog` (they appear in a fresh role's
  permission matrix without any hand-written seed);
- extraction gates both ways: 409 "turned off for this organization" before opt-in; after an Admin opts
  in, 200 with status `Unavailable` and a readable reason because no server credential is configured —
  and the document still convertible;
- the egress audit row written and readable: `Action=Extract, DocumentType=DocumentExtraction`;
- tenant isolation live: org A's document id under org B returns 404, and B's inbox list is empty;
- over-size (>10 MB) and disallowed extension both 400 through the real multipart endpoint;
- delete of an unlinked document returns 204, removes the row, **and** removes the blob from disk.

### The browser pass

Done, against the same fresh Organization, with the user signing in. All three new surfaces were
driven for real, and it **found two genuine bugs that every other check had missed** — which is
precisely the argument for doing it.

- **Bug 1 (user-blocking): the "+ Add as" menu was clipped, hiding three of the four conversion
  targets.** The grid sits in a Bootstrap `.table-responsive`; setting `overflow-x: auto` makes the
  browser compute `overflow-y: auto` too, so the absolutely-positioned menu was cut off at the
  wrapper's bottom edge. All four items were in the DOM and only "Purchase Bill" was reachable —
  `ng build`, `tsc` and the component test (which asserts the items exist) were all green. Fixed by
  rendering the menu `position: fixed` at coordinates captured from the button on open, with
  dismissal on outside-click and on scroll.
- **Bug 2 (contradictory copy): the row-level notice read "AI-assisted extraction is turned off for
  this organization" while the switch directly above it read On.** `extractionAvailable()` is
  `enabled && extractorConfigured`, so a tenant that has opted in on a server with no credential fell
  into the same branch as a tenant that had not. Two different causes with two different remedies now
  say so separately.

Everything else behaved. Verified in the browser: the consent card's wording; upload through the real
file picker; the inline **image** preview (an earlier broken render turned out to be an invalid
hand-made test fixture, not the app — a canvas-generated PNG renders correctly, and the request was
200 all along); the **PDF `<iframe>`** preview rendering in Chrome's viewer, which is the
`bypassSecurityTrustResourceUrl` path; conversion to Purchase Bill landing on a pre-filled form with
the scan above it and the correct no-extraction wording; Save producing a **DRAFT**; the
`SourceDocumentPanel` showing the scan on the saved bill; the document auto-moving to Done with a
"Purchase Bill created" link and its Delete/Reopen/"+ Add as" controls correctly withdrawn.

The Quick Payment change was driven end to end: Save Draft produced `Code=DRAFT`, `Status=Draft` with
**0** GL entries (`sqlcmd`), the form froze, Approve appeared as a separate action, a first Approve
attempt failed with the server's own 409 ("Default Accounts Payable account is not configured") and
**left the draft intact with Approve still offered**, and after configuring the default the second
attempt posted: `Code=0001`, `Status=Approved`, **1** GL entry, with the page showing the real code
rather than "DRAFT".

**One gap the pass exposed and closed:** `SourceDocumentPanel` had been wired into Invoice, Purchase
Bill and Expense but **not** the two Payment detail pages, so a Quick Payment converted from a scan
had no way to show its source — exit criterion #2 failing for one of the four targets. Added to both
`payment-detail-page` and `supplier-payment-detail-page` and verified live.

### What is still not verified

The real Anthropic API call. It is exercised by no test by design ("no test may call the network"),
and this deployment has no `DocumentExtraction:ApiKey` configured, so the live path proven end to end
is the **unconfigured** one — which the browser pass did confirm reads correctly on screen. Setting a
real key and extracting one bill is a five-minute check that should happen before anyone relies on
the extraction.

---

## Known gotchas hit this phase

1. **A `MultipartFormDataContent` disposed before the send completes** throws
   `ObjectDisposedException: Cannot access a closed Stream` deep inside `TestHost`'s stream plumbing,
   with a stack trace naming `MultipartContent.ContentReadStream.set_Position` and nothing about the
   real cause. A helper that builds the content under `using` and **returns** the `Task` instead of
   awaiting it inside is the trap; three integration tests failed this way at once. Await inside.
2. **Bootstrap's dropdown JS is not loaded in this app** — `web/angular.json` registers
   `src/styles.scss` and no `scripts` entry at all, so a `data-bs-toggle="dropdown"` renders a button
   that silently does nothing. The inbox's "+ Add as" menu is driven from a signal instead. Worth
   knowing before reaching for any other Bootstrap JS component (modal, tooltip, collapse).
3. **A Bootstrap `.dropdown-menu` inside a `.table-responsive` is clipped**, because `overflow-x: auto`
   makes the browser compute `overflow-y: auto` as well. The items are all in the DOM and a component
   test asserting they exist passes; they are simply unreachable on screen. `position: fixed` at
   coordinates captured on open escapes the ancestor overflow. Found only in the browser pass.
4. **Angular blocks an interpolated `<iframe [src]>` outright** as an unsafe resource URL, while
   `<img [src]>` with the same string is fine. A PDF preview therefore needs
   `DomSanitizer.bypassSecurityTrustResourceUrl`; `ng build` does not catch it, only a runtime render
   does.
5. **A `canApprove()` gate on a detail page can strand a document routed there from elsewhere.**
   `payment-detail-page` requires `allocations.length > 0 && remaining === 0`, so sending a
   zero-allocation Quick Payment Draft to it would have produced a Draft nobody could approve. Check
   the destination's own enable-conditions before routing a new kind of document at it.
