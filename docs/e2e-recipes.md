# E2E recipes — the endpoint and `sqlcmd` shapes a seed script needs

Moved out of `CLAUDE.md` on 2026-09-16. These are lookups, not rules: you need them while writing a
curl seed script or a `sqlcmd` verification, and never otherwise. Each was paid for by a session
that lost time to it, and the phase that found it is named.

**Read this before writing a seed script.** The recurring shape of the mistake is that a wrong field
name deserialises to a default rather than failing — `POST /products` with `productType` silently
yields a *Goods* product, which then 409s at Approve with a message about the warehouse — so the
symptom appears three steps later and in another subsystem.

The rules that merely *use* these facts stay in `CLAUDE.md`: print every status code, prove a 403
beside a 200 from the same user, and create a fresh Organization per phase.

- `sqlcmd -S localhost` against a **named** instance returns nothing and reports nothing; read the instance from the connection string (`DESKTOP-H0R00ME\SQLEXPRESS` here). Only printing every status code makes the resulting 400 visible (phase-41).
- A registered user has no verification code until `POST /api/auth/request-verification-code`; a Member-role user is the only way to prove a document-scoped 403, since Admin is seeded with every key (phase-27b).
- `POST /products` takes **`primaryUnitId`**; `POST /accounts` takes **`{name, groupId, kind}`** only (no `code`, and `kind` is an `AccountKind` — `"Normal"` fails to deserialise with a 400 naming no field); a fresh organization has **no warehouse** (phase-34b).
- Print every approval's status code in a seed script; `POST /api/organizations` returns `organizationId`, and the GL defaults are one `PUT /accounting-defaults` with all eleven accounts (phase-26c).
- A fresh Organization has no Accounts or Account Groups; an E2E posts one group per `AccountRootType` (singular `Asset`/`Liability`/`Equity`/`Income`/`Expense`) before any account (phase-27a).
- `identity` is a reserved word in T-SQL — reading a verification code needs `[identity].VerificationCodes`, and every document-scoped 403 proof needs a Member user, which needs that code (phase-28).
- `POST /accounts` `kind` is `Other`/`Bank`/`Cash`; `POST /organizations` needs `accountingStartDate` + `workspaceName`; `POST /auth/register` needs `phone`; `locationGrants` is a list of `{locationId, grants}`; lists are paged (`['items'][0]['id']`) (phase-35a).
- `POST /organizations` entitlement flags are `multipleLocations`/`multipleWarehouses`/`trackInventory` (no `Enabled` suffix); `POST /billing-locations` needs `address`; invitations return `membershipId`; `vatRate` is `ThirteenPercentVat` (phase-35b).
- `sqlcmd -Q` prints "(N rows affected)" into a captured value; `SET NOCOUNT ON` belongs beside `SET QUOTED_IDENTIFIER ON` at the top of every script (phase-35b).
- `sqlcmd -i` chokes on a forward-slash absolute path, reporting "-E and the -U/-P options are mutually exclusive"; run it from a relative path (phase-36).
- `POST /auth/register` needs `turnstileToken` as well as `phone`, and `POST /organizations/{id}/invitations` takes **`roleId`** (system Member = `…-0001-000000000002`), not a role name (phase-36).
- `PUT /general-settings` takes `RecentSellingPrice`/`ExclusiveOfVat`/`AccountingMovement` — not the guessable names — and a wrong member is a 400 naming no field; a negative-permission proof for a key **Member legitimately holds** needs a custom role with no grants (phase-37).
- `POST /api/organizations` needs `industry` and a non-empty `turnstileToken`; accept-invitation is `/api/organizations/memberships/{id}/accept-invitation` with no org segment; units are `/units-of-measurement`; credit terms are under `/configuration/` (phase-31).
- `POST /accounts` takes `groupId`; `POST /products` takes `type`, not `productType`, and the wrong name silently yields a Goods product that 409s at Approve about the warehouse (phase-32).
- `POST /products` needs `categoryId`; creates return 201; configuration lookups live under `/organizations/{id}/configuration/...`; a fresh organization has no warehouse, unit, category or product (phase-40).
- `POST /units-of-measurement` takes **`shortName`**, not `symbol` — the wrong name is a 400 naming `ShortName`, which is at least honest; `POST /deals` and `PUT /deals/{id}` differ only in that the PUT has **no `contactId`**, and `PUT /tasks/{id}/status` takes `{newStatus}` (phase-48).
- Both record lists narrow to one row with `?id=` (`GET /deals?id=`, `GET /tasks?id=`) — that is how the detail and edit pages read their record, and it inherits the private-deal visibility rule for free (phase-48).
- `POST /contacts` takes **`type`** (`Customer`/`Supplier`/`Lead`) and a required `openingBalance` — not `contactType`; it is the cheapest `IExpirySensitiveMasterData` write for proving an expiry gate. `TenantSubscriptions` lives in the **`tenancy`** schema, so expiring a tenant is one `UPDATE tenancy.TenantSubscriptions SET TermEndsAt = DATEADD(day, -15, SYSDATETIMEOFFSET())` (the aggregate refuses to build the state — phase 31's rule) (phase-49).
- A command with **no document id of its own** can still get the 403-not-404 proof: `PUT .../subscription` with a **nonexistent `planId`** 404s in the handler (`"That subscription plan does not exist."`), so a 403 can only have come from `AuthorizationBehavior` — beside a 200 on `GET .../subscription` from the same Member in the same run (phase-49).
- `sqlcmd -i` runs with `QUOTED_IDENTIFIER OFF`, so any `INSERT` into a table with a filtered index fails; put `SET QUOTED_IDENTIFIER ON` at the top of every script. `-W` and `-y/-Y` are also mutually exclusive (phase-34c).
- `POST /payments` requires **`allocations`** to be present (`NotNull`, so `[]` for an unallocated payment) — omitting it is a 400; a `Cheque` row is written **only** as a side effect of a Payment whose `paymentModeId` names a mode with `requiresChequeDetails: true`, via `chequeDetails: {chequeNo, chequeDate, receivedDate}`. There is no endpoint that creates one directly (phase-50).
- The member list is `GET /{org}/members`, not `/memberships`, and its rows key the id as **`membershipId`** — `['items'][0]['membershipId']`. `PUT /{org}/memberships/{membershipId}/role` then takes `{roleId}` and is how a run moves *its own* user onto a custom role, which is the cheapest way to get a 403 without a second user and an email verification code (phase-50).
- The cheque negative path is a complete 403-not-404 proof with a control: `POST /{org}/cheques/{nonexistent}/transition` returns **404** ("Cheque not found.") while the user is Admin, and **403** naming `Accounting.Cheque.Manage` after the same user is moved to a role granting only `Accounting.Cheque.View` — same organization, same run, with `GET /cheques` returning 200 on both sides (phase-50).
- `sqlcmd` rejects `-s ' '` with "Unexpected argument" (the separator cannot be a space); use `-s','`. Tables are in **per-module schemas** — `payments.Cheques`, `sales.Invoices`, `contacts.Contacts`, `catalog.Products`, `accounting.GlLines`, `tenancy.Organizations` — so an unqualified or `dbo.`-qualified name is "Invalid object name" (phase-50).
- `PUT /accounting-defaults` spells the two control accounts **`defaultAccountsReceivableId`** and **`defaultAccountsPayableId`** — *not* `defaultReceivableAccountId`/`defaultPayableAccountId`, which every neighbouring key would lead you to write. There is **no cash account** on that payload at all. The wrong name deserialises to null and the `PUT` returns **200**, so the symptom is a `409` at the first Approve — *"Default Accounts Payable account is not configured"* — three steps later. `GET`ting the same endpoint back and reading the key names is the fastest way to settle it (phase-51).
- Opening stock is `PUT /opening-balances/products/{productId}` with `{warehouseId, quantity, rate}` — there is no `/opening-stock` route, and the wrong one is a bare **404** with an empty body, which reads like an auth problem (phase-51).
- Phase 51's tracked-product shapes: `POST /products` takes trailing **`batchTracking`** / **`serialTracking`** (both default false; either one on a Service product, or with `trackInventory: false`, is a 400 naming the field). A line carries **`batchNo`** (a *number*, not an id — it is minted on a receipt), optional `manufactureDate`/`expiryDate`, and **`serialNumbers`** as an array of exactly `quantity` strings (phase-51).
- A 403-not-404 proof for a `Reports.*` key needs no nonexistent id: a report **has** no id. Grant a custom role one of a *pair* of sibling report keys, and the 403 on one sits beside a 200 on the other, same user, same run — plus a 200 on the refused report from before the role change as the control (phase-51).

- A fresh Organization's lookup routes are **directly under** `/api/organizations/{id}` — `/units-of-measurement`, `/product-categories`, `/warehouses` — and **not** under `/configuration/`, which 404s with an empty body (phase-52, correcting the reading of phase-40's note).
- `PUT /accounting-defaults` needs all twelve accounts including `defaultRoundingAccountId` and `defaultInventoryAdjustmentAccountId`; `POST /account-groups` takes `{name, rootType}` with singular root types (phase-52).
- A product's unit matrix is `POST /products/{id}/secondary-units` `{unitId, conversionRate, sellingPrice, purchasePrice}`, and `PUT .../secondary-units/{secondaryUnitId}` takes the same **without** `unitId` — the unit is the row's identity, not one of its values (phase-52).
- A document line takes `unitId` (the **unit lookup's** id, not the product's secondary-unit row id). Null means the product's primary unit. A unit the product does not carry is a 400 naming `Lines[N]`; the conversion factor is never accepted from the client (phase-52).
- The membership table is **`tenancy.OrganizationMemberships`**, not `tenancy.Memberships`. This matters because an E2E that moves its own user onto a custom role without `Tenancy.Role.Manage` **cannot move it back** — `PUT /memberships/{id}/role` is a 403 and so is `GET /roles`, so the only way out is `UPDATE tenancy.OrganizationMemberships SET RoleId = '00000000-0000-0000-0001-000000000001'`. The seeded Admin role id is that constant (`Role.AdminId`) (phase-52).
- A workspace name is unique across the whole database, so a seed script that hardcodes one fails with a 409 on its **second** run and every run after; derive it (`phase52u$(date +%H%M%S)`) (phase-52).
- An Inventory Adjustment line takes a trailing **`unitId`** like the other eight (null = the product's primary unit); its `direction` is `Increase`/`Decrease` and a Decrease line still needs `unitCost` present (send `0`). `PUT /inventory-adjustments/{nonexistent}` is a clean 403-not-404 proof: **404** as Admin (*"Inventory adjustment not found."*) and **403** naming `Inventory.InventoryAdjustment.Edit` from a custom role holding only `Inventory.InventoryAdjustment.View` (phase-54).
- `PUT /accounting-defaults`'s twelve accounts, in the spellings that actually deserialise: `defaultSalesAccountId`, `defaultAccountsReceivableId`, `defaultVatPayableAccountId`, `defaultPurchaseAccountId`, `defaultAccountsPayableId`, `defaultVatReceivableAccountId`, `defaultInventoryAccountId`, `defaultCogsAccountId`, `defaultInventoryAdjustmentAccountId`, `defaultRoundingAccountId`, `defaultDiscountAccountId`, `defaultLandedCostClearingAccountId` (phase-54).
- `sqlcmd` fails **any DML** on `inventory.StockLedgerEntries` with msg 1934 — *"SET options have incorrect settings: 'QUOTED_IDENTIFIER'"* naming five unrelated features — because the table carries phase 51's filtered index. `SET QUOTED_IDENTIFIER ON` at the top of the script, and note this now bites on `DELETE`/`UPDATE`, not only `INSERT` (phase-54, extending phase-34c's note).
- An Angular route for a document is `/organizations/{id}/<module>/<type>/{docId}` — `/organizations/{id}/inventory/inventory-adjustments/new`, **with** the module segment. Omitting it is `NG04002: Cannot match any routes` in the console and a blank page with no error on screen (phase-54).
- `POST /import-jobs` carries the bank account as a **query parameter** (`?entityType=BankStatement&mode=CreateNew&reviewBeforeApply=true&bankAccountId=…`) beside the multipart file — a simple type on a multipart POST binds from the query string without help, unlike an array (phase 38). Omitting it is a 400 naming `BankAccountId`, and supplying it on any *other* `entityType` is also a 400 (phase-55).
- A statement-line delete is `POST /bank-accounts/{id}/statement-lines/delete` with **both keys present**: `{"lineIds": [...], "importJobId": null}` or `{"lineIds": null, "importJobId": "…"}`. It is a POST, not a DELETE, because the body carries a list. Exactly one selector, or it is a 400 (phase-55).
- A **template download returns a workbook**, so a Python E2E helper that does `resp.read().decode()` dies with a `UnicodeDecodeError` that reads like a failed request; decode in a `try` and fall back to reporting the byte count (phase-55).
- `PagedResult<T>`'s constructor is `(Items, Page, PageSize, TotalCount)` — not `(Items, TotalCount, Page, PageSize)`, which every neighbouring DTO's field order would lead you to write. The two middle arguments are both `int`, so only the compiler's arity check catches it (phase-55).
- The reference product is behind Cloudflare: a bare `urllib`/`requests` call to `api-v2.tigg.app` returns **HTTP 403 "error code: 1010"** whatever the auth header says. Drive it from the Browser pane instead of adding a user-agent — the pane is a real browser and passes legitimately (phase-55).
- The import template's header row marks a **required** column with a trailing `**` (`Date**`), so a seed script matching header text exactly finds nothing and reports a column that is plainly there as missing; strip trailing asterisks before matching (phase-56).
- `POST /import-jobs` returns **201**, not 202, and `GET /import-jobs/{id}` returns `{job, rows}` -- the status is `['job']['status']`, not `['status']`. The download route is `/import-templates/{entityType}`, not `/import-jobs/template` (phase-56).
- A Journal Voucher is the cheapest way to put GL lines on a named account: `POST /journal-vouchers` `{date, reference, lines: [{accountId, debit, credit}]}` then `POST /journal-vouchers/{id}/approve` with `{}`. No product, warehouse or contact is needed, which is what makes it the right seed when the subject is the ledger rather than a document (phase-56).
- A custom role for a negative-permission proof is two calls: `POST /{org}/roles` `{name, description}` then `PUT /{org}/roles/{id}/permissions` `{grants: {"<key>": true}, locationGrants: null}` -- `grants` is a **dictionary**, and `locationGrants` must be present (phase-56).
- Quick Approve is `POST /bank-accounts/{id}/statement-lines/{lineId}/quick-approve` with `{target, targetId, locationId}`, where `target` is **`Contact`** or **`Account`** (two members, not the vendor's four). Against a nonexistent line it is a clean **404** as Admin (*"Bank statement line not found on this account."*) and a **403** naming `Accounting.BankStatement.Manage` from a custom role holding only `Accounting.BankStatement.View` -- beside a 200 on `GET .../statement-lines` from that same user (phase-57).
- The bank-statement import template's columns are `Date | Deposit | Withdrawal | Description` in **that order**, and the trailing `**` on a required header still has to be stripped before matching (phase 56). A statement is the only way to create a `BankStatementLine` -- there is no `POST .../statement-lines` -- so an E2E that needs one fills the app's own generated template with `openpyxl` and posts it multipart (phase-57).
- `POST /import-jobs` with `reviewBeforeApply=false` completes without a second call; poll `GET /import-jobs/{id}` until `['job']['status'] == 'Completed'` (phase-57).
- A `python - <<'EOF'` heredoc in the Bash tool mis-parses exactly as `cat`'s does, and a multi-line replacement string built with `\n` will not match a CRLF file -- normalise **both** sides to the file's own newline, and let an asserted anchor count abort rather than half-patch (phase-57, extending phase-26a).
- Delivery Note / GRN: `POST /goods-received-notes` `{contactId, warehouseId, date, reference, trackingNo, lines: [{productId, quantity, rate, vatRate, discountPct, unitId}], referrerType?, referrerId?}`; `POST /delivery-notes` adds a **required** `expectedDeliveryDate` plus `shippingAddress` and `terms`. Approve and Void are bodiless POSTs, except that DN Approve takes an optional `{overrideWarning}` and answers a shortfall under Warn with **422** `warningKind: "StockAvailability"`, the Invoice's shape (phase-58).
- The conversion templates are `GET /purchase-orders/{id}/goods-received-note-conversion-template` and `GET /sales-orders/{id}/delivery-note-conversion-template`. The GRN template carries **no warehouse**, so add `warehouseId` before posting it back. A second conversion from the same order is a **409**, and so is voiding a received PO or a delivered SO (phase-58).
- `PUT /general-settings` replaces all six fields, so `GET` it first and send every one back, changing only `inventoryTrackingMode` (`PhysicalMovement`/`AccountingMovement`) or `negativeStockBalanceAction` (`Reject`/`Warn`/`DoNothing`) (phase-58).
- The physical balance in SQL is `inventory.PhysicalStockMovements` (Direction `In`/`Out`) **plus** `inventory.StockMovements` rows whose `SourceDocumentType IN ('WarehouseTransfer','InventoryAdjustment','ProductionJournal','OpeningStock')`; the accounting balance is `StockMovements` alone. A DN/GRN row in `GlJournalEntries`, `StockMovements` or `StockLedgerEntries` is a bug, so assert 0. Orders carry `sales.SalesOrders.DeliveredAt` and `purchasing.PurchaseOrders.ReceivedAt` (phase-58).
- `GET /reports/inventory-position` takes an optional `mode` and **echoes the ledger it read** in the response's `mode`. `GET /reports/inventory-variance` takes `asOfDate`, not a range. The 403-not-404 pair: `POST /delivery-notes/{nonexistent}/approve` is **404** as Admin (*"Delivery note not found."*) and **403** naming `Sales.DeliveryNote.Approve` from a role holding only `Sales.DeliveryNote.View`; the report pair is inventory-variance **403** beside inventory-position **200** (phase-58).
- On a tenant seeded with **Opening Stock**, the three-view check is layers = movements = GL Inventory **+ the opening stock's value**. Opening Stock posts no GL entry, so a check without the term fails by exactly that constant (phase-58).
- In the Browser pane a native `<select>`'s option list cannot be clicked by coordinate (the click lands and nothing is chosen, and the form then reports the field empty); set it with `form_input` on the select's ref (phase-58).
- POS configuration (phase 60) lives under `/{org}/pos`: `GET /configuration` (walk-in + locations with `posMode`), `GET|PUT /locations/{id}/settings` (a **whole-object** `PUT`: `serviceChargeEnabled, serviceChargeRate, serviceChargeAccountId, roundOffEnabled, roundOffAccountId, cashVerificationRequired, denominations, defaultTab, printEstimateBill, printInvoice, printCreditNote, printKot`), `PUT /locations/{id}/mode` `{posMode}` (`None`/`Retail`/`Restaurant`), and `PUT /locations/{id}/payment-modes` `{paymentModeIds: [...]}`. `defaultTab` is `Retail`/`DineIn`/`TakeAway`/`Delivery` and must belong to the location's **current** mode, so set the mode first (phase-60).
- `POST /organizations` takes `posRetail`/`posRestaurant`. A payment mode that a till can offer needs `kind` (`Cash`/`Card`/`EPayment`/`Other`) **and** an `accountId` naming a `Cash`- or `Bank`-kind account; `POST /configuration/payment-modes` with an income account is a 400 naming `AccountId`. The walk-in is a contact with code `WALKIN`, and `POST /contacts/{id}/deactivate` on it is 409 (phase-60).
- The `Pos.Settings.Manage` 403-not-404 pair: `GET /pos/locations/{nonexistent}/settings` is **404** as Admin (*"Billing location not found."*) and **403** from a role granting only `Tenancy.BillingLocation.View`, beside a **200** on `GET /billing-locations` from that same user. The feature gate is a separate 403 whose detail names *"Point of Sale (Retail) or Point of Sale (Restaurant)"*; authorization runs first (phase-60).
- The till (phase 61) is under `/{org}/pos`. Sessions:
  - `POST /sessions {locationId, openingAmount, denominations: [{value, count}] | null}`. With cash
    verification on, `denominations` is required and an amount alone is a 400.
  - `GET /locations/{id}/sessions/mine` returns **204** when you have none open.
  - `POST /sessions/{id}/cash-movements {direction: In|Out, amount, accountId, note, overrideNegativeCashBalanceWarning}`.
  - `POST /sessions/{id}/close {countedAmount | denominations, note}`. A difference needs `note`
    (400 otherwise).
  A location needs `PosMode` set, a linked **Cash** mode with an account, and (for a sale) a default
  warehouse on `PUT /billing-locations/{id}` (which answers **204**) or `warehouseId` on the sale
  (phase-61).
- `POST /pos/sales {sessionId, locationId, lines: [{productId, quantity, rate, vatRate?, discountPct, unitId?, batchNo?, serialNumbers?}], tenders: [{paymentModeId, amount}], changeAmount, contactId?, warehouseId?, orderType?, discountPct, overrideStockWarning, overrideCreditLimitWarning}`.
  `contactId` null is the walk-in, and `vatRate` null is the product's own. It returns `{id, code,
  grandTotal, serviceCharge, roundOff, tendered, changeAmount, creditAmount}`. Credit on the walk-in
  is a 409, and change beyond the cash tendered is a 400 naming `Tenders` (phase-61).
- The `Pos.Session.*` 403-not-404 pair: `GET /pos/sessions/{nonexistent}` (or `POST .../close`) is
  **404** "Session not found." as Admin. From a role granting only `Tenancy.BillingLocation.View` it
  is **403 naming `Pos.Session.Operate`**, beside a 200 on `GET /billing-locations`. The day's
  figures (phase 66: `GET /pos/reports/day?fromDate=&toDate=`, which replaced `/pos/day-summary?date=`) are
  **403 naming `Pos.Session.ViewAll`** from the same role (phase-61).
- In SQL a till sale is `sales.Invoices.Channel = 'Pos'` with `PosSessionId`, tenders in
  `sales.InvoiceTenders`, and two `GlJournalEntries` rows (`SourceDocumentType = 'Invoice'`). A
  session's own postings are `SourceDocumentType = 'PosSession'`. Drawer counts are text such as
  `1000x1,500x1` in `pos.PosSessions.OpeningCount`/`ClosingCount` (phase-61).

- The till's reads (phase 62), all under `/{org}/pos` and gated on `Pos.Session.Operate`:
  - `GET /tills` lists the launcher's rows, each with `mySessionId`/`mySessionCode`.
  - `GET /tills/{locationId}` is the till's settings subset, with `paymentModes` (Cash first),
    `categories`, `walkInCustomer`, `warehouseId` and `canSellOnCredit`.
  - `GET /tills/{locationId}/products?search=&code=&categoryId=&page=&pageSize=`: `code` is an exact
    Barcode/Code/SKU match, and every `rate` is VAT-exclusive after the price basis. A product's
    `units[0]` is the primary unit.
  - `GET /sessions/{id}/sales` lists the session's sales, with `printCount`.
- `POST /pos/sales/{invoiceId}/prints` has no body and returns the receipt with `printNumber` (1 is
  the original) and `title` (`Invoice`/`TaxInvoice`/`AbbreviatedTaxInvoice`). It is gated by
  `Sales.Invoice.View` at the sale's location. An ERP invoice or a voided sale is a 409. In SQL it is
  `sales.InvoicePrints`, and the stored heading is `sales.Invoices.IsAbbreviatedTaxInvoice`.
- `PUT /pos/locations/{id}/settings` takes a 13th field, **`abbreviatedTaxInvoiceEnabled`**. The
  sale result carries `isAbbreviatedTaxInvoice`. A walk-in bill is abbreviated only on a
  VAT-registered organization with the setting on and a total of at most 10,000 (phase-62).
- The till's 403-not-404 pair: `GET /pos/sessions/{nonexistent}/sales` is **404** "Session not
  found." as Admin. It is **403 naming `Pos.Session.Operate`** from a role holding
  `Sales.Invoice.Create/View`, `Tenancy.BillingLocation.View` and `Tenancy.Subscription.View`, beside
  a 200 on `GET /billing-locations`. The last key is what lets the browser reach the till's own
  refusal rather than be redirected Home by the feature guard (phase-62).
- Driving the till in the browser pane: stub `window.print` after **every** full reload (a Vite
  reload after an edit drops it too), and fill number inputs with `form_input`. A ref-based click on
  them can land elsewhere (phase-62).

- Refunds at the till (phase 63), under `/{org}/pos`:
  - `GET /tills/{locationId}/sales?search=` finds approved till sales of that location by number
    (newest 20). Phase 84: plus another location's sale whose number is **exactly** `search`, only for a
    caller holding `Sales.CreditNote.Create` and `.Approve` at `locationId`; each row has
    `soldAtLocationName` (null at home) and `fullyRefunded` (phase 85 removed `refundableHere`: a
    restaurant bill from another location is refunded there too).
  - `GET /tills/{locationId}/sales/{invoiceId}/refundable` (phase 84; was `/sales/{id}/refundable`)
    returns the lines with `invoiceLineId`, `sold`, `remaining`, plus `owed`, `priorRefunds`,
    `canRefund`, `soldAtLocationName` and `returnWarehouseName`. A home sale needs `Sales.Invoice.View`
    at the till; an away one the two refund keys there (a 403 naming the missing one). A refunding location with no default warehouse is a 409 naming it.
  - Phase 84: `POST /products` must send **`availableForSale: true`** for a till seed; absent, it binds to false and
    `POST /pos/sales` is a 409 "'Coke 250ml' is not available for sale.". In SQL a till refund's warehouse is
    `sales.CreditNotes.WarehouseId` (NULL on older and ERP notes, meaning the sale's). The 403-not-404 pair for the
    refundable read: a nonexistent id is 404 "Sale not found." as Admin and **403 naming `Sales.Invoice.View`** from a
    role holding every Admin key but that one; from a role lacking only `Sales.CreditNote.Approve` the nonexistent id is
    404 and another location's real sale is **403 naming `<CODE>.Sales.CreditNote.Approve`**.
  - `POST /refunds/preview {sessionId, invoiceId, lines: [{invoiceLineId, quantity}]}` returns the
    server's figure: `grandTotal`, `roundOff`, `owedBefore`, `requiredPayout`, `toAccount`.
  - `POST /refunds {sessionId, locationId, invoiceId, lines, payouts: [{paymentModeId, amount}], reason}`.
    The payouts must sum to exactly `requiredPayout` (a 400 naming `Payouts` otherwise), so preview
    first. A credit sale's refund sends `payouts: []`. A missing reason is a 400 naming `Reason`.
  - `GET /sessions/{id}/refunds` lists a drawer's refunds with `printCount`, and
    `POST /refunds/{creditNoteId}/prints` (no body) returns the credit note with `printNumber`.
  - A refund needs `Sales.CreditNote.Create` (pipeline) **and** `Sales.CreditNote.Approve` at the
    location (handler), which a default Member lacks. The 403-not-404 pair: `POST /refunds` naming a
    nonexistent `invoiceId` is **404** "Sale not found." as Admin and **403 naming
    `Sales.CreditNote.Approve`** from a role without it, because the key is checked before the sale is read.
  - In SQL a refund is `sales.CreditNotes.Channel = 'Pos'` with `PosSessionId` (the session it was
    paid out of, not the sale's), `Reason`, `RoundOff`; payouts are in `sales.CreditNotePayouts`; prints
    in `sales.CreditNotePrints`; and it posts up to two `GlJournalEntries` rows
    (`SourceDocumentType = 'CreditNote'`): the note, and the payout (none for a credit sale).

- The restaurant (phase 64), all under `/{org}/pos`:
  - Configuration: `GET /locations/{id}/floor-plan`; `POST /locations/{id}/areas {name}`;
    `PUT /areas/{id} {name, isActive}`; `PUT /areas/{id}/layout {tables: [{id|null, name, capacity, shape:
    Rectangle|Circle, x, y, width, height, isActive}]}` (every existing table must be in it, or 400 naming
    `Tables`; the canvas is 1100 x 800). Kitchen stations: `GET|POST /kitchen-stations {name}`,
    `PUT /kitchen-stations/{id} {name, isActive}`, `PUT /kitchen-stations/{id}/products {productIds}` (the
    whole list; a product left off goes back to Default). A station called "Default" is a 400.
  - The till: `GET /restaurants/{locationId}` (floor + open orders + `canVoid`),
    `GET /restaurants/{locationId}/products`, `POST /orders {locationId, orderType: DineIn|TakeAway|Delivery,
    tableId, covers, contactId, items: [{productId, quantity, unitId, note}]}` (no rate: the catalogue prices
    it), `GET|PUT /orders/{id}` (`PUT` takes `{tableId, covers, contactId}`), `POST /orders/{id}/items
    {newItems, moreOf: [{lineId, quantity}]}`, `POST /orders/{id}/serve {items}`, `POST /orders/{id}/discard
    {items, reason}`, `POST /orders/{id}/void {reason}`, `POST /orders/{id}/tickets/{ticketId}/prints` (no body).
  - The ERP list: `GET /orders?status=&orderType=&page=&pageSize=&search=&fromDate=&toDate=&locationId=&sort=`.
  - A location must be in Restaurant mode (`PUT /locations/{id}/mode {posMode: "Restaurant"}`) on an
    organization created with `posRestaurant: true`; every order request also needs `Sales.Invoice.Create`
    at the location (handler). A Delivery needs a named, non-walk-in `contactId`.
  - In SQL: `pos.PosOrders` (Code `ORD0001`, Status `Open`/`Voided`), `pos.PosOrderLines` (no quantity
    column; `ServedQuantity` only), `pos.KitchenTickets` (SendNumber, KitchenStationId NULL = Default,
    Reason on a cancellation, PrintCount), `pos.KitchenTicketLines` (signed Quantity). A line's net quantity is
    `SUM(KitchenTicketLines.Quantity)` grouped by `PosOrderLineId`. An order posts nothing: no
    `GlJournalEntries`, `StockMovements` or `Invoices` row names it.
  - The 403-not-404 pair: `POST /orders/{nonexistent}/discard` is **404** "Order not found." as Admin and
    **403 naming `Pos.Order.Void`** from a role holding `Pos.Order.Operate` but not `.Void` (phase-64).

- Billing a restaurant order (phase 65), under `/{org}/pos`:
  - `POST /orders/{id}/bill/preview {split: Whole|Items|Equal, items: [{lineId, quantity}], parts}` prices a part
    (Pos.Order.Operate). `parts` is "one of N parts of what is left"; `items` is `[]` unless `split` is Items.
  - `POST /orders/{id}/invoices {sessionId, locationId, split, items, parts, tenders: [{paymentModeId, amount}],
    changeAmount, contactId, overrideStockWarning, overrideCreditLimitWarning}` bills it as a till sale
    (Sales.Invoice.Create, your own open session at the order's location). Returns `{id, code, grandTotal,
    serviceCharge, roundOff, ..., orderStatus, orderToBill}`. The location needs a default warehouse.
  - `GET /kitchen/{locationId}?view=Pending|Served|All&station=<id>|default&orderType=` is the board
    (Pos.Kitchen.Operate); `POST /orders/{id}/tickets/{ticketId}/serve {items}` serves (`[]` = everything pending).
  - In SQL: `sales.Invoices.PosOrderId`, `sales.InvoiceLines.PosOrderLineId`; an order line's invoiced quantity is
    `SUM(InvoiceLines.Quantity)` over Approved invoices; `pos.PosOrders.Status = 'Settled'` with `SettledAt`.
  - The 403-not-404 pair: `GET /kitchen/{nonexistent}` is **404** "Billing location not found." as Admin and
    **403 naming `Pos.Kitchen.Operate`** from a role holding `Pos.Order.Operate`, beside its 200 on
    `GET /restaurants/{loc}` (phase-65).
- `LineNo` is a T-SQL keyword (`LINENO`): `pos.PosOrderLines.LineNo` must be bracketed, `ol.[LineNo]`, or the
  whole batch fails (phase-65).
- Mark as Take Away and transfer (phase 68), under `/{org}/pos`:
  - `POST /orders/{id}/take-away {lineId, quantity}` returns the order; the parcel is a new line with `isTakeAway: true`
    and `parcelledFromLineId`, at the rate `PUT /locations/{id}/settings`'s **14th field `serviceChargeOnTakeAway`**
    gave it when marked (absent from the PUT reads as On). Only a Dine In order, only food not yet served and not yet billed.
  - `POST /orders/{id}/transfers {tableId, items: [{lineId, quantity}]}` returns `{source, target, targetCreated}`;
    a free table gets a new Dine In order (1 guest, walk-in). Its own table is a 400 naming `TableId`; a Take Away
    source is a 409. A transfer that empties an unbilled source voids it ("All items transferred to ORD…").
  - In SQL: `pos.KitchenTickets.Kind` (`Send`/`Cancellation`/`TakeAway`/`Transfer`) and `CounterpartOrderId`;
    `pos.PosOrderLines.IsTakeAway`, `ParcelledFromLineId`; a line's net is still `SUM(KitchenTicketLines.Quantity)`.
    An invoice's grand total is **not a column**: rebuild it as `SUM(Amount + ServiceChargeAmount + VatAmount) + RoundOff`.
  - The 403-not-404 pair: both endpoints against a nonexistent order are **404** "Order not found." as Admin and
    **403 naming `Pos.Order.Operate`** from a role holding `Pos.Order.View` + `Tenancy.BillingLocation.View`,
    beside a 200 on `GET /billing-locations`. Send a valid body: validation runs before authorization (phase-68).
- In the Browser pane a Bootstrap `.btn-check` radio clicked by its ref hits the hidden input and changes nothing;
  click its `<label>` ref instead (phase-65).
- The audit table is `workflow.Audits` (`Action`, `DocumentType`, `LocationId`), not `tenancy.AuditEntries` (phase-65).
- POS reports (phase 66), under `/{org}`, every one taking `fromDate`, `toDate` (both required, at most 366 days)
  and an optional `locationId` (an unknown one is a 404 "Billing location not found."):
  - `GET /pos/reports/day` (+ `/export`), `GET /pos/dashboard`, `GET /pos/sessions?status=Open|Closed&search=&page=`
    (+ `/export`) -- `Pos.Session.ViewAll`.
  - `GET /pos/reports/orders?status=Open|Settled|Voided&orderType=&page=` (+ `/export?full=`) -- `Pos.Order.View`,
    PosRestaurant.
  - `GET /reports/pos-payment-summary?type=Cash|Card|EPayment|Other|Credit&paymentModeId=&page=` (+ `/export?full=`)
    -- `Reports.PosPaymentSummary.View`.
  - `channel=Pos|Erp` on `/reports/sales-by-item`, `/sales-by-customer`, `/sales-summary`, `/sales-master-report`,
    `/sales-register`, `/system-audit` and their exports; a purchase-side report with a channel is a 400 naming
    `Channel`. In SQL `sales.Invoices.Status` is `Void`, not `Voided` (and the same on `CreditNotes`).
  - The identities to check in SQL: register total (Σ lines `Amount + ServiceChargeAmount + VatAmount`, invoices less
    credit notes) = the day's `netSales - netRoundOff`; the GL Rounding account's credit − debit = `netRoundOff`.
  - The 403-not-404 pair: `GET /reports/pos-payment-summary?...&locationId=<nonexistent>` is **404** as Admin and
    **403 naming `Reports.PosPaymentSummary.View`** from a role holding `Reports.SalesRegister.View`, beside its 200 on
    `/reports/sales-register` (phase-66).
- The till applies service charge on Retail and on a sale with no order type; only Take Away and Delivery are exempt
  (phase 64 H). A seed that tenders a Retail momo at 200 + VAT is 22 short and refused (phase-66).
- The bill preview's figure is `total`, not `grandTotal` (`POST /pos/orders/{id}/bill/preview`); the bill itself
  returns `grandTotal` (phase-66).

- The counted print (phase 67) is `POST /{org}/print/{Invoice|CreditNote}/{id}` with no body, returning the
  PDF. The `GET` on those two types is a **409**; a draft or a voided document is a 409, and any other type on
  the POST is a 400 naming `DocumentType`. Rows are in `sales.InvoicePrints` / `sales.CreditNotePrints` with
  `PrintNumber` and `Medium` (`TillReceipt`/`Pdf`/`Email`), shared with the till's `.../prints` routes. The
  detail DTOs carry `printCount`.
- An email is `POST /{org}/emails` as a **form** (`application/x-www-form-urlencoded` works when there are
  no files): `requestId`, `documentType`, `parentId`, `to`, `subject`, `body`, `attachDocumentPdf=true`.
  It returns 202 and the job sends within seconds; `communications.EmailSendLogs.Status` goes to `Sent`. The
  dev environment sends through **SMTP**, not the file drop, so address it to a reserved `.test` domain.
- Reading a PDF's text in an E2E: inflate each stream (`zlib`), map glyphs through `/ToUnicode`, parsing
  `bfrange` and `bfchar` only inside their own sections (phase 67's `e2e67_lib.pdf_text`, the Python twin of
  `TestSupport/PdfText`). Print it with `PYTHONIOENCODING=utf-8`, or Devanagari kills the script.
- A credit note's invoice (phase 69): `POST|PUT /credit-notes` take `againstInvoiceId` (a price adjustment) **or**
  `againstInvoiceNumber` + `againstInvoiceDate` (one issued before the system), plus `reason`. A typed date on or after
  the tenant's first non-draft invoice is a **400 naming `AgainstInvoiceDate`**; a mismatch with the picked invoice
  (customer, currency, location, date, nothing left) is a **409**. Approve on a VAT-registered organization with
  nothing named is a 409 citing Rule 20. `POST /currencies {code: "USD"}` activates a currency for a USD invoice.
- The picker is `GET /credit-notes/creditable-invoices?contactId=&locationId=&search=&excludingCreditNoteId=`, a
  `PagedResult` of `{id, code, date, currencyCode, exchangeRate, grandTotal, creditedTotal, remainingTotal}`. A list
  answers a nonexistent contact with **200** and no rows, so its 403-not-404 pair is that 200 as Admin, then **403
  naming `Sales.CreditNote.Create`** from a role holding only `Sales.CreditNote.View`, beside its 200 on
  `GET /credit-notes`. In SQL: `sales.CreditNotes.AgainstInvoiceId`, `AgainstInvoiceNumber`, `AgainstInvoiceDate`,
  `Reason`; a conversion keeps `ReferrerType = 'Invoice'` and a null `AgainstInvoiceId` (phase-69).
- A fresh organization's invoice codes have no prefix (`0001`), so match codes from the approve response, never a
  literal `INV…` (phase-69).
- The packaging fee (phase 70): `PUT /pos/locations/{id}/settings` takes **`packagingFeeBasis`** (`None`/`Percent`/`PerUnit`),
  `packagingFeeValue` and `packagingFeeAccountId`; an **absent basis keeps the stored fee and its account**, a 0% is a 400
  naming `PackagingFeeValue`. `POST|PUT /products` take `packagingFeeApplicable` (absent: on for a create, unchanged for an
  update); `PUT /accounting-defaults` takes `defaultPackagingFeeAccountId`. Only a Take Away/Delivery line, or a parcel
  marked from a table, carries it. In SQL: `PackagingFeeBasis`/`PackagingFeeValue` on `pos.PosLocationSettings`,
  `pos.PosOrderLines`, `sales.InvoiceLines`, `sales.CreditNoteLines`, plus `PackagingFeeAmount` on the two document lines;
  `catalog.Products.PackagingFeeApplicable`. An invoice's total is now `SUM(Amount + ServiceChargeAmount +
  PackagingFeeAmount + VatAmount) + RoundOff`.
- Statuses a seed script must not guess (phase 70): `PUT /billing-locations/{id}` is **204**; `POST /pos/sessions`,
  `POST /pos/sales`, `POST /pos/orders` and `POST /pos/orders/{id}/invoices` are **200**; `POST /pos/locations/{id}/areas`
  returns the **whole floor plan** (`['areas'][0]['id']`), not the area. `GET /{org}/members` lists `membershipId`/`email`.
- The settings 403-not-404 pair: `PUT /pos/locations/{nonexistent}/settings` with a **valid** body is **404** "Billing location
  not found." as Admin and **403 naming `Pos.Settings.Manage`** from a role holding only `Tenancy.BillingLocation.View`,
  beside its 200 on `GET /billing-locations` (phase-70).
- A bill's dates (phase 71): the invoice, credit-note and debit-note PDFs print `Transaction Date: 2083-04-16 BS
  (2026-08-01 AD)` and `Issue Date: …` (the Nepal day of `ApprovedAt`; none on a draft or an abbreviated invoice)
  **without** an `X-Calendar` header. A credit note's invoice is two fields, `Against Invoice: <code>` and
  `Invoice Date: …`. PyMuPDF's `page.get_text()` reads our PDFs' text directly (install it with `pip --target` into the
  scratchpad). The statutory register exports' first two columns are `Date (BS)` and `Date (AD)`, as text.
- `PUT /pos/locations/{id}/settings` needs a non-empty **`denominations`** even with cash verification off (a 400
  naming `Denominations`). The `POST /print/Invoice/{nonexistent}` pair: **404** as Admin, **403 naming
  `Sales.Invoice.View`** from a role holding only `Sales.CreditNote.View`, beside its 200 on `POST /print/CreditNote/{id}`
  (phase-71).
- A browser pass without the token in the transcript: a script signs in and writes the `erp_auth` value into
  `web/public/<name>.txt`, the preview is **restarted** (a file added after start-up is a 404), the page `fetch`es it
  and sets `document.cookie` (`path=/; Secure; SameSite=None`) only when the status is 200 and the body is not HTML, and
  the file is deleted at once (phase-71).
- An order's terms (phase 72), under `/{org}/pos`: `PUT /orders/{id}/discount {discountPct}`,
  `PUT /orders/{id}/lines/{lineId}/discount {discountPct}` and `PUT /orders/{id}/service-charge {waived}`, each returning
  the order (**200**). The field is required: an empty body is a **400 naming `DiscountPct`** (or `Waived`), and 12.345%
  is a 400. Once anything they price is billed a change is a **409**; the value already there is a 200. The order and its
  lines carry `discountPct`, `serviceChargeWaived`, `gross` and `discount`; the bill preview carries `gross`, `discount`,
  `discountPct`, `serviceChargeWaived`, and each line's billed `serviceChargeRate`.
- Their keys (phase 72): `Pos.Order.Operate` in the pipeline, `Sales.Invoice.Approve` at the order's location in the
  handler (**phase 76: `Sales.Invoice.TillDiscount` for both discounts, `Sales.Invoice.TillRemoveServiceCharge` for the
  waiver**, so the 403 below names those). So a nonexistent order is **404** as Admin and **403 naming `Pos.Order.Operate`** from a role holding
  `Pos.Order.View` + `Tenancy.BillingLocation.View` (beside its 200 on `GET /billing-locations`), and for a role holding
  every waiter key but Approve it is **404** on a nonexistent order and **403 naming `Sales.Invoice.Approve`** on a real
  one. `GET /pos/restaurants/{loc}` and `GET /pos/tills/{loc}` carry `canDiscount`.
- `POST /pos/sales` with a `discountPct` (line or bill) or a `rate` below the grid's for its unit is **403 naming
  `Sales.Invoice.Approve`** without that key; a higher rate is a 200 (phase-72).
- A bill's `contactId` names that bill's customer and the order keeps its own; with a named customer, `tenders: []` puts
  the whole bill on account. In SQL: `pos.PosOrders.DiscountPct`/`ServiceChargeWaived`, `pos.PosOrderLines.DiscountPct`;
  a bill's `sales.Invoices.DiscountPct` is the order's, `sales.InvoiceLines.DiscountPct` the line's, and its
  `ServiceChargeRate` is `.00` when the order was waived (phase-72).
- `sqlcmd` here needs no secret: the connection string uses Integrated Security, so
  `sqlcmd -S "DESKTOP-H0R00ME\SQLEXPRESS" -d ErpApp -E -W -s '|' -Q "SET NOCOUNT ON; SET QUOTED_IDENTIFIER ON; ..."`.
  A run that moves its own membership onto a role without `Tenancy.Role.Manage` switches it on and back with
  `UPDATE tenancy.OrganizationMemberships SET RoleId = ... WHERE Id = <membershipId>`; the next request sees it (phase-72).
- Seed statuses (phase 72): `POST /account-groups`, `/warehouses`, `/units-of-measurement`, `/product-categories`,
  `/products`, `/contacts`, `/configuration/payment-modes`, `/billing-locations` and `/roles` are **201**;
  `PUT /accounting-defaults`, `PUT /pos/locations/{id}/mode|payment-modes|settings`, `PUT /pos/areas/{id}/layout` and
  `PUT /roles/{id}/permissions` are **200**.

- Phase 73's printing-template routes are under **`/configuration/`**: `POST /configuration/printing-templates`
  `{name, documentType}` (201; the first for a type is the default), `PUT .../{id}/layout` `{layout, hiddenElements,
  printedCustomFieldIds, footerText, compactMargins}` (200; every member required, enums by name), `POST .../preview`
  (the same plus `documentType`, returns a PDF) and `GET .../elements`. Custom fields are `POST
  /configuration/custom-field-definitions` `{name, type, applicableDocumentTypes, choiceOptions}` (201) and
  `PUT /configuration/custom-field-values/{documentType}/{id}` `{values: [{fieldDefinitionId, value}]}` (**204**). In
  SQL, `configuration.PrintingTemplates.HiddenElements` and `.PrintedCustomFieldIds` are comma-joined, so read them
  with `-s'|'`, not `-s','` (phase-73).
- `POST /invoices` takes a **required** `warehouseId` even when every line is a Service product; null is a 400 that
  names no field ("Failed to read parameter"). Create a warehouse in the seed (phase-73).
- The auth cookie's JWT lives **one hour**. A browser pass that runs past it lands on Sign In with three 401s in the
  console; log in again from the script and re-transplant the cookie (on `https://localhost:7104`, since a `Secure`
  cookie cannot be set from the `http://localhost:4200` page and cookies are shared across ports) (phase-73).
- Amount discounts (phase 74). `POST /pos/sales` takes a line's **`discountPerUnit`** (rupees off each unit, never
  beside its `discountPct`) and the bill's **`discountAmount`** (never beside `discountPct`); both kinds, a per-unit
  over the rate, or an amount over what the lines come to are each a **400 naming the field**. The order and line
  discount `PUT`s take `{discountPct}` **or** `{discountAmount}`, exactly one; neither, or both, is a 400, and an amount
  over the order is a 409. Any price but the catalogue's, or either Amount, needs `Sales.Invoice.Approve` at the
  location (403 naming it). In SQL: `sales.InvoiceLines.DiscountPerUnit` / `BillDiscountAmount`,
  `sales.Invoices.DiscountAmount` (the sum of the shares), the same three on credit notes, `pos.PosOrders.DiscountAmount`,
  `pos.PosOrderLines.DiscountPerUnit` and `OrderDiscountShare` (NULL until the first bill, and again after every bill is
  voided). A refund's lines carry their share; sale less refunds should be zero per product and per figure (phase-74).
- A test user can hold only **one open session per location**: a second `POST /pos/sessions` at the same location is a
  409 naming the open one, so a later script must reuse the session an earlier one opened (phase-74).
- A fresh organization has **no chart of accounts**, so an invoice's Approve is a 409 ("no Default Sales Account").
  Three `POST /account-groups {name, rootType, parentGroupId: null}` (Asset, Liability, Income), three `POST /accounts
  {name, groupId, kind: "Other"}`, then `GET /accounting-defaults`, set `defaultSalesAccountId`,
  `defaultAccountsReceivableId` and `defaultVatPayableAccountId`, and `PUT` it back whole (200) (phase-75).
- A location's printing template (phase 75): `GET|PUT|DELETE /configuration/printing-templates/location-choices`. The
  PUT takes `{locationId, printingTemplateId}` (200, the type is the template's); a type carrying no location under the
  scope is **409**, an inactive template 409, an unknown template or location 404. The DELETE takes `?locationId=
  &documentType=` (204; no type is a 400 naming `DocumentType`). The 403-not-404 pair: the PUT naming a nonexistent
  template is **404** as Admin and **403 naming `Configuration.PrintingTemplate.Manage`** from a role holding only
  `.View`, beside its 200 on the GET. In SQL: `configuration.PrintingTemplateLocationChoices`.
- The till keys (phase 76): `Sales.Invoice.TillDiscount` (a discount at either till), `.TillRemoveServiceCharge` (the
  waiver, both ways) and `.TillChangeRate` (a Retail price off the catalogue) are checked at the location, and a
  location-only grant is `PUT /roles/{id}/permissions` with `locationGrants: [{locationId, grants: {"<key>": true}}]`;
  the 403 names `<CODE>.<key>` (a fresh HeadOffice's code is `HO`). Credit still names `Sales.Invoice.Approve`.
  `GET /pos/tills/{loc}` carries `canDiscount`/`canChangeRate`, `GET /pos/restaurants/{loc}` `canDiscount`/
  `canRemoveServiceCharge`. A table's `shape` is `Rectangle` or `Circle` (`Square` is a 400 naming no field).
  The POS role-editor read: `/pos/settings/users-permissions/role-reference/edit?id=…`, its *Edit* button saves (phase-76).
- Phase 77. A transfer is `POST /pos/orders/{id}/transfers {tableId, items: [{lineId, quantity}]}` (200, returning
  `{source, target, targetCreated}`), and a parcel is `POST /pos/orders/{id}/take-away {lineId, quantity}` (200, the
  order). Validation runs before authorization, so a 403-not-404 probe needs a **valid** body: one item with a random
  `lineId` and quantity 1, not `items: []` (that is a 400 for everyone).
  - A restaurant bill is `POST /pos/orders/{id}/bill/preview {split, items, parts}` (its `total`), then
    `POST /pos/orders/{id}/invoices` with the same and `sessionId`, `locationId`, `tenders`, `changeAmount`,
    `contactId`, `overrideStockWarning` and `overrideCreditLimitWarning`.
  - A till refund is `POST /pos/refunds/preview {sessionId, invoiceId, lines: [{invoiceLineId, quantity}]}` (its
    `grandTotal`), then `POST /pos/refunds` with `locationId`, `payouts` and `reason`.
  - In SQL, a refund's sale is `sales.CreditNotes.ReferrerId` (with `ReferrerType = 'Invoice'`), not
    `AgainstInvoiceId`. `pos.PosOrderLines.[LineNo]` needs its brackets: `LineNo` is a T-SQL keyword.
  - The export is `POST /export-jobs?categories=SalesDocuments&categories=Payments` (201). Poll `GET /export-jobs`
    for `Completed`, then `GET /export-jobs/{id}/download`. Read the .xlsx from its XML with `zipfile`; no openpyxl is
    needed. Sales Documents' Σ(Amount + Service Charge + Packaging Fee + VAT Amount + Round Off) equals SQL's
    invoices less credit notes, and Payments' received less paid equals Σ tenders − Σ `ChangeAmount` − Σ payouts.
  - `python -I` ignores `PYTHONIOENCODING`; print a Σ with `python -I -X utf8`.
- Phase 78. A purchase document stores a `LocationId` only when the tenant's scope is *All Transactions*: `PUT
  /organizations/{id}/billing-location-settings {locationScopeMode: "AllTransactions", locationWiseReportPermission:
  false}` (200). On the default *Sales Transactions Only* every bill, debit note and expense has NULL, and the export's
  Location column is rightly empty.
  - Purchase shapes: `POST /purchase-bills` takes `lines: [{productId, quantity, rate, vatRate,
    expenditureClassification, discountPct, unitId}]`, `tdsTypeId`, `discountPct`, `currencyCode`/`exchangeRate`,
    `additionalCosts: [{costTermId, productId, method: "Value"|"Quantity", amount}]` and `locationId` (201; approve
    is a bodiless POST, 200). A debit note against it takes `referrerType: "PurchaseBill"`, `referrerId` and
    `warehouseId`; an expense takes `tdsApplicable`, `tdsTypeId` and `lines: [{accountId, amount, vatRate}]`. Lookups:
    `POST /configuration/tds-types {code, name, ratePct}`, `POST /configuration/cost-terms {name, category:
    "AdditionalCost"}`, `POST /products/{id}/secondary-units {unitId, conversionRate, sellingPrice, purchasePrice}`.
  - `POST /pos/sales` answers **200**, not 201. A drawer's Cash In from the bank is a 409 on a tenant whose bank holds
    nothing (the negative-cash guard); take a float top-up from an equity account.
  - The export job table is `exports.ExportJobs`; its `Categories` column is comma-joined, so read it with `-s'|'`.
  - The download's 403-not-404 pair: `GET /export-jobs/{nonexistent}/download` is **404** "Export job not found." as
    Admin and **403 naming `Configuration.ExportJob.View`** from a role holding only `Tenancy.BillingLocation.View`.
  - Accounting defaults for a purchase E2E: set every `default*Id` by name (payable, receivable, both VATs, inventory,
    landed cost clearing, TDS payable, purchase, COGS) from one `GET`, then `PUT` it back whole.
- Phase 79. `PUT /configuration/printing-templates/{id}/layout` takes `layout` `Standard|Traditional|Minimal|Modern|
  Classic|Retail`; Retail on any type but `Invoice`/`CreditNote`/`SalesOrder` is a **400 naming `Layout`** (so is the
  same on `POST .../preview`). `GET .../elements` rows carry `layouts: [{layout, omits}]`. A roll's counted print is one
  page whose `/MediaBox` is `[0 0 226.75 <height>]` (80 mm), readable straight from the PDF's bytes. The 403-not-404 pair:
  the layout `PUT` on a nonexistent template with a **valid** body is 404 as Admin and **403 naming
  `Configuration.PrintingTemplate.Manage`** from a role holding only `.View`, beside its 200 on `GET .../elements`.
  `POST /currencies {code: "USD"}` is 201. Render a PDF without poppler with `powershell -File tools/pdf/pdf2png.ps1 -Dir <folder>`
  (`Windows.Data.Pdf`, every `*.pdf` there to `<name>-p<N>.png`); the sizes it prints are DIPs (302.33 = 80 mm).
- Phase 80. The physical-movement and order documents: `POST /quotations {contactId, date, expiryDate, reference, lines,
  discountPct}`, `POST /sales-orders {…, deliveryDate, …}`, `POST /purchase-orders {contactId, date, reference, lines}`,
  `POST /goods-received-notes {contactId, warehouseId, date, reference, trackingNo, lines, referrerType:
  "PurchaseOrder", referrerId}` and `POST /delivery-notes {contactId, warehouseId, date, expectedDeliveryDate, reference,
  trackingNo, shippingAddress, lines, referrerType: "SalesOrder", referrerId}`; a line is `{productId, quantity, rate,
  vatRate, discountPct, unitId}`; each approves with a bodiless POST (200). Creating the note marks its order
  delivered / received. Manufacturing: `POST /bills-of-materials {productId, outputQuantity, manufactureOnEverySale,
  notes, isActive, rawMaterials: [{productId, quantity}], byProducts: [{productId, costAllocationPct, quantity}],
  expenses: [{costTermId, amount}]}`, a cost term with `category: "ProductionCost"`, and production orders / journals
  with the same three lists (a journal takes `warehouseId`, `referrerType: "ProductionOrder"`, `referrerId`, and needs
  its raw material in stock). A cash transfer from an empty cash account is refused, so fund it with a journal voucher
  first (`lines: [{accountId, debit, credit}]`). Tables: `manufacturing.ProductionJournals` (its cost columns),
  `manufacturing.BillsOfMaterials`, `inventory.PhysicalStockMovements`, `purchasing.GoodsReceivedNotes`,
  `sales.DeliveryNotes`. An invalid `categories` value on `POST /export-jobs` is a 400 from binding, before
  authorization, so the 403 probe sends valid ones.

- Phase 81. The ERP price keys: `GET /sales/price-powers?documentType=Invoice|Quotation|SalesOrder&locationId=` returns
  `{canDiscount, canChangeRate}` (the type's View key; any other type is a 400). A refusal on `POST`/`PUT /invoices`,
  `/quotations` or `/sales-orders` names `<CODE>.Sales.Invoice.Discount` or `.ChangeRate`, or the bare key for a
  quotation, which carries no location on the default *Sales Transactions Only* scope. A clerk role for the proof: copy
  every key Member holds organization-wide from SQL (`tenancy.RolePermissions` where `RoleId` is
  `…-0001-000000000002`), drop the two, and grant one at a location with `locationGrants`. `PUT /general-settings` takes
  `suggestSellingPriceMode` `FixedSellingPrice|RecentSellingPrice` and `productPriceBasis`; read it, change, and `PUT`
  it back whole. `GET /products/{id}/suggested-rate?unitId=` answers in that unit. Conversion is `POST /invoices` with
  `referrerType: "Quotation"` and `referrerId` of an **approved** quotation.
- Phase 82. A restaurant seed from nothing: `POST /organizations {name, industry, address, accountingStartDate,
  isVatRegistered, workspaceName, …, posRestaurant: true, turnstileToken}` (201, `organizationId`; any token passes
  the dev secret). A payment mode's `accountId` must be an account created with `kind: "Cash"` or `"Bank"` (a 400
  naming `AccountId` otherwise), and a restaurant bill is a **409** until the location has a warehouse: `PUT
  /billing-locations/{id} {code, name, address, warehouseId, isActive}` (204). `PUT /pos/locations/{id}/mode
  {posMode: "Restaurant"}`, then the settings, payment modes, an area and `PUT /pos/areas/{id}/layout`. A discard is
  `POST /pos/orders/{id}/discard {items: [{lineId, quantity}], reason}` (200, the order, its `discountAmount` lowered
  once billed). Its 403-not-404 pair, with a valid body: **404** "Order not found." as Admin and **403 naming
  `Pos.Order.Void`** from a role holding `Pos.Order.View` + `.Operate` + `Tenancy.BillingLocation.View`. In SQL a
  discard is a `pos.KitchenTickets` row of `Kind = 'Cancellation'` with its `Reason` and a negative line quantity.
- Phase 83. A guest's discount is `discountPct` **or** `discountAmount` on both `POST /pos/orders/{id}/bill/preview`
  and `POST /pos/orders/{id}/invoices` (both is a 400 naming `DiscountAmount`; null or 0 is none). The preview answers
  `partDiscount` and each line's `partDiscountShare`; `GET /pos/orders/{id}` answers `partDiscount` and a `total` net of
  it. `POST /pos/locations/{id}/areas` answers **200**, not 201, and a table must sit inside the 1100 by 800 floor
  (`x + width <= 1100`). The bill with a discount from a role lacking `Sales.Invoice.TillDiscount` is **403** naming
  `HO.Sales.Invoice.TillDiscount`; the 403-not-404 pair is the preview of a nonexistent order with a valid body: 404 as
  Admin, **403 naming `Pos.Order.Operate`** from a role holding `Pos.Order.View` + `Tenancy.BillingLocation.View`.
  A role holding every key but one: read Admin's grants from `GET /roles/{adminId}/permissions` (`groups[].permissions[]`
  of `{permissionKey, isGranted}`) and `PUT` them back minus the key. In SQL, `sales.InvoiceLines.PartDiscountAmount`
  sits inside `BillDiscountAmount`. `GET /sales/price-powers` answers `atLocation` too. There is no `audit.AuditEntries`.
- Phase 85. A restaurant bill for a refund: `POST /pos/orders {locationId, orderType: "DineIn", tableId, covers, contactId,
  items: [{productId, quantity, unitId, note}]}` (200, the order with `lines[].id`), `POST /pos/orders/{id}/take-away
  {lineId, quantity}` (200), the preview's `total`, then `POST /pos/orders/{id}/invoices` with exactly that in cash.
  Location settings take `packagingFeeBasis: "PerUnit"`, `packagingFeeValue` and `packagingFeeAccountId`. Pick a
  refund's lines from `sales.InvoiceLines` by `PackagingFeeAmount`. `GET /credit-notes/{id}` answers
  `posRefund.warehouseName`; a till sale's tenders are `sales.InvoiceTenders` (`PaymentModeId`, `Amount`).
- Phase 86. A sale's recorded fee accounts are `sales.Invoices.ServiceChargeAccountId` / `.PackagingFeeAccountId` (NULL on
  sales before phase 86 and on a sale without that figure). Moving a location to new fee accounts is the whole
  `PUT /pos/locations/{id}/settings` body again with the new ids. `GET /credit-notes` rows carry `returnWarehouseName`; a
  credit note's void is `POST /credit-notes/{id}/void` with no body (200).
- Phase 87. ERP documents for a reversal E2E: `POST /contacts {type: "Customer"|"Supplier", name, openingBalance: 0}`;
  `POST /invoices {contactId, warehouseId, date, reference, lines: [{productId, quantity, rate, vatRate}]}` then
  `POST /invoices/{id}/approve {overrideWarning: true}`; a return is `POST /credit-notes {contactId, date, referrerType:
  "Invoice", referrerId, reason, lines}` + a bodiless approve; a debit note takes `referrerType: "PurchaseBill"` and
  `warehouseId`. `POST /payments {contactId, direction: "Received"|"Paid", date, paymentModeId, accountId, amount,
  reference, allocations: [{targetDocumentType, targetDocumentId, amount}]}`, approve with
  `{overrideNegativeCashBalanceWarning}`; a later allocation is `POST /payment-allocations/apply {sourceType: "Payment",
  sourceId, parentDocumentId, targetDocumentType, targetDocumentId, amount}` (key `Payments.Payment.Edit`, and its
  validator needs the target to exist, so the 403 probe names a real invoice and a nonexistent payment). Recorded
  accounts: `sales.Invoices.ReceivableAccountId` / `VatPayableAccountId` / `CogsAccountId` / `InventoryAccountId`,
  `sales.InvoiceLines.SalesAccountId`, `purchasing.PurchaseBills.PayableAccountId` (…), `purchasing.PurchaseBillLines.
  DebitAccountId`, `payments.Payments.UnallocatedControlAccountId`. Account groups made by the phase 85 seed are named
  `Assets`, `Liabilitys`, `Incomes`, `Expenses`, `Equitys`.
- Phase 88. `GET /payments/open-allocation-targets?contactId&direction&currencyCode` (rows `{targetDocumentType, id,
  code, date, dueDate, amount, balance, currencyCode}`); `GET /payments/default-allocations` needs `currencyCode` (400
  without). `POST /payments/preview-gl-posting {accountId, amount, direction, currencyCode, exchangeRate, allocations}`
  (`allocations` required). Both GETs take `Sales.Invoice.View` (Received) / `Purchasing.PurchaseBill.View` (Paid): a
  role copied from Member minus one is a 403 beside a 200 on the other direction, and a nonexistent contact is a 200
  `[]`, never a 404. `GET /roles/{id}/permissions` is `{groups: [{module, permissions: [{permissionKey, isGranted}]}]}`;
  the system Member role is `…-0001-000000000002`.
- Phase 89. `GET /reports/vat-summary?fiscalYear=2083` (and `/export`) answers `{fiscalYear, fromDate, toDate, columns,
  lines: [{key, section, label, labelNepali, isTotal, monthly[12], total}], netVat}`; no `fiscalYear` is a 400, one
  outside the BS table (2095) a 404. Hold a month to `GET /reports/sales-register` and `/purchase-register` over its
  `columns[i].fromDate/toDate` (the registers net returns, so compare line minus its return line). `POST /expenses
  {contactId, date, dueDate, supplierInvoiceReference, notes, tdsApplicable: false, tdsTypeId, lines: [{accountId,
  amount, vatRate}]}` + `POST /expenses/{id}/approve {}`; an import bill is `isImport: true` with `importCountry`,
  `importDate`, `importDocumentNo`; a capital line is `expenditureClassification: "Capital"`. A counted print is `POST
  /print/Invoice/{id}` (200, a PDF). The audit table is **`workflow.Audits`** (`Action = 'Create'`). Its 403 pair: a
  role copied from Member minus `Reports.VatSummary.View` is 403 naming the key beside a 200 on `GET
  /reports/trial-balance?asOfDate=`; put the membership back with `UPDATE tenancy.OrganizationMemberships`.
- Phase 90. `GET /reports/purchase-register` rows carry `invoiceNo` (a bill's or expense's `supplierInvoiceReference`,
  a debit note's own code) and `/export` an *Invoice No* column after *Document No*. A purchase bill's supplier number is
  `supplierInvoiceReference` on `POST /purchase-bills` (after `reference`); `GET /reports/annex-thirteen?fromDate&toDate
  &thresholdAmount` is before VAT. `GET /reports/invoice-age?fromDate&asOfDate&contactId` is the per-contact read to hold
  against the organization-wide one. A `.xlsx` from the exporter is namespaced (`<x:row>`, `<x:c>`, `<x:v>`), and an empty
  cell has no element, so read cells by their `r` column letter. Its 403 pair: a role holding only
  `Reports.PurchaseRegister.View` + `Purchasing.PurchaseBill.View` is 403 naming `Reports.AnnexThirteen.View` beside a
  200 on the register, and 403 naming `Sales.Invoice.View` on the Received picker for a nonexistent contact beside a
  200 on Paid.
- Phase 91. A serial-tracked product: `POST /products {..., serialTracking: true}`; receive it with `POST /purchase-bills`
  lines carrying `serialNumbers` (one per unit) and a bodiless-`{}` approve. The till's location needs a warehouse
  (`PUT /billing-locations/{id}`), and `PUT /pos/locations/{id}/settings` a non-empty `denominations`. `GET /pos/tills/
  {loc}/products?code=<serial>` answers the product with `scannedSerialNo` (the stored casing); `GET /pos/tills/{loc}/
  products/{productId}/serials?search=` lists the units in stock. `POST /pos/sales` lines take `serialNumbers` (none is
  a 400 naming `Lines[0]`, an unknown unit a 409); refunds and their preview take `lines: [{invoiceLineId, quantity,
  serialNumbers}]` (a unit already returned is a 400 naming `Lines`); `GET .../refundable` lines carry `serialNumbers`
  and `returnableSerialNumbers`. An ERP note: `GET /invoices/{id}/credit-note-conversion-template` lines carry the
  units still out; `POST /credit-notes` with them + `reason`, then approve. In SQL: `inventory.DocumentLineSerials`
  (`ParentType` `InvoiceLine`/`CreditNoteLine`/`PurchaseBillLine`, `ParentLineId`, `SerialNo`) and
  `inventory.StockLedgerEntries.SerialNo` (a returned unit is a new `SourceDocumentType = 'CreditNote'` layer of 1).
  A restaurant bill's guest declining the service charge: `waiveServiceCharge: true` on `POST /pos/orders/{id}/bill/
  preview` and `/invoices` (required on the client's request; the preview answers `partServiceChargeWaived`); in SQL
  `sales.InvoiceLines.PartServiceChargeWaived` with `ServiceChargeRate .00`. A table with an open order refuses a second
  `DineIn` order (409), so seat a probe as `TakeAway`. The 403 pairs: the serial list of a nonexistent product is 404 as
  Admin and **403 naming `Pos.Session.Operate`** from a role holding every Admin key but that, beside a 200 on
  `GET /billing-locations`; a real order's waived bill is **403 naming `<CODE>.Sales.Invoice.TillRemoveServiceCharge`**
  beside the same bill charged (200).
