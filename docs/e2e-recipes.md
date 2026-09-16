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
- `sqlcmd -i` runs with `QUOTED_IDENTIFIER OFF`, so any `INSERT` into a table with a filtered index fails; put `SET QUOTED_IDENTIFIER ON` at the top of every script. `-W` and `-y/-Y` are also mutually exclusive (phase-34c).
