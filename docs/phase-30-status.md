# Phase 30 — Communications: outbound email, SMS medium, email logs

**FR-11.1, FR-4.5's Email Logs. Roadmap heading "30. Communications".**

## TL;DR

A **Send Email** dialog on six document types (seven screens) and the Contact detail page, an
**Email Logs** tab with real data behind it, an **Email Templates** configuration page, and
`AlertMedium.Sms`. Every send goes through a claim-then-act ledger (`EmailSendLog`) and a fourth
background job.

**Five things worth carrying forward.**

1. **The confirm-live pass corrected the roadmap twice and phase 27b once.** Send Email is on **6 of
   15** document types, not all of them; it is *not* on the Contact statement report but it *is* on
   the Contact detail page; and phase 27b's "Invoice, Credit Note and Customer Payment" was a
   three-sample inference over a set more than twice that size. The real rule is a rule, not a list:
   **Send Email exists exactly where an email template can be scoped** — asserted in both directions
   by a guard test.
2. **A shared UI panel is not evidence of a shared model** (Decision B). The reference product shows
   email templates inside its Custom Templates panel, which is what put `CustomTemplateType.Email`
   into the codebase in 27b — but it serves them from a different resource, with six extra fields, a
   disjoint type vocabulary, and a type that is immutable after creation. `EmailTemplate` is its own
   aggregate and the dead enum member is **deleted**, not left looking like a feature.
3. **"We already have the interface" measures the wrong thing** (Decision G). Phase 20e predicted
   `AlertMedium.Sms` would be "one enum member and a branch" because `ISmsSender` already existed. It
   was four changes — recipients change meaning, the subject stops being meaningful, and it *spends
   money* through phase 18's credit ledger. The cost was in what the surrounding aggregate assumed
   about the one medium it had.
4. **The rule for whether a background job needs an identity is not "does it write?"** (Decision H).
   It is **"does it send a MediatR request?"** This job only reads, so phase 20e's no-identity default
   looked like it applied — but it renders the attached PDF through the permission-gated
   `PrintDocumentQuery`, and a MediatR request with no acting user fails authorization. First job in
   the codebase to need `IJobActingUser` for a purely read-only reason.
5. **Do-exactly-once and "a resend is a new row" are compatible if the key is an intent** (Decision
   D). Not an occurrence (there is no schedule) and not a content hash (that would silently swallow a
   legitimate second send); a **request id minted when the dialog opens**. Proven both ways in SQL.

Also: `EmailSendLog` carries a **rowversion, which phase 21a's rule forbids on `ImportJob`/`ExportJob`**
— because that rule protects rows with *two* writers, and this row has one (Decision I).

---

## Step 1 — Confirm-live pass (2026-09-05, Moonbeam UAT tenant, read-only)

The user signed in; the pass was read-only apart from expanding two collapsed panels and opening
two dialogs, none of which was saved or sent. **No email was sent from the reference tenant** — see
"The one write we did not do" below for why that was the right call and what it costs us.

Four questions were open. Three were answered in full and the fourth only in the negative. Two of
the three **changed the plan**.

### 1. Email templates are not `CustomTemplate`s — they are their own resource, scoped to a document

Configurations > Apps > Custom Templates (`#/config/app/custom-templates`) renders **four collapsed
panels**: Customer Balance Confirmation, Supplier Balance Confirmation, Terms and Conditions, and
**Email**. That grouping is what phase 27b saw, and it is what put `Email` into
`CustomTemplateType`. The grouping is a UI convenience and **the data model underneath is a
different shape**:

- The other three panels fetch `GET /erp/custom-templates?type=terms_and_conditions` (etc.).
  The Email panel fetches **`GET /erp/email-templates`** — a separate resource entirely.
- An Email template's editor (`#/config/app/custom-templates/edit/<id>`) carries **six fields the
  other three do not have**: `Template Type *` (the document context), `Reply to *`, `CC`, `BCC`,
  `Subject *`, and a rich-text (TinyMCE) body. A Terms template is a name and a body.
- **`Template Type` is disabled on edit** — an Email template's document scope is fixed at
  creation. `CustomTemplate.Update` deliberately *allows* a Type move (clearing `IsDefault`), so the
  two aggregates do not even share an update invariant.

**The Template Type vocabulary is disjoint from `CustomTemplateType`.** The create form's picker
offers exactly eight, read off the option `title` attributes:

| # | Template Type |
|---|---|
| 1 | Invoice |
| 2 | Quotation |
| 3 | Sales Order |
| 4 | Credit Note |
| 5 | Customer Payment |
| 6 | Supplier Payment |
| 7 | Purchase Order |
| 8 | Balance Confirmation |

Older rows on this tenant additionally carry **General**, **Statement**, **Delivery Note** and
**Goods Received Note** contexts (`Payment Reminder / General`, `Statement of Account / Statement`,
and two notification templates whose context label renders blank because those document types are
unused here). So the picker's eight are the *currently offered* set, not the historical one.

### 2. `Send Email` appears exactly where an Email template context exists

It is **not** in the document's `OPTION` kebab (that holds Edit / Make Duplicate / Void / Create
Credit Note / Print). It is an inline action in the **Details card header**, immediately left of
`View Print Preview`.

Probed one real approved document per type:

| Screen | Send Email | View Print Preview |
|---|---|---|
| Invoice (`#/sales/invoices/…`) | **yes** | yes |
| Quotation (`#/sales/quotations/…`) | **yes** | yes |
| Sales Order (`#/sales/orders/…`) | **yes** | yes |
| Credit Note (`#/sales/credit-notes/…`) | **yes** | yes |
| Customer Payment (`#/sales/payments-received/…`) | **yes** | yes |
| Supplier Payment (`#/purchases/payments-made/…`) | **yes** | yes |
| Purchase Order (`#/purchases/orders/…`) | **yes** | yes |
| Purchase Bill (`#/purchases/purchases-bill/…`) | no | yes |
| Debit Note (`#/purchases/debit-notes/…`) | no | yes |
| Expense (`#/purchases/expenses/…`) | no | yes |
| Journal Voucher (`#/accounting/journal-voucher/…`) | no | yes |

**Seven documents, and the seven are exactly the seven document contexts in the Template Type
picker** (the eighth, Balance Confirmation, is a letter rather than a document). That is the rule,
and it is a much better rule than a list: *Send Email exists precisely where an Email template can
be scoped.* Cash Transfer, Warehouse Transfer, Inventory Adjustment, Production Order and
Production Journal were not probed individually — none of them has a Template Type, so the rule
puts them in the "no" column, and probing four more pages to re-derive a rule already proven on
eleven was not worth the session budget.

This **corrects phase 27b**, which recorded Send Email as present "only on Invoice, Credit Note and
Customer Payment". It sampled three and inferred; the real set is more than twice that.

### 3. The Contact detail page has Send Email — the Contact *statement report* does not

The roadmap said "that dialog on every printable document and on the Contact statement". Neither
half is right:

- **Not every printable document** — 7 of 15, per the table above.
- **Not the statement report.** `#/reports/new/customer-statement`'s `OPTION` menu holds exactly
  **Export | Print**. No Send Email.
- **But the Contact detail's Overview tab does**, in a row of actions reading
  `Send Email | +SMS | Export Options` — i.e. immediately beside phase 18's SMS action, which is
  where FR-4.5 would expect it.

The Contact dialog differs from a document's in two ways: its Template picker offers only the
**General**-context templates (just `Payment Reminder` on this tenant), and it has **no
"Attach … PDF" checkbox at all** — there is no document to attach. It keeps the extra-files drop
zone.

### 4. The dialog resolves merge fields before you see them

`New Email` is an ant-design **bottom drawer**, not a modal. Fields, in order:

| Field | Live behaviour |
|---|---|
| `Template: *` | Pre-selected to the context's default. The picker offers **only** templates whose Template Type matches this screen — on invoice 045 it listed exactly one, `Invoice Notification`. |
| `To: *` | Empty tag input + a **`More...`** picker (SELECT ALL / DESELECT ALL / list / Reset to default / APPLY) over the contact's known addresses. On this contact it read **"No data found"** — the contact has no email on file, which is also why `To` did not prefill. |
| `CC` / `BCC` | Two more tag inputs, collapsed behind their labels. |
| `Reply To: *` | Pre-filled with the signed-in user as a removable chip (`demo@tiggapp.com ×`). On the *template* create form the same field also defaults to the user. |
| `Subject: *` | **Merge fields already substituted**: `Invoice From Moonbeam Trading and Suppliers`, from the template's stored `Invoice From $[ORGANIZATION_NAME]$`. |
| body | TinyMCE, editable, **fully substituted** (see below). |
| `Attach Invoice PDF` | A checkbox, **checked**. Absent on the Contact dialog. |
| drop zone | `Drag and drop or click here to upload files` over a real `<input type=file>`. |
| `Send Email` | Not disabled with `To` empty — validation fires on click, not on render. |

**Merge syntax is `$[TOKEN]$`** — the same convention `SmsTemplate` established in phase 18, and
the same one `CustomTemplate`'s doc comment claims by documentation convention. (It renders as
`${TOKEN}` at a glance in a screenshot; the input `value` is unambiguous.)

Stored template body, read out of the TinyMCE iframe on the edit screen:

```
Hello $[CUSTOMER_NAME]$,

Your invoice $[INVOICE_NO]$ from $[ORGANIZATION_NAME]$ is now available for your review.
…
Invoice Number: $[INVOICE_NO]$
Invoice Date: $[INVOICE_DATE]$
Due Date: $[DUE_DATE]$
Invoice Amount: $[CURRENCY]$ $[GRAND_TOTAL]$
…contact us at $[ORGANIZATION_PHONE]$ or reply to this email.

Best regards,
$[USER_NAME]$
$[ORGANIZATION_ADDRESS]$
$[ORGANIZATION_PHONE]$
```

The same body, in the dialog on invoice 045:

```
Hello Adhitya Bhandari,

Your invoice 045 from Moonbeam Trading and Suppliers is now available for your review.
…
Invoice Number: 045
Invoice Date: 02-09-2026
Due Date: 02-09-2026
Invoice Amount: NPR 50,850.00
…contact us at 9705056788 or reply to this email.

Best regards,
demo@tiggapp.com
Manbhawan
9705056788
```

Three things fall out of that pair, and all three matter:

1. **The dialog previews resolved values, and they are editable.** What the user sends is the
   document's own text, seeded from the template — not a template reference resolved later. This is
   phase 27b's Terms and Conditions decision arriving a second time, on a second mechanism.
2. `$[USER_NAME]$` fell back to the user's **email** when no display name was set, and
   `$[CURRENCY]$ $[GRAND_TOTAL]$` rendered as `NPR 50,850.00` — currency code, thousands separators,
   two decimals.
3. Dates render **AD `dd-mm-yyyy`**, not BS, even though this tenant renders BS elsewhere.

### The merge-field catalogue

The body editor's `Custom Tags` toolbar menu is the authoritative list. **Four groups**, three fixed
and one per document context:

| Group | Fields |
|---|---|
| **Organization** | Name, Display Name, Address, Phone, Email, Website, Pan |
| **Contact** | Name, Address, Phone, Email, Pan |
| **User** | Name, Phone No, Email, Address |
| **Invoice** *(the context group)* | Customer Name, Invoice Reference, Invoice No, Invoice Date, Transaction Date, Due Date, Currency, Exchange Rate, Sub Total, Transaction Discount, Non-Taxable Total, Taxable Total, VAT, Grand Total, Invoice Note, Payment Mode, Payment Reference, Payment Amount |

### 5. Email Logs: the tab exists, keyed polymorphically, and holds nothing

- A **Contact**'s Activity tab has four sub-tabs: Comments / Activities / SMS History / **Email
  Logs**.
- A **document**'s Activity tab has three: Comments / Activities / **Emails** — note the different
  label for the same thing.
- Both fetch the same endpoint with a different source:
  `GET /erp/email-logs?source=Contact&source_id=<guid>` and
  `GET /erp/email-logs?source=Invoice&source_id=<guid>`.

So the reference product's email log is **one polymorphic `(source, source_id)` table**, the same
shape as its `activity-logs` — and the same shape phase 27a gave `Comment` and `Attachment` here.

**Both tabs are empty on this tenant** ("No Emails Available"), on every document and contact
checked. The Alert Scheduler panel that phase 20e found an "Email Logs" view behind no longer
exposes one: its rows' kebab holds Edit / Delete / Mark As Inactive and nothing else.

**So the log's column set could not be read.** It is the one thing this pass could not answer.

### The one write we did not do

Reading the log's columns, the send's synchronous-or-not feel, and any resend affordance all
required actually sending mail from the reference tenant to a third party. That was offered and
**declined** — deliberately, because every decision it would have informed can be made on evidence
already in hand:

- the log's columns are recoverable from the dialog's own field set (template, to/cc/bcc, reply-to,
  subject, attachment flag) plus `AlertSendLog`, which is this codebase's own already-shipped,
  already-reasoned email ledger;
- the sync-vs-async decision (Decision A) turns on SMTP latency and on the ledger this codebase
  already has, not on how another product's drawer feels;
- "is there a resend?" is answered by the dialog being reachable again at any time, and the
  roadmap already fixes the semantics — *a resend is a new row, never a retry*.

This is phase 29's lesson holding a second time: **check what the tenant can tell you read-only
before asking to write.** There, two already-approved bills settled the whole question. Here the
read-only surface settled three of four, and the fourth was worth less than the send.

### Navigation notes (for the next session)

- Hash routes confirmed this pass: `#/config/app/custom-templates`,
  `#/config/app/custom-templates/edit/<id>`, `#/config/app/custom-templates/create`,
  `#/config/app/alert-scheduler`, `#/sales/orders/<id>`, `#/purchases/orders/<id>`,
  `#/reports/new/customer-statement`. **`#/sales/sales-orders` and `#/purchases/purchases-order`
  are wrong** and silently redirect to the Customers / Suppliers list — the real slugs drop the
  module prefix (`orders`, not `sales-orders`).
- The app's cards and menu items are `div`s with React handlers and no accessible role, so `find`
  matches nothing and `read_page` is thin. Driving it means `querySelectorAll` + dispatching the
  full `pointerdown, mousedown, pointerup, mouseup, click` sequence — a bare `.click()` is ignored
  by several of its controls (the template card kebab, the popover items).
- The Browser pane's screenshot coordinate frame (800×531) is **not** the page's CSS pixel frame, so
  `getBoundingClientRect` coordinates cannot be fed to `computer{left_click}`. Click through
  JavaScript, or take a screenshot and read coordinates off the image.
- `javascript_tool` hard-fails at 45s, so a probe loop over more than two page navigations must be
  split across calls; parking the helper on `window.__p` between calls works well.

---

## Step 2 — Scope decisions

### Decision A — an outbound send is a background job, not a synchronous call

The roadmap asked this first, and recommended the job. It is the job, and the live dialog's own
feedback turned out **not** to be the deciding input — the tenant had never sent an email, so there
was nothing to observe. The decision rests on this codebase instead:

- **SMTP latency is not the user's problem.** A send is a TCP connect, a TLS handshake, an AUTH and a
  DATA round trip to a third party. Holding an HTTP request open across all four means the Send
  button spins for seconds and times out when the provider is slow.
- **A failure is an outcome, not an error.** A synchronous send has to decide what to do when SMTP
  refuses: fail the request and lose the composed message, or succeed and lie. The job records
  `Failed` with the reason, the message survives in the log, and the user sees it in the Email Logs
  tab.
- **The ledger already exists.** Phase 20e's claim-then-act idiom and phase 21b's shared runner host
  are both already built and already reasoned about. This is the fourth background job and the third
  to ride `QueuedJobRunnerHostedService`; it cost one options class and two registration lines.

**What the job costs, stated plainly**, because it is not free: the extra files a user drops on the
dialog have to be persisted rather than streamed, since the request's streams are long gone by the
time the runner reads them. That is Decision E, and it is the one real consequence.

### Decision B — email templates are their own aggregate, and `CustomTemplateType.Email` is deleted

Phase 27b added `CustomTemplateType.Email` and left it unconsumed, expecting phase 30 to be its
consumer. **The confirm-live pass says it cannot be** (Step 1.1). Four facts, any two of which would
be enough:

| | `CustomTemplate` | `EmailTemplate` |
|---|---|---|
| Served by | `/erp/custom-templates?type=…` | `/erp/email-templates` — a different resource |
| Fields | name, body | name, body, **type, subject, reply-to, cc, bcc** |
| Type vocabulary | 4 kinds of letter | 9 document contexts — **disjoint** |
| Type after create | mutable (clears `IsDefault`) | **disabled on the edit form** |

Folding them together means six columns that are always null on three-quarters of the rows, plus a
second enum meaningful for one member of the first — the exact shape `ExportJob`'s Decision C
rejected for `ImportJob`. So `EmailTemplate` is its own aggregate.

**And the dead member is removed rather than left.** A `CustomTemplateType.Email` that no screen
offers and no code reads is precisely the rot phase 27a built `DocumentMechanisms` to prevent: it
would sit there looking like a feature, creatable through the API, doing nothing. The migration
deletes any row carrying it — such a row is a template that could never have been used for
anything — and the Configurations panel loses its Email section to a dedicated **Email Templates**
page.

This is the second time a phase-27b assumption has been corrected by opening the screen (the first
was Send Email's document coverage, above). The pattern is worth naming: **a shared UI panel is not
evidence of a shared model.**

### Decision C — `EmailSendLog` is its own table, next to `SmsLog` and `AlertSendLog`

The roadmap asked whether email and SMS logs are one polymorphic "message log" or two, and named
phase 18's Decision #2 as the precedent for checking before merging. Checked; they are two, and
there are now three logs rather than two:

| | `SmsLog` (18) | `AlertSendLog` (20e) | `EmailSendLog` (30) |
|---|---|---|---|
| Written | **after** a successful send | **before** SMTP | **before** SMTP |
| Key | none (a history row) | (definition, occurrence date, recipient) | (organization, **request id**) |
| Status | none — its existence is the success | Pending/Sent/Failed | Queued/Sending/Sent/Failed |
| Parent | `ContactId` | an `AlertDefinition` | polymorphic `(ParentType, ParentId)` |
| Granularity | one row per recipient | one row per recipient | **one row per send** |

`SmsLog` has no status at all, because phase 18 writes it only on success — merging would mean
adding a status column that is always `Sent` on every historical row. `AlertSendLog`'s key is an
*occurrence*, which is what makes a scheduled alert idempotent; a user-initiated send has no
occurrence and must not have one. Three tables, three genuinely different questions.

**One row per send, not per recipient**, is the sub-decision worth stating: a user composes one
message with a To, a CC and a BCC. Splitting it into three rows would report one action as three and
make the CC list unreconstructable.

### Decision D — idempotency is a client-minted request id, not a content hash or an occurrence

The exit bar asks for do-exactly-once, and the roadmap fixes the semantic that **a resend is a new
row, never a retry**. Those two pull in opposite directions unless the key is chosen carefully:

- an *occurrence* key (20e's) cannot exist — there is no schedule;
- a *content* hash would make a deliberate second send of the same invoice to the same person
  silently do nothing, which is wrong: a customer who says "I never got it" is asking for exactly
  that;
- a **request id minted when the dialog opens** separates the two cases exactly. One opened dialog is
  one intent; submitting it twice (a double-click, a client retry of a response it never saw) is one
  email. Reopening mints a fresh id, and that resend is a new row.

Enforced by a unique index on `(OrganizationId, RequestId)`, with a cheap pre-read in front of it for
the sequential case. Proven both ways in Step 3.

### Decision E — dropped files are stored, not filed as `Attachment`s, and are released at terminal status

Decision D made storing them mandatory. Two questions followed.

**Not `Attachment` rows.** The drop zone sits inside a compose window, beside "Attach Invoice PDF".
Routing drops into `Attachment` would make them appear on the document's Documents tab, where a user
who attached a signed slip to one email would find it filed as a permanent document of record they
never filed. Phase 18's Decision #2 is the precedent for checking that two `(ParentType, ParentId)`
shapes are the same *concept* before reusing one; they are not.

**Released at terminal status** (phase-21b Decision E: a feature that writes a blob decides its
deletion story in the same phase). The blob exists for exactly one reason — the job must read it
after the request that received it ended — and once the send is `Sent` or `Failed` that reason is
gone. The processor deletes the bytes, then stamps the row; blob first, row second, so a failure
leaves a harmless orphan rather than a row promising bytes that are not there. **File names
survive**, so the log can still say what went out. A resend re-uploads, which costs nothing, because
the dialog is a fresh compose either way.

### Decision F — permission keys: three keys and two different answers

Derived per feature, not defaulted, and this one deliberately **departs from the nearest precedent**.

- **`Configuration.EmailTemplate.View` / `.Manage` — Admin-only**, sitting exactly where
  `Configuration.CustomTemplate.*` and `Configuration.PrintingTemplate.*` already sit. A template
  fixes the words this organization says in its own name, and its default BCC list silently copies
  every future send to whoever it names. That last property is decisive: a BCC nobody notices is the
  shape of a data leak, and it is configured here, not at send time.
- **`Communication.Email.Send` — Admin *and* Member.** `Crm.Sms.Send` is Admin-only and the obvious
  move was to match it. The reason that key is Admin-only is **scale**: one `SendSmsCommand` can
  address every contact in the tenant (`SmsAudienceMode.All`). This one cannot address anyone the
  caller has not typed, about a document the caller cannot already open. Emailing a customer their
  own invoice is the standing rule's "bounded, routine daily-use working data" half, not its "flat
  register exposing contact identity" half — and Admin-only would mean a salesperson who may create
  and approve an invoice may not send it, which is a broken feature rather than a security posture.
- **`Communication.EmailLog.View` — Admin and Member.** It shows what was already sent about a
  document the reader can already open. A Member who may send but cannot see whether the send failed
  is worse on both usability and safety.

**The bound on Send is enforced, not assumed.** The key alone gates almost nothing; the handler
re-checks the *parent's own View key* once the parent is known, throwing the identical
`ForbiddenException` shape — phase-27a's `AttachmentAccess` two-layer pattern. So emailing an invoice
really requires `Sales.Invoice.View` **and** `Communication.Email.Send`, and a role can be denied
sending outright while keeping every document it can read. Both halves are proven live in Step 3.

### Decision G — `AlertMedium.Sms` was not "one enum member and a branch"

Phase 20e predicted it would be, since `ISmsSender` had existed unused since phase 18. The roadmap
asked to verify that before assuming it. **It was wrong, and the shape of the miss generalises:**

1. the enum member;
2. **recipients change meaning** — `AlertDefinition.Recipients` is validated as email addresses, and
   an SMS alert needs phone numbers, so validation switches on the medium rather than being fixed;
3. **an SMS has no subject**, so `AlertContent`'s two halves stop being uniformly meaningful and the
   dispatcher must not prefix the body with the alert's internal name;
4. **it spends money.** Phase 18 debits one `SmsCreditLedgerEntry` per recipient. A scheduled SMS
   that did not debit would make the credit balance a lie; one that cannot afford itself has to fail
   *visibly in the send ledger* rather than throwing inside a timer tick, where nobody would see it.

Four changes. The generalisable lesson is that "we already have the sender interface" measures the
wrong thing — the cost was in everything the *surrounding* aggregate assumed about the one medium it
had.

`AlertSendLog` gained a `Medium` column for the same reason it already denormalises `AlertType`:
without it a `Recipient` is ambiguous between an address and a phone number, and the Email Logs
screen would silently list SMS sends.

### Decision H — the job assumes the sender's identity, and that is not optional

The obvious reading is that this job only reads, so phase 20e's "a background job needs no ambient
identity" default applies as it did for phase 21b's exporter. **That reading is wrong here**, and it
is worth writing down because it is easy to get backwards.

The exporter reads through org-filtered queries *it owns*. This job renders the attached PDF through
`PrintDocumentQuery` — a permission-gated MediatR request — precisely so an emailed PDF cannot drift
from a printed one. A MediatR request with no acting user fails `AuthorizationBehavior`. So the
choice is between duplicating the print pipeline and naming a user, and duplicating a
fifteen-document pipeline to avoid naming a user is the worse answer by a distance.

Phase 21a's `IJobActingUser` is exactly the mechanism, and what made it defensible there makes it
defensible here: the id is read off the row the runner just claimed, never from anything
client-supplied, and it names a user who was authenticated and permission-checked by a real HTTP
request. It also buys something real — the check re-runs at *render* time, so a sender who lost
access to the invoice between pressing Send and the runner picking it up gets a recorded failure
rather than a mailed document they may no longer read.

**This is the first job in the codebase to need an identity for a purely read-only reason.** The rule
that actually predicts it is not "does the job write?" but **"does the job send a MediatR request?"**

### Decision I — a rowversion on the send row, which phase 21a's rule forbids elsewhere

`ImportJob` and `ExportJob` deliberately carry no concurrency token, because phase-21a's bug 1 showed
a cancel wedging a running import: two legitimate writers, and a token bumped by either invalidates
the other. **`EmailSendLog` has exactly one writer after creation** — nothing edits a send, and a
resend is a new row by design — so the conflict cannot arise, and the token buys real
compare-and-set that those two had to do without. Recorded because the rule reads like a blanket ban
and is not one; the question is how many writers the row has.

---

## Step 3 — Manual E2E

Against a fresh organization (`phase30-comms-*`), master data seeded by `curl` + cookie jar with
**every status code printed**, and delivery pointed at a local file-drop sink
(`Email:DeliveryMode=FileDrop`) so a real send could be proven **without mailing a real person**.

### What the seed script cost, and what that says

Six request shapes were wrong on the first run, and every one was caught only because the script
prints statuses:

| Trap | Symptom |
|---|---|
| `POST /api/organizations` returns `organizationId`, not `id` | phase 29's, already recorded — avoided |
| contact personnel is `/contacts/{id}/contact-personnel` | 404 |
| a Product needs a `categoryId` **and** a `primaryUnitId` | 400 |
| `VatRate` is `ThirteenPercentVat`, not `Thirteen` | 400, as an unreadable JSON bind failure |
| a fresh org has **no chart of accounts** — approve needs a Default Sales Account | 409 |
| a Goods line consumes stock whether or not the product tracks inventory | 409 oversell |

The last one is a genuine finding rather than a seeding mistake: `ApproveInvoiceCommandHandler`
selects `goodsLines` by **`ProductType == Goods`**, not by `TrackInventory`, so a Goods product on a
tenant without the Track Inventory feature can never be invoiced — opening stock is refused (403,
feature off) and approval is refused (409, oversell). The E2E used a **Service** line to get past it.
Recorded as a follow-up; it is not phase 30's to fix.

### What was proven

**A send, end to end.** `POST /emails` (multipart) → 202 → the runner picked it up → a real `.eml`
landed in the sink. Its MIME structure:

```
To: adhitya@example.test, accounts@example.test
Cc: boss@example.test
Bcc: audit@example.test
Reply-To: sales@phase30.test
Content-Type: multipart/mixed
  ├── text/html            <p>Hello Adhitya Bhandari,</p>…
  ├── application/pdf      filename=Invoice_0004.pdf     ← rendered by the job, from the print pipeline
  └── text/plain           filename=slip.txt             ← the dropped file
```

That single file proves the parts most likely to be wrong: **BCC is a real BCC** and not folded into
To; the **PDF was rendered inside the background job**, which exercises `IDocumentPdfRenderer`,
`MediatorDocumentPdfRenderer`, `IJobActingUser` and `PrintDocumentQuery`'s own permission check all
at once (Decision H); and the dropped file survived the round trip through `IFileStorage`.

**Merge resolution matches the reference product.** `GET /emails/prepare` returned the template
resolved: `$[INVOICE_NO]$` → `0004`, `$[CURRENCY]$ $[GRAND_TOTAL]$` → `NPR 56,500.00`,
`$[INVOICE_DATE]$` → `02-09-2026`, `$[USER_NAME]$` → the sender, `unresolvedTokens: []`. Note
`$[INVOICE_NO]$` resolved although the catalogue only *offers* `DOCUMENT_NO` — the alias mechanism
working as designed.

**Do-exactly-once, both directions**, in SQL:

```
--- rows per RequestId (the 1F77 id was submitted twice) ---
RequestId                            |Rows_
7B9DBC54-27D8-4DDF-BA1F-36CA25CFD624 |1
1F779BAE-CB38-4FFF-A263-539C8C68CD7B |1      <- submitted twice, one row
```

The duplicate submit carried a *different* To, subject and body and still returned
`{"alreadyQueued": true}` with the **first** row's id — the first intent wins, which is the correct
reading of a double-click. The index behind it is present and unique:

```
IX_EmailSendLogs_OrganizationId_RequestId | is_unique 1
```

**Terminal status and blob release**, in SQL:

```
Status|To                                        |Cc               |Bcc                |Pdf|Completed
Sent  |adhitya@example.test,accounts@example.test|boss@example.test|audit@example.test |1  |set

FileName|SizeBytes|Blob    |PurgedAt
slip.txt|21       |released|set
```

The name survives, the bytes do not — Decision E, demonstrated.

**The negative path, and its control.** A Member-role user (registered, verification code read from
`[identity].VerificationCodes` **with the brackets**, invited onto the system Member role, accepted):

```
PUT /email-templates/00000000-…-0000000000ff   -> 403
  "You do not have permission to perform this action (Configuration.EmailTemplate.Manage)."

GET /emails/prepare?parentId=00000000-…-0000000000ff  -> 404  "Invoice not found."
```

The 403 names the exact key and comes back for a **nonexistent id**, proving
`AuthorizationBehavior` fired before the handler ran. The second line is the part worth keeping: the
*same* user gets a **404** on the send path, which proves `Communication.Email.Send` really is
granted to Member. Decision F's split is not just asserted in a comment — both sides of it are
demonstrated in one pair of requests.

### Browser pass

Driven through the `erp-web-ssl` profile with the auth cookie transplanted via `document.cookie`
(phase-25 Step 3's recipe). On the invoice detail:

- **Send Email** renders beside Print, and opens the drawer.
- The drawer arrives populated: Template pre-selected to the default, To pre-filled with the
  contact's own address as a removable chip, **BCC auto-expanded** because the template carries one,
  Reply To defaulted to the signed-in user, Subject and body **fully resolved**, Attach Invoice PDF
  checked, drop zone present.
- **More…** offered `accounts@example.test` — the contact personnel's address — correctly excluding
  the one already chosen. Clicking it added a second chip.
- Send closed the drawer; the row appeared in the ledger with both recipients.
- The **Email Logs** tab, which phase 27b shipped as an empty-state message with a pager and no
  backend, now renders three rows: recipients with CC/BCC beneath, subject, attachments
  (`Document PDF`, `slip.txt`), sender, and a `Sent` badge, with the pager reporting 1–3 of 3.

---

## What shipped

**Domain**
- `EmailTemplate` + `EmailTemplateContext` (9) + `EmailTemplateContexts` (the `(DocumentType,
  PaymentDirection)` → context map, by switch, never an ordinal cast).
- `EmailSendLog` + `EmailSendStatus` + `EmailSendAttachment` + `EmailParentType` (7).
- `DocumentMechanisms.Emailable` (6 types), `AlertMedium.Sms`, `AlertSendLog.Medium`.
- `CustomTemplateType.Email` **removed**.

**Application**
- `EmailMergeFields` (the live catalogue, 4 groups) + `EmailMergeResolver` + `EmailMergeValueReader`.
- `EmailComposition` — the shared derivation `PrepareEmailQuery` and `SendEmailCommand` both go
  through, so the previewed draft and the sent message cannot disagree.
- `PrepareEmailQuery`, `SendEmailCommand` (+ validator), `ListEmailLogsQuery`, and the
  `EmailTemplate` create/update/set-default/list quartet.
- `IEmailSendJobProcessor` + `EmailSendJobProcessor`; `IDocumentPdfRenderer` (the Api-implemented seam).
- `IEmailSender` widened to `EmailMessage` (multi-recipient, CC/BCC, Reply-To, attachments, HTML),
  with the Phase 1a three-argument call kept as a default interface method.
- `AlertDispatcher` branches on medium, with an affordability pre-check and a per-recipient credit debit.

**Infrastructure / Api**
- EF configurations, one additive migration (hand-edited: the scaffolder's `defaultValue: ""` for
  `AlertSendLog.Medium` is not a valid enum member and would throw on the first read of any existing
  row — backfilled to `Email`), six seeded permissions.
- `MimeMessageFactory` shared by `SmtpEmailSender` and the new `FileDropEmailSender`;
  `EmailDeliveryMode` selected by configuration, never by environment name.
- `EmailSendRunnerOptions` + a fourth `QueuedJobRunnerHostedService` registration.
- `CommunicationsEndpoints` (multipart `POST /emails` with `.DisableAntiforgery()`),
  `MediatorDocumentPdfRenderer` wired at the composition root.

**Angular**
- `app-send-email-dialog` on all six emailable document types (seven screens) **and** the Contact
  detail page.
- Email Logs data behind phase 27b's existing tab and pager.
- An **Email Templates** configuration page with the server-served merge-field catalogue.

**Guards** — the server sweep gained seven facts (the six-type list, the four confirmed absences,
printable-is-wider, the both-directions context agreement, the Payment-needs-a-direction throw, the
by-name round trip, and Contact as the only non-document parent); the client sweep gained the
Send-Email-and-no-other-page pair.

Tests: Domain 398 (+12), Application.UnitTests 796 (+38), Angular 199 (+12).

---

## Follow-ups this phase deliberately did not take

- **A rich-text body editor.** The reference dialog is TinyMCE; this is a textarea that sends HTML.
  Same divergence, same seam, and the same reasoning as `app-terms-editor` — and on a
  customer-facing message, a plain textarea cannot silently inject markup.
- **`EmailTemplateContext.BalanceConfirmation` has no consumer.** It is offered on the template
  screen because the reference product offers it, but no Send Email action reaches it; phase 27b's
  balance-confirmation letter is still print-only. Wiring it is small and belongs with whoever next
  touches that letter.
- **`$[USER_ADDRESS]$` and `$[DOCUMENT_NOTE]$` always resolve empty** — `User` has no address column
  and no document aggregate carries a note. Offered for catalogue parity and documented as always
  empty, which is truthful and keeps a body pasted from the reference product from printing a raw
  placeholder.
- **`$[DUE_DATE]$` resolves to the document date**, because no aggregate stores a due date —
  phase-26b's carried item. `DocumentAgeQueryHandler` already ages every document from its own date
  for the same reason, so the email and the ageing report at least tell one story. One line changes
  when that item lands.
- **No resend action in the UI.** Live has none either, and the semantics are already right: reopen
  the dialog and send, which is a new row by construction.
- **A Goods line consumes stock regardless of `TrackInventory`** (found in Step 3). On a tenant
  without the Track Inventory feature this makes a Goods product uninvoiceable: opening stock is
  refused 403 and approval refused 409. Not phase 30's to fix, but it is a real trap for the next
  phase that seeds an org.
- **No per-recipient delivery status.** One row per send is Decision C; if a provider later reports
  per-recipient bounces, that is a child table, not a re-key.
- **`AlertMedium.Sms` has no UI.** The dispatcher, validation, credit debit and ledger all handle it;
  the Alert Scheduler form still offers Email only, because the reference product's Medium dropdown
  does too (phase 20e's live finding, unchanged).

---

## Postscript — a self-inflicted mess worth recording

A `sed -i` over the glob `web/src/app/features/*/*/*.ts`, used to correct one import's relative
depth, **rewrote the line endings of all 125 files it matched** from CRLF to LF. `git diff` showed
them as unchanged (autocrlf normalises on read) while `git status` showed 125 extra modified files,
turning a 72-file change set into a 191-file one. Restoring them needed `rm` **then**
`git checkout --` per file: a bare `git checkout` is a no-op when git already believes the content
matches.

The lesson is not "be careful with sed". It is that **a glob in an `-i` edit is a write to every file
it matches, whether or not the pattern fires** — and on Windows that write is visible even when the
content is not. Prefer restricting the file list to the ones actually being changed.
