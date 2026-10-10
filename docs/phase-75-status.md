# Phase 75 — Printing by location: the issuing location on every bill, and a template per location

## TL;DR

**A document now prints the location it was issued from, and a location can print a type with a template of its
own.** Before this phase every PDF printed the organization's registered address and nothing about the branch, and
one template per type served every branch (phase 73 Decision C). The till's receipt printed the branch's address *in
place of* the registered one. The user's decisions (2026-10-10), each the recommended option:

- **The location line prints only for an organization with more than one location** (Decision A): "Location: name,
  address" under the registered address, on every printable type, in all three layouts. A one-branch tenant never sees
  "HeadOffice" on a bill. A template may hide it (a new `PrintElement.Location`, never locked: Schedule 5 asks for the
  seller's address, which is the registered one).
- **A location may choose its own template per type, falling back to the organization's default** (Decision B), as the
  vendor does. Print, email and reprint all follow the document's location. Offered only for the types that carry a
  location under the tenant's Billing Location scope; any other is refused (409), never stored and ignored.
- **The till receipt prints the registered address as the seller's, and the location's address under its name**
  (Decision C), so the receipt and the PDF agree.

**Read live first, with one write** (abcagro1, § Live read). The vendor's selection is per type and location, and a
location with no selection prints with its "Default" location's. Its *Location Name* / *Location Address* switches are
off by default, and **switched on and saved they still print nothing**: the editor writes `org_location_name` beside the
stored `organization_location_name`, which stays `false`. A location with no selection of its own also makes the print
dialog's template list fail (400 "record not found"; the picker shows "Select Template").

| | |
|---|---|
| Live read taken | **yes**, 2026-10-10, abcagro1 (two writes through its own screens, listed at the end) |
| Migration | `Phase75PrintingTemplateLocationChoices`: one new table, `configuration.PrintingTemplateLocationChoices`, unique on (organization, type, location); deleting a template cascades to its choices |
| New endpoints | 3 under `/{org}/configuration/printing-templates/location-choices`: `GET` (View), `PUT {locationId, printingTemplateId}` and `DELETE ?locationId=&documentType=` (Manage) |
| Changed print output | every document of a multi-location organization: "Location: …" under the registered address; till receipts: the registered address, then the location's own |
| New permission keys | **none**: `Configuration.PrintingTemplate.Manage` sets a location's template as it sets the default |
| Changed screens | Configurations > Printing Templates: a *By location* card (shown with two or more locations); the layout editor's Organization group gains *Location* |
| Initial bundle | **652.58 kB**, unchanged (the configuration page is lazy) |

**Three things worth carrying forward.**

1. **A choice that cannot apply is refused, not stored.** A Quotation carries no location under the default sales-only
   scope, so a Quotation choice would sit in the table and never be read; the PUT answers 409 instead (phase 43's
   present-and-ignored rule). The screen offers only the types the server's own `locationBearingDocumentTypes` names.
2. **The handlers keep the invariant and the reader re-checks it.** Retyping or deactivating a template removes its
   choices; deleting one cascades in SQL. `PrintingTemplateResolver` still requires an active template of the same type,
   so a row written by any other door falls back to the default rather than printing another type's layout (phase 73's
   applier re-checking the Domain, again).
3. **The vendor's switch that does nothing.** A field the editor offers is not a feature until the print shows it:
   switched on and saved, the vendor's location switches wrote a key the renderer does not read (phase 54's "ask what
   writes it", from the other side).

Tests: Domain 991, Application.UnitTests **1705** (+18), Infrastructure.UnitTests 13, Api.IntegrationTests **57** (+5),
Angular **830** (+5, 101 files). `dotnet build`, all four .NET suites (Docker up, one at a time), `ng build` and `ng test`
are green.

---

## Live read, 2026-10-10 (abcagro1, *Abc Agro Trading*)

Entered as in phase 74 (15 days left): *Open Tigg*'s hand-off captured with `window.open` replaced and followed in the
same tab, its hash never printed; every API request the page made logged in the page; 1440x900 emulated, clicks by
element ref (and a 1440x1300 viewport for the editor's last section, whose switches sat on the 900 px edge).

1. **Configurations > Printing Templates has a *Location* picker** defaulting to "Default", the all-zero location id.
   `GET /printing-template-select?location_id=00000000-…` returns one `{source, channel, printing_template_id}` row per
   type and channel (ERP, POS, MOBILE-POS). The same call for HeadOffice and for POS Retail returned **`[]`**: no
   location had a selection of its own.
2. **Selecting is a click on a tile**: `POST /printing-template-select {selected_template_id, source_collection:
   "Invoice", location_id}` (no channel in the body). Done once, for POS Retail (my click landed on *Invoice Modern*).
3. **The print follows the document's location.** *View Print Preview* on INV0001/1003 sends `location_id` with the
   document (`collection-report-link?…&location_id=954d…`); it then rendered *Invoice Modern*. On INV0002/1002, a
   location with no selection, the page rendered the Default's *Invoice Standard*: **a location without a selection
   falls back to the Default location's**.
4. **A vendor defect there**: for that location the dialog's template list (`printing-templates?…&location_id=…`)
   answered **400 "record not found"**, so its picker read "Select Template" while the page printed Standard.
5. **The *Organization* section's *Location Name* and *Location Address* switches** (the client's `org_location_name`,
   `org_location_address`, `hasValue: false`) are off by default. Switched on and saved (the second write), the save
   carried **both** `organization_location_name: {display: false}` (the stored key) and `org_location_name: {display:
   true}` (the client's), and the preview of INV0001/1003 printed **no location** at all, only "Biratnagar," under the
   name. The switch does not reach the bill.
6. Every location's address on this tenant is blank, and the till receipt already prints the location's name (phase
   62). Ours already did the same; its address took the seller's place (fixed here, Decision C).

---

## 1. What shipped

- **Domain.** `PrintElement.Location` (offered on all seventeen printable types, hideable on all);
  `PrintingTemplateLocationChoice` (organization, type, location, template), created from a template so its type is the
  template's, and refusing a template of another type or organization.
- **Printing.** `PrintingTemplateResolver.ResolveAsync`: the location's choice if it names an active template of the
  type, else the type's default, else `PrintingTemplateSettings.Default`. `PrintDocumentQueryHandler` reads the
  document's location through phase 32b's `DocumentLocationReader`, resolves the template by it, and sets
  `LocationName`/`LocationAddress` when the organization has more than one location (counted active or not, so a
  closed branch's reprints keep its line). `PrintTemplateApplier` hides both with `Location`. The renderer prints
  "Location: name, address" under the address in every layout. The editor's preview shows a sample line on the same
  rule.
- **Commands.** `SetPrintingTemplateLocationChoiceCommand` (404 for an unknown template or location, 409 for an
  inactive template or a type carrying no location here; replaces an earlier choice),
  `ClearPrintingTemplateLocationChoiceCommand` (its own DELETE; clearing nothing succeeds),
  `ListPrintingTemplateLocationChoicesQuery`. `UpdatePrintingTemplateCommandHandler` removes a template's choices when
  it is retyped or deactivated.
- **Till receipts.** `PosReceiptDto` and `PosRefundReceiptDto` gain `LocationAddress`; `SellerAddress` is the
  organization's. Both receipts print the address under the location's name.
- **Screen.** Printing Templates: *By location* (only with two or more active locations): a *Location* select, then a
  table of the types that carry a location and have an active template, each with *Organization default (name)* and
  its active templates. A choice is a PUT, the default a DELETE; the result is announced in an always-present status
  region; a refused choice puts the select back by hand (phase 64). The layout editor lists *Location name and address
  (when there is more than one location)* under Organization.

## 2. Scope decisions (the user's, 2026-10-10, each the recommended option)

- **Decision A — the location line only for a multi-location organization**, hideable. The alternatives were always
  (a one-branch tenant's bills saying "HeadOffice") and the vendor's off-by-default switch (whose own version prints
  nothing).
- **Decision B — a template per location, falling back to the default.** The vendor's shape, minus its channel axis:
  the till prints its own receipts in the browser (phase 62), so only the ERP's PDF has a template to choose. This
  reverses phase 73's Decision C, which had recorded the per-location selection as a divergence.
- **Decision C — the till receipt prints both addresses.** Before, a location with an address replaced the registered
  address on the receipt.

Decided in the session, not asked:

- **Decision D — the location line is never locked.** VAT Rules Schedule 5 asks for the seller's address, and the
  registered address stays locked on a tax document as before. Which address a branch-issued bill must carry is a
  question for the tax advisor, recorded below, not guessed.
- **Decision E — clearing is a DELETE, not a null on the PUT**, so a misspelt `printingTemplateId` is a 400 naming it
  and never a silent "use the default" (phase 72's required-field rule).
- **Decision F — the choice table cascades from the template and restricts from the location.** A location is never
  deleted (only deactivated), and a second cascade path would be refused by SQL Server.
- **Decision G — no new permission key.** Choosing a location's template is setting a default at a narrower scope; a
  configuration key is not one a role grants per location (phase 32b scopes transaction keys). Admin-only, as `.Manage`
  already is.

## 3. Bugs found and fixed

- **A heredoc ate the apostrophes' escapes** in two error strings of the page (`'Could not change the location\'s …'`
  arrived as `location's` inside single quotes), and `ng test` failed to compile. The known gotcha; fixed with double
  quotes through the Edit tool.
- **The first E2E approve was a 409**: a fresh organization has no chart of accounts, so the seed now creates three
  accounts and sets the Sales, Receivable and VAT Payable defaults (a recipe added to `e2e-recipes.md`).

## 4. Evidence

**Suites** (Docker up, one at a time): Domain 991, Application.UnitTests 1705, Infrastructure.UnitTests 13,
Api.IntegrationTests 57, Angular 830 (101 files; two first-in-file tests timed out at 5000 ms under load beside `ng serve` and passed alone, 26/26). `ng build` green; initial bundle 652.58 kB.

**E2E** (fresh organization `919259a6-91ad-4d52-82e2-5f2d7bb1d2d6`, VAT-registered, multiple locations: HeadOffice and
`1002` Lakeside, Pokhara-6; seeded through the API by a Python script with a cookie jar, every status printed):

- Invoice templates *Plain* (the default) and *Ruled* (Traditional, footer "Lakeside branch: pay at the counter");
  `PUT location-choices {Lakeside, Ruled}` **200**, listed back.
- An approved invoice at each location, printed with the counted `POST /print/Invoice/{id}`: Lakeside's PDF reads
  **"Location: Lakeside, Pokhara-6"** under "Manbhawan, Lalitpur" and carries Ruled's footer; HeadOffice's reads
  **"Location: HeadOffice"** with no footer. Rendered with `Windows.Data.Pdf` and looked at: Lakeside's page is the
  ruled layout, the location line centred between the address and the PAN; HeadOffice's is Standard.
- Refusals: a Quotation template **409** ("A Quotation carries no location in this organization…"); a misspelt template
  field **400 naming `PrintingTemplateId`**; a clear without a type **400 naming `DocumentType`**; an unknown location
  **404**.
- Plain hiding `Location`: HeadOffice's reprint has no "Location:".
- Deactivating Ruled: the choice list is empty, and Lakeside's reprint falls back to Plain (no footer, no line).
- Clear: **204**, list empty. Then Ruled chosen again, a CreditNote template chosen for HeadOffice, and Ruled
  **deleted** (204): only the CreditNote choice is left, by the SQL cascade.

**403-not-404.** `PUT location-choices {Lakeside, nonexistent template}` is **404** "Printing template not found." as
Admin, and **403 naming `Configuration.PrintingTemplate.Manage`** from a custom role holding only
`Configuration.PrintingTemplate.View`, beside that user's **200** on `GET location-choices`; its DELETE is 403 too. The
membership was put back on Admin in SQL.

**In SQL** (`sqlcmd`, Windows authentication): one choice row (CreditNote, HO, Spare, active); templates Plain
(`HiddenElements = Location`), Quote, Spare; invoices 0001 at `1002` and 0002 at `HO`, two prints each; zero choices
without a template.

**Browser and keyboard pass** (`erp-web` at `http://localhost:4200`, the test login's cookie set on
`https://localhost:7104` from a file a script wrote and deleted at once):

- Printing Templates on the E2E organization shows *By location* below the list: *Location* "HeadOffice (HO)", rows
  only for **Invoice** and **CreditNote** (the sales-only scope's types that have templates; no Quotation row), and
  HeadOffice's CreditNote select already reading its saved *Spare*.
- Keyboard only: thirteen Tabs from the page's back link reach the *Location* select (its label "Location"); ArrowDown
  picks *Lakeside (1002)* and the table's column becomes "Template at Lakeside"; Tab reaches the select labelled
  "Invoice"; ArrowDown chooses *Branch bill*: the status region reads **"Lakeside now prints Invoice with Branch
  bill."**, focus stays on the select with the #0a58ca ring. ArrowUp: **"Lakeside now prints Invoice with the
  organization's default."**; ArrowDown again, and SQL reads `Invoice | 1002 | Branch bill`.
- The layout editor for *Plain* lists *Location name and address (when there is more than one location)* last in its
  Organization group, unticked (Plain hides it); Address and PAN still checked and locked.
- The console held no error.

## 5. Left open, and for later phases

- **Which address a branch-issued tax invoice must carry** (a question for the tax advisor). Today the registered
  address is locked and the location's line is optional.
- **The location on the till receipt is unconditional** (as since phase 62); only the PDF follows Decision A's
  multi-location rule.
- **A location's template has no channel axis.** If the till ever prints through templates, the vendor's ERP / POS /
  MOBILE-POS split would need one.
- **Ordering and placement of header blocks, the Modern/Classic layouts and label renames, an 80 mm ERP invoice, amount
  in words for a foreign currency, the vendor's DN/GRN/WT custom HTML templates**: phase 73 § 5, unchanged.
- **The vendor's defects recorded here** (the location switches that print nothing, the template list that 400s for a
  location with no selection) are facts for a later comparison, not work for us.
- Carried unchanged: phase 74 § 5, phase 72 § 5, phase 71 § 5, phase 70 § 5, phase 69 § 5, phase 67 § 5, phase 66 § 5,
  phase 63 § 5, phase 62 § 5.

## Writes made to the vendor tenant (abcagro1, with the user's leave)

- **POS Retail (1003)'s Sales Invoice template selection** set to *Invoice Modern* (it had none; it now prints Modern
  where it printed the Default's Standard).
- **The *Invoice Modern* template** saved with *Location Name* and *Location Address* switched on (no visible effect,
  § Live read 5).
- Nothing was deleted, and no other setting, role or account was changed.
