# Phase 39 — CRM and workflow as first-class screens, and the two editors

## TL;DR

Mostly surface — routes, a rich-text control, an image upload — so the risk was never silent data
damage but **shipping a control that looks right and is unsafe**. The phase is organised around that:
the rich-text editor is an XSS surface the moment its output is rendered anywhere, and the uploaded
logo is a file the print pipeline embeds.

1. **One rich-text editor, sanitised in the Domain.** `RichText.Sanitize` is *re-emission, not
   filtering*: input is parsed into a tree with no attribute slot except a four-valued enum, and the
   output is generated from string constants the emitter owns. A tokenizer bug can therefore produce
   wrong *formatting* and cannot produce an attribute, a tag name or a URL that came from the input.
   It runs on write, on all seven rich-text columns, enforced by a behavioural sweep guard.
2. **The editor's grammar is the print pipeline's capability list.** The toolbar is eleven buttons
   because that is what `RichTextPdfRenderer` can draw. Colour, font, size, tables and images — all of
   which the reference product offers — are dropped, because a field that renders three different
   ways in three places is worse than one that renders one way everywhere (Decision B).
3. **The logo's format check and its dimension check are one parser.** `ImageHeader` reads PNG/GIF/
   JPEG headers without decoding pixels; "is this at least 300×300" and "is this actually an image"
   have a single answer, and the answer comes from the bytes rather than from the client's
   `Content-Type`.
4. **`BalanceConfirmation` got its consumer** — an addition, not parity: the reference product has the
   context and no action that reaches it either (Decision E).
5. **Deals and Tasks are list + detail in the reference product**, not list-only. Both got their
   standalone list routes; the detail pages are a carried item with a stated reason (Decision F).
6. **The search results page was conditional on 34c, and 34c said yes.** The kind filter is also the
   performance answer: it cuts the fan-out from eighteen queries to one.

**Two corrections to the kickoff**, both from the live pass: `Workflow > Tasks` is an org-wide feed
with no parent column (phase 13 called that "speculative … which erp-module-scan.md never confirms
exists" — correct on its evidence, wrong now), and the reference product has **no post-creation logo
control at all**, so the profile screen is an addition rather than parity.

**Two product defects the E2E found that no unit test could:** a Domain invariant surfacing as a 500
instead of a 400, and `CreateDealCommand.AssigneeUserIds` with no validator rule at all, so omitting
it from the body was a 500 (pre-existing since phase 15). Two more were in the harness: `curl -F
name=<value` reads the value as a *file path* when it starts with `<`, and a **quoted** heredoc in
the Bash tool eats backslash escapes — which cost an hour across three occurrences, the third being
the draft of the gotcha documenting it.

**One finding from widening a guard:** `SearchSweepGuardTests` only recognised `PagedResult<T>`, so
the two list queries that predate it were invisible to the guard whose entire purpose is to notice a
list nobody gave a search box — and they were exactly the two lists this phase had to give one.
Widening it to recognise the envelope by shape surfaced seven more, all legitimately exempt.

Tests: Domain **640 (+188)**, Application.UnitTests **1056 (+21)**, Api.IntegrationTests **29 (+5)**,
Angular **422 (+117)**. `dotnet build` / `dotnet test` / `ng build` / `ng test` all clean. `ng build`
still warns the initial bundle exceeds its 500 kB budget (pre-existing; 650 kB, down from 34b's 726).

---

## Confirm-live pass (Moonbeam UAT, 2026-09-13, read-only)

The kickoff named two unconfirmed shapes. Both were read, and both changed the plan.

### 1. The editor — TinyMCE 7.1.1, one config, two hosts

Read off `tinymce.activeEditor.options.get(...)` in the Terms editor on the Invoice add form and
again in the Send Email dialog. The `toolbar` string is **byte-identical** in both:

```
fontstyle alignment forecolor dent | lineheight | bullist numlist |fontfamily fontsize | code | image | table | customtags | fullscreen | hr
```

- `fontstyle` expands to **Bold / Italic / Strikethrough / Underline**.
- `customtags` is in the string and renders **no button** — registered and missing.
- **No `valid_elements` / `extended_valid_elements` / `invalid_elements`.** TinyMCE's permissive
  default schema; nothing restricts what can be stored.
- `code` is **Source code** — a user can type raw HTML by hand.

Two consequences. "One rich-text editor behind both seams" stops being a design preference and
becomes an observation: the reference product literally mounts the same configured component in both
places, which is what phases 27b and 30 were each describing when they separately recorded "a
textarea standing in for the rich editor" and named the same seam. And **server-side sanitisation is
mandatory rather than defensive** — a Source-code box plus no element whitelist is a stored-XSS hole
by construction, and the field is rendered afterwards in a browser, in a PDF a customer receives, and
in an HTML email body.

### 2. The logo — and the control that does not exist

- Rendered on `Organization > Overview` and on the tenant's own sign-in card.
- **`EDIT DETAILS` has no logo field.** Nine text fields (Name, Display Name, Email, Pan No, Phone
  No, Registered Address, Website, Accounting Starting Date, Vat Registered) and no upload. Clicking
  the logo does nothing.
- Stored in S3 in two renditions, `thumb` 125×125 and `preview` 516×513, behind pre-signed URLs.
- **Printed header** (Invoice print preview, template "Invoice Classic Copy"): logo **top-left**,
  square, ~80pt; the organization block **centred** beside it; the document title **centred below**
  rather than right-aligned.

So in the reference product the logo can only ever be set during signup. See Decision D.

### 3. Two things the kickoff did not ask about, which changed the scope

`CRM >` is Deals, Contacts, Contact Group, SMS. `Workflow >` is Tasks, Document, Transaction
Approval — confirming 34b's carried item #6. But both leaves are **list + detail**:

| | list | detail |
|---|---|---|
| Deals | Pending/Won/Lost tabs, search box, sortable headers, per-row kebab, `+ ADD NEW` | left rail (status toggle, Closing Date, Stage, Assigned By/To, Created), tabs **Overview / Contact Personnel / Tasks**, a **Documents** dropzone, an **Activity** composer |
| Tasks | Pending/Started/Done tabs, search box, columns Due / Created At / Title / Type / Priority / Created By / Assigned To, **no parent column** | Overview, **Documents**, **Activity** (Comments / Activities / Emails), `MARK AS DONE` |

The "no parent column" is the finding that shaped the backend: the standalone list is genuinely *one
list over every parent*, not a filtered view, so `ListTasksQuery` gained a **nullable parent** rather
than a parent picker — and `TaskList` served a third host with no template change.

---

## Decision A — sanitise on write, in the Domain, by re-emission

**Where.** In the Domain's own setters (`SetTerms` on the five types, `CustomTemplate.Create/Update`,
`EmailTemplate.Create/Update`, `EmailSendLog.Queue`), not in a handler and not at render.

Sanitising at render would put the obligation on every future read path, and phase-35a's lesson is
that read paths are exactly what a sweep forgets — 14 of 15 detail DTOs dropped a field there while
the write-path guard stayed green. Sanitising in a handler would leave a Domain caller able to
bypass it. Stored content is therefore already safe and nothing downstream has to remember.

**How, and why it matters more than where.** Nothing in `RichText` asks "is this tag safe?" and
passes the answer through. The input is parsed into `RichTextNode` — a tree whose only attribute slot
is the `RichTextAlignment` enum — and the output is generated from that tree out of string constants
the emitter owns, with every text run escaped. The security property is worth stating precisely,
because it is what makes a hand-written parser acceptable at all:

> A bug in the tokenizer can produce wrong formatting, and cannot produce an attribute, a tag name or
> a URL that came from the input. The only user bytes that survive are text-node characters, escaped.

Alignment is the one thing that looks like an exception and is not: the parser matches
`text-align` against a closed set of four keywords and then **discards the string**, and the emitter
writes one of four constants. No byte of user input reaches an attribute position.

**No `HtmlSanitizer` dependency.** An HTML→QuestPDF renderer was needed regardless, and that needs a
tree. Adding a library that does half of what must be built anyway, and whose output would then have
to be re-parsed, is more moving parts rather than fewer. The test file is written adversarially
against that decision — the standard evasion families (event handlers, `javascript:` URLs, unquoted
attributes, malformed tags browsers re-interpret, CSS expressions, data: URIs, SVG vectors,
entity-encoded payloads, mXSS through `<noscript>`/`<math>`) — and the blanket property asserted is
not "these payloads are removed" (any blocklist satisfies that one payload at a time) but *the
output's only attribute is the one the emitter writes*. A new evasion technique fails that without
anybody having had to think of it first.

**Idempotence is a product requirement, not a nicety.** A document is loaded into the editor and
saved again on every edit, so a non-idempotent sanitiser rots a field visibly over a few saves —
ampersands doubling, whitespace growing. Asserted over the whole adversarial corpus.

---

## Decision B — the editor's toolbar is what the PDF can draw

Eleven buttons: **Bold, Italic, Underline, Strikethrough | Align left/centre/right/justify | Bullet
list, Numbered list, Horizontal line.**

The reference product offers five more things this does not: text colour, font family, font size,
tables and images. Each is dropped for one reason: `RichTextPdfRenderer` cannot honour it, and the
PDF is the copy a customer actually receives and argues about. An editor offering formatting the
printed copy silently discards is worse than one that offers less.

What the grammar does instead of dropping content:

| input | outcome | why |
|---|---|---|
| `<span style="color:…;font-size:…">` | unwrapped, text kept | the live editor's own colour/size/font markup |
| `<a href="…">` | unwrapped, text kept, href gone | a link's words are content; its destination is an attribute, and no attribute survives |
| `<img>` | disappears | void, so unwrapping removes it |
| `<table>` | one paragraph per row, cells space-separated | a pasted price list is data; dropping it silently would be the worse failure |
| `<h1>`–`<h6>` | `<p>` + `<strong>` | weight kept, scale dropped — a document's own typography owns scale |
| `<script>`, `<style>`, `<iframe>`, `<svg>`, `<math>`, `<template>`, `<noscript>`, … | dropped **with subtree** | the distinction that matters: an unwrapped `<span>` must keep its text and a dropped `<script>` must not leave `alert(1)` as visible prose |

`execCommand` is deprecated and is still the right call: it is the only way to edit a
`contenteditable` selection without shipping a selection engine, every current browser implements it,
and **nothing depends on what it produces** — whatever markup it leaves goes through the client
sanitiser before it becomes the model's value and the server's before it is stored. If a browser drops
it, one file changes.

### The two sanitisers, and the file that pins them to each other

There are two and there have to be: the server's decides what is stored, the client's decides what
the user sees while typing. Without the client's, pasting a coloured table from Word shows a coloured
table, the save returns a plain paragraph, and the field appears to have eaten the content.

`web/src/app/shared/rich-text/rich-text-cases.json` is the contract. It is read by
`rich-text.spec.ts` and linked into `Domain.UnitTests` as an **embedded resource** (so a moved file is
a build error rather than a green test over nothing) — phase-26b's arrangement for `BsCalendar` and
`bs-date.ts`, applied to the second pair of twins.

**It earned its place immediately**: it caught the two halves disagreeing about collapsing runs of
whitespace. The client's `DOMParser` does it natively; the server's tokenizer replaced each
whitespace character with a space and did not collapse runs. Invisible until somebody indents their
markup, at which point the server prints a paragraph pushed halfway across the page. The file
deliberately carries **only well-formed input** — the two parsers reach the tree by different routes
and are not required to agree about repairing malformed markup, because the server's answer is the one
that gets stored.

---

## Decision C — the migration converts, rather than letting two representations coexist

Seven columns held plain text from a textarea (five `Terms`, two template `Body`s). Deciding at
render time which representation a row is would leave both in one column for ever and put the guess on
every future read path. `Phase39RichText` converts once, escaping first and turning newlines into
`<br />`.

**One simplification, stated**: the SQL wraps the whole value in a single paragraph where
`RichText.FromPlainText` splits blank-line-separated runs into separate paragraphs. The two render
identically — a blank line is a blank line either way — and the simpler form is what T-SQL expresses
without a loop. Re-saving through the editor re-canonicalises.

`Down` narrows the column and **leaves the content as HTML**, deliberately: the escaping is
reversible but the paragraph structure is not distinguishable from markup a user has since typed, so a
"reversal" would quietly damage edited rows. The narrowing fails if a body has grown past 4,000
characters, which is the correct behaviour — that is the migration saying the data no longer fits the
shape being asked for, rather than truncating somebody's terms.

`Terms` also had **no validator rule at all** on ten commands, since phase 27b. Survivable while the
control was a textarea; not once it accepts a paste of an entire web page.

---

## Decision D — the profile screen is an addition, and says so

The live pass found no post-creation logo control. Two options followed: match that (logo settable
only at signup, which this codebase's wizard is a single JSON command and cannot easily carry a file),
or add one.

**Added**, on a new `Configurations > Organization Profile` screen, because an ERP whose logo can
never be corrected after signup is a worse product — the same "addition, labelled as one" the phase-38
variant importer is. The nine detail fields are shown **read-only** beside it: a page of nothing but
an upload box is a screen nobody could place, and the fields are exactly what the printed header
prints next to the logo. Editing them is a carried item and the page says so rather than offering
controls that do not work.

**The format check is the dimension check.** `ImageHeader` reads PNG's IHDR, GIF's logical screen
descriptor and JPEG's SOF marker chain — about sixty lines, no pixels decoded, no dependency. The
reference product's rules are "JPG/PNG/GIF, min 300×300, max 5MB"; two of those can be checked from
the declared content type and length, and both of those are client-supplied. A parser that can find
the dimensions has already answered the question that matters. The stored `LogoContentType` comes from
the bytes, so the serving endpoint never echoes a client's claim back to a browser.

The JPEG arm is where this kind of parser is usually written wrong, and the tests say so: DHT (0xC4)
sits inside the 0xC0–0xCF range and is **not** a frame header, and reading it as one yields confident
nonsense rather than a failure.

**Deletion story decided with the feature** (phase-21b Decision E): a replacement deletes the blob it
replaced, and the delete happens *after* the row commits. The other order can leave the organization
pointing at bytes that are gone — a broken logo on every document; this order can at worst orphan one
file, which is invisible.

**The print assertion, not the upload assertion.** `Phase39LogoPrintTests` renders real PDFs and
looks at the bytes: an embedded image XObject with a logo, none without, and no `<strong>` or `&lt;`
anywhere. `/Subtype /Image` rather than `/Image` — the latter appears in every PDF's ProcSet array
whether or not an image is embedded, and the first version of that test passed for the wrong reason.
The renderer re-checks the bytes with the same `ImageHeader`, because QuestPDF throws for an
undecodable image at `GeneratePdf` time — after composition, so there is no try/catch around the draw
call that would help, and an organization whose stored logo has somehow gone bad still needs its
invoices to print.

---

## Decision E — `BalanceConfirmation` is an addition too

Phase 30 offered the context on the template screen because the live picker offers it, and shipped no
action that could select it. The reference product's statement screen still offers **Export | Print**
only, so wiring it is a divergence — but a context with nothing behind it, shipped twice, is worse
than diverging once.

The mechanics turn on one asymmetry: **this is the only context whose attachment its parent does not
identify.** Every other send names a document; a balance confirmation names a *contact*, and the same
contact has a different letter for every as-at date. So:

- `EmailSendLog.BalanceAsOfDate` — one nullable column, with a Domain invariant in **both**
  directions (required for this context, refused for every other).
- The **contact type is derived, not stored**. A contact is a Customer or a Supplier, never both, so a
  stored copy could only ever come to disagree with the row it was copied from. The job reads it.
- A **second method on `IDocumentPdfRenderer`**, not a second interface: the reason the seam exists is
  unchanged (QuestPDF is in the Api layer, a job cannot reach it), and splitting it would mean two
  adapters and two chances for an emailed PDF to stop matching its printed twin.
- No "attach" checkbox. An email whose whole purpose is to state a balance has nothing to send
  without the letter, so a choice would only be a way to send an empty message.

---

## Decision F — Deals and Tasks get their lists; the detail pages are carried

The live detail pages host **Documents, Activity and (for a Deal) Tasks and Contact Personnel** —
which is phase-27a's mechanism sweep over two parent types this codebase does not have. `Deal` and
`WorkTask` would need appending to `AttachmentParentType`, `CommentParentType` and `TaskParentType`,
a third route family beside the Contact and Document ones, permission re-checks per parent, EF
configuration, a migration and guard-test updates.

That is a phase's worth of sweep, and phase 39's own subject is the two editors. The lists ship —
which is what closes 34b's carried item as written ("Deals and Tasks have no standalone routes") — and
the detail pages are named here with what they need, rather than half-built.

What the lists cost was small and one thing was not: `ListTasksQuery`'s parent became nullable, and
the handler composes the parent and search filters as **separate `.Where()` clauses** rather than
folding them into the predicate. An expression tree does not short-circuit, so
`request.ParentId == null || x.ParentId == request.ParentId.Value` hands EF a null to compare on the
unscoped branch — which is *every* caller of the new screen (CLAUDE.md, phase-33).

---

## Decision G — the results page's kind filter is the performance answer

Phase 33 deferred the search results page with a specific re-entry test: worth it once the
per-collection cap of 5 bites. Phase 34c ran it — an ordinary term returns the cap from every
collection on the 50,000-invoice dataset, so the dropdown is a *sample*.

34c measured the same unfiltered fan-out (eighteen queries) at 595–1,106 ms p95, over its own 500 ms
budget in every pass. Narrowing to one collection runs a fraction of that. So the control that makes a
long result list readable is the same one that makes it affordable to produce — which is why the
filter narrows the **query** and not the rendered rows. Filtering client-side would filter an
already-capped sample: identical on a small tenant, wrong on a real one.

The term lives in the url (`?q=`, `?kind=`), which makes a result page shareable, reloadable and
back-button-correct. The `effect()` over the route signals is the right shape precisely because its
source is *external* — phase 34b's warning is about an effect over a signal the same handler just
wrote, and neither half applies to a route parameter nothing in the component sets directly.

---

## Decision H — Quick Links keeps its arrow buttons

Phase 33's carried item #2 asked for drag, because the reference product's tray is a
`sortable-container`. Drag was added; the arrows **stayed**. A drag-only reorder is unreachable from a
keyboard, and the reference product's own control is no excuse for shipping something this codebase's
phase-34a pass would have failed.

One implementation detail is worth a test of its own: dragging **moves**, it does not swap. The
difference only shows over a distance — dragging the sixth tile onto the first should push the rest
down, where a swap would also fling the old first tile to position six — and for the adjacent case the
arrows use, the two are identical, which is why both gestures share one method.

---

## Bugs and findings

### 1. A Domain invariant reaching the API is a 500 (found by the E2E)

`EmailSendLog.Queue` refuses a `BalanceAsOfDate` on a context that has no letter. Correct as an
invariant — and an invariant reached through the API surfaces as `{"title":"An unexpected error
occurred.","status":500}`, which tells a caller nothing and reads to an operator like a server fault.

Fixed by adding the rule to `SendEmailCommandValidator` in both directions, so the failure is a 400
naming the field. **The Domain check stays as the backstop.** This is only visible where the two
layers are exercised together, which is the E2E's job and nothing else's.

### 2. `CreateDealCommand.AssigneeUserIds` had no validator rule at all (pre-existing, phase 15)

A body omitting the array binds it to null and the handler dereferences it: a caller's mistake is a
500. Both the Create and Update validators now carry `NotNull()` — with a message saying what is
meant ("as an empty list if the deal has none"), because FluentValidation's default reads "must not be
empty" and an empty list is legitimate.

Same shape as phase 34b's finding that asking one question of every paginated list turned up two
queries with no validator at all. Found here because the E2E sent the minimal body a person would.

### 3. The search sweep guard could not see the two lists it existed for

`SearchSweepGuardTests` recognised a "paginated list query" as one returning `PagedResult<T>`.
`ListTasksQuery` and `ListDealsQuery` predate that type and return their own
`{Rows, Page, PageSize, TotalCount}` record — so they were invisible to the guard whose whole purpose
is to notice a list nobody gave a search box, and they were **exactly** the two lists this phase had
to give one.

Widened to recognise the envelope by **shape**. That surfaced seven more previously-invisible
queries; all seven are the rule's own exempt case (a panel already scoped to one parent row, or a
sequence whose rows only mean anything in order), and each now carries its reason. `ListSmsLogsQuery`
is named as the first of them that would earn a term, with its re-entry condition, rather than left as
a silent gap.

### 4. The write-path guard's probe filled nullable parameters with `default(T)`

`RichTextWritePathSweepGuardTests` drives each type's factory with a probe value per parameter. For a
`DateOnly?` it passed `default(DateOnly)` — a date, not an absent one — so the new invariant threw and
the guard reported `EmailSendLog.Body` as unsanitised. The fix is in the probe, not the invariant:
optional-and-absent is what a nullable parameter means, so it is what the probe should say.

Worth recording because the guard was right to fail — a factory that throws cannot be shown to
sanitise — and the failure pointed at the wrong file.

### 5. `curl -F name=<value` reads the value as a file path

`-F 'body=<p>Our books show…'` made curl try to read a file named `p>Our books show…`, returning exit
26 and HTTP 000 — which reads exactly like the server fault phase 30 recorded for its own `-F` upload
attempt. `--form-string` is the fix. Phase 30's note ("curl cannot read a file for `-F` upload here")
is about a different failure with the same symptom, which is precisely why this one was confusing.

### 6. The Angular app routes by path, not by hash

The browser pass bounced to Sign In three times against `#/organizations/…`. The cookie was fine and
`/api/auth/me` returned 200 through it; `location.href` was `https://localhost:4200/login#/…` — the
app uses `PathLocationStrategy`, so the hash was a fragment on the login page. The reference product's
own URLs are hash-based, which is what makes this easy to carry over wrongly.

### 7. A quoted heredoc eats backslash escapes (harness, three times)

CLAUDE.md already says not to build files with `cat > file <<'EOF'` in the Bash tool. The reason it
gave was truncation; the reason that actually bit here is escapes. `<<'PY'` suppresses variable
expansion and does **not** suppress this shell's backslash handling, so `"\n"` and `"\t"` inside an
embedded Python string arrived as a literal newline and tab.

The first instance announced itself (`SyntaxError: unterminated string literal`). The second did not:
an MSBuild path `..\..\web\src\app\shared\rich-text\...` became `..\..\web\src` + BEL + `pp\shared` +
CR + `ich-text`, because `\a` and `\r` were interpreted — surfacing several steps later as
`'', hexadecimal value 0x07, is an invalid character` from the XML parser, in a file whose generator
looked correct.

The third instance is why it is written down rather than filed as bad luck: the first draft of the
`known-gotchas.md` section documenting this was itself appended through a heredoc, and every escape
in it was eaten.

---

## E2E (fresh Organization on real SQL Server, every status printed)

Seeded through the API on the standing test identity: organization, contact, unit, product category,
Service product, warehouse, five account groups, fifteen accounts, GL defaults.

| step | result |
|---|---|
| `POST /invoices` with `<script>`, `onclick=`, `javascript:` and `<b>` in Terms | **201** |
| `GET /invoices/{id}` — the read path | `<p>Payment due in <strong>30 days</strong>.</p><p>Late fees apply.</p><p>terms page</p><ul>…</ul>` |
| `sqlcmd` on `sales.Invoices.Terms` — the column itself | **byte-identical to the read**; no script, no attribute, no `javascript:` ever stored |
| `POST /logo` a 400×400 PNG | **200** |
| `POST /logo` a 299×300 PNG | **400** — "must be at least 300×300 pixels. This one is 299×300." |
| `POST /logo` a `<svg><script>` named `logo.png`, typed `image/png` | **400** — "not one of those, whatever its name or type says" |
| a rejected upload | leaves the existing logo intact (`hasLogo=true`) |
| `GET /print/Invoice/{id}` with a logo | 44,421 bytes, **1** image XObject, **0** raw markup |
| the same with the logo removed | 40,017 bytes, **0** image XObject |
| `GET /emails/prepare?…&context=BalanceConfirmation` | context = `BalanceConfirmation` |
| the same parent with **no** context | context = `General` — the Contact page's own send, unchanged |
| `POST /emails` (BalanceConfirmation) → `communications.EmailSendLogs` | `BalanceConfirmation \| 2026-09-13 \| **Sent** \| <p>Our books show <strong>1,695.00 DR</strong>.</p>` — the job rendered the letter, delivered it, and the `<script>` in the body never reached the row |
| `POST /emails` with a date and no such context | **400** naming `BalanceAsOfDate` (was a 500 — finding #1) |
| `GET /tasks` with no parent | **3** tasks, across Organization, Contact and Invoice parents |
| `GET /tasks?parentType=Contact&parentId=…` | **1** — phase 13's behaviour, unchanged |
| `GET /tasks?search=auditor` / `=nothingmatches` | **1** / **0** |
| `GET /tasks?search=<101 chars>` | **400** — "Search must be 100 characters or fewer." |
| `GET /deals` / `?search=retainer` | **2** / **1** |
| `GET /search?term=0001` / `&collection=Document` | 4 hits across 4 collections / **1** |

**Negative #1 — the behaviour fires before the handler.** `POST` and `GET` on the logo of an
organization that does not exist, as a full Admin:
`403 "You do not have permission to perform this action (Tenancy.Organization.ProfileManage)"` and the
same for `…ProfileView`. A 403 rather than a 404 against a nonexistent id is what proves
`AuthorizationBehavior` ran first.

**Negative #2 — both directions on one real Member** (phase-31 rule (d)). A second user registered,
verified through `[identity].VerificationCodes`, invited as system Member and accepted:

- `GET /profile` → **200**, reads the organization's name — `OrganizationProfileView` is Admin+Member.
- `POST /logo` → **403** naming `Tenancy.Organization.ProfileManage`.
- `DELETE /logo` → **403** naming the same key.

That is the permission split this phase decided, proven on one identity in both directions rather
than inferred from a seed table.

**Browser pass** (dev cert, `erp-web-ssl`, transplanted `erp_auth` cookie with `SameSite=None;
Secure`):

- The approved invoice's Terms render as **formatting** — bold, paragraphs, a bulleted list — on the
  read-only grey surface, with no toolbar. The `<a>` is plain text and the script is gone.
- A Draft quotation's editor shows **eleven buttons** in order: B I U S | four alignments | bullet,
  numbered, rule. Typing works; `execCommand('bold')` produces `<b>`, and **on blur the control
  re-renders from the sanitised value** — `<b>` → `<strong>`, a `style`d div → a plain paragraph, a
  `javascript:` link → plain text, an injected `<script>` gone. The user sees what will be stored
  while still looking at the field.
- The **Send Email dialog carries the same control with the same eleven buttons** — one component,
  two hosts, as the live pass showed the reference product does.
- `Workflow > Tasks` and `CRM > Deals` render as real screens and **appear in the left nav**, which
  derives itself from the router (34b) — no nav edit was needed for either.
- The search results page: "Everything" shows 4 records across 4 collections; clicking **Documents**
  narrows to the one invoice, driven by the url.
- `Configurations > Organization Profile` shows the uploaded logo, the rules line, Replace/Remove, and
  the nine read-only details.

---

## Carried items

1. **Deal and Task detail pages.** Decision F. They need `Deal` and `WorkTask` in the three
   polymorphic parent enums, a third route family, per-parent permission re-checks, EF configuration,
   a migration and guard updates — phase-27a's sweep over two new parent types.
2. **The nine Organization detail fields are read-only.** The reference product's EDIT DETAILS dialog
   edits all nine; this phase built the logo because that was the roadmap's item. There is no
   `UpdateOrganizationCommand` at all — the aggregate has been create-only since phase 1b.
3. **The printed header's wider shape still diverges.** Live centres the organization block beside the
   logo and centres the document title below; this codebase keeps org-left / title-right. Cosmetic,
   and changing it touches the one shared layout for all fifteen types.
4. **`ListSmsLogsQuery` has no search term**, exempted with its re-entry condition — the first of the
   seven the widened guard uncovered that would earn one.
5. **The rich-text grammar carries no tables or images.** Decision B. Re-entry: a tenant who needs a
   table in their terms, which would mean teaching `RichTextPdfRenderer` to draw one first.
6. **`customtags`** — the reference editor's one toolbar entry that renders no button. Probably a
   merge-field inserter; unresolvable without a tenant where it works.
7. **The client and server sanitisers may disagree on malformed markup.** Deliberate and stated in
   `rich-text-cases.json`: the server's answer is the one that gets stored, and pinning repair
   behaviour would pin two HTML parsers to each other rather than to a contract.
8. **Two `NG8113` warnings** (`DatePipe` unused in `ChequeRegisterPage` and `AllocatePaymentPage`)
   remain — 34a's carried item #3, on phase 40's list.
