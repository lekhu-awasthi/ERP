#!/usr/bin/env bash
# Phase 34c -- scale dataset, part 1 of 2: the master data and the ONE reference invoice.
#
# Decision C (docs/phase-34a-status.md) says the 120,000 bulk rows are produced by direct INSERT,
# "then a single API-driven approve to prove the seeded rows are shaped like real ones". This script
# is that half: it drives the real API to create a fresh organization, its chart of accounts, its
# GL defaults and exactly one contact / one product / one approved invoice. seed-bulk.sql then
# replicates THOSE rows -- reading their column values out of the database rather than restating
# them -- so the bulk rows cannot drift from what the handlers actually write.
#
# Credentials come from `dotnet user-secrets` (the Testing:* identity CLAUDE.md describes). Nothing
# secret is written to this file or to its output.
#
# Usage:  bash tools/scale/seed-master.sh [https://localhost:7104]
# Output: tools/scale/.seed-ids.env  (git-ignored; consumed by run-measurement.sh)

set -uo pipefail

API="${1:-https://localhost:7104}"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
JAR="$HERE/.cookies"
OUT="$HERE/.seed-ids.env"

secret() { dotnet user-secrets list --project "$ROOT/src/Api" 2>/dev/null | sed -n "s/^$1 = //p"; }

EMAIL="$(secret 'Testing:Email')"
PASSWORD="$(secret 'Testing:Password')"
if [[ -z "$EMAIL" || -z "$PASSWORD" ]]; then
  echo "FATAL: Testing:Email / Testing:Password not in user-secrets for src/Api." >&2
  exit 1
fi

# A curl helper that prints its status code for every call (phase-26c: a seed script that pipes
# failures to /dev/null hides them, and the first report just comes back empty). It sets the global
# RESP rather than echoing the body -- a helper that both prints and returns is a trap under $( )
# (phase-34b).
RESP=""
call() {
  local method="$1" path="$2" body="${3:-}"
  local code
  if [[ -n "$body" ]]; then
    RESP="$(curl -sk -X "$method" "$API$path" -b "$JAR" -c "$JAR" \
      -H 'Content-Type: application/json' -d "$body" -w $'\n%{http_code}')"
  else
    RESP="$(curl -sk -X "$method" "$API$path" -b "$JAR" -c "$JAR" -w $'\n%{http_code}')"
  fi
  code="${RESP##*$'\n'}"
  RESP="${RESP%$'\n'*}"
  printf '%-6s %-58s %s\n' "$method" "${path:0:58}" "$code" >&2
  case "$code" in 2*) return 0 ;; *) echo "  body: ${RESP:0:400}" >&2; return 1 ;; esac
}

# jq is not assumed; these two field reads are all the JSON parsing this script needs.
field() { sed -n "s/.*\"$1\":\"\([^\"]*\)\".*/\1/p" <<<"$RESP" | head -1; }

rm -f "$JAR" "$OUT"

echo "== login ==" >&2
call POST /api/auth/login "{\"email\":\"$EMAIL\",\"password\":\"$PASSWORD\"}" || exit 1

STAMP="$(date +%m%d%H%M%S)"
echo "== organization ==" >&2
call POST /api/organizations "$(cat <<JSON
{"name":"Scale 34c $STAMP","industry":"Trading","address":"Kathmandu",
 "accountingStartDate":"2023-07-17","isVatRegistered":true,
 "workspaceName":"scale34c$STAMP","email":"scale$STAMP@example.com","phone":"9800000000",
 "panNumber":"300000000","website":null,
 "trackInventory":true,"multipleLocations":false,"multipleWarehouses":true,
 "multiCurrency":false,"manufacturing":false,"posRetail":false,"posRestaurant":false,
 "turnstileToken":"scale-seed"}
JSON
)" || exit 1
ORG="$(field organizationId)"
[[ -n "$ORG" ]] || { echo "FATAL: no organizationId in $RESP" >&2; exit 1; }
echo "organizationId=$ORG" >&2

# ---- Chart of accounts -----------------------------------------------------------------------
# A fresh organization has zero account groups and zero accounts -- nothing seeds a chart of
# accounts (phase-27a). Root types are singular: Asset/Liability/Equity/Income/Expense.
declare -A GROUP
for root in Asset Liability Equity Income Expense; do
  call POST "/api/organizations/$ORG/account-groups" "{\"name\":\"$root Group\",\"rootType\":\"$root\",\"parentGroupId\":null}" || exit 1
  GROUP[$root]="$(field id)"
done

# `POST /accounts` takes {name, groupId, kind} only -- no code, and kind is an AccountKind
# (phase-34b / phase-32).
declare -A ACCT
mkacct() { # mkacct <var> <name> <root>
  call POST "/api/organizations/$ORG/accounts" \
    "{\"name\":\"$2\",\"groupId\":\"${GROUP[$3]}\",\"kind\":\"$4\"}" || exit 1
  ACCT[$1]="$(field id)"
}
mkacct sales "Sales" Income Other
mkacct ar "Accounts Receivable" Asset Other
mkacct vatpay "VAT Payable" Liability Other
mkacct purchase "Purchase" Expense Other
mkacct ap "Accounts Payable" Liability Other
mkacct vatrec "VAT Receivable" Asset Other
mkacct tds "TDS Payable" Liability Other
mkacct inventory "Inventory" Asset Other
mkacct cogs "Cost of Goods Sold" Expense Other
mkacct invadj "Inventory Adjustment" Expense Other
mkacct prodcost "Production Cost" Expense Other
mkacct forexgain "Forex Gain" Income Other
mkacct forexloss "Forex Loss" Expense Other
mkacct landed "Landed Cost Clearing" Asset Other
mkacct cash       "Cash"                     Asset     Cash

# The GL defaults are ONE PUT taking all eleven (now fourteen) accounts (phase-26c).
call PUT "/api/organizations/$ORG/accounting-defaults" "$(cat <<JSON
{"defaultSalesAccountId":"${ACCT[sales]}","defaultAccountsReceivableId":"${ACCT[ar]}",
 "defaultVatPayableAccountId":"${ACCT[vatpay]}","defaultPurchaseAccountId":"${ACCT[purchase]}",
 "defaultAccountsPayableId":"${ACCT[ap]}","defaultVatReceivableAccountId":"${ACCT[vatrec]}",
 "defaultTdsPayableAccountId":"${ACCT[tds]}","defaultInventoryAccountId":"${ACCT[inventory]}",
 "defaultCogsAccountId":"${ACCT[cogs]}","defaultInventoryAdjustmentAccountId":"${ACCT[invadj]}",
 "defaultProductionCostAccountId":"${ACCT[prodcost]}","defaultForexGainAccountId":"${ACCT[forexgain]}",
 "defaultForexLossAccountId":"${ACCT[forexloss]}","defaultLandedCostClearingAccountId":"${ACCT[landed]}"}
JSON
)" || exit 1

# ---- Master data the documents need ----------------------------------------------------------
call POST "/api/organizations/$ORG/warehouses" '{"name":"Main Warehouse"}' || exit 1
WAREHOUSE="$(field id)"

call POST "/api/organizations/$ORG/units-of-measurement" '{"name":"Piece","shortName":"pc"}' || exit 1
UNIT="$(field id)"

call POST "/api/organizations/$ORG/product-categories" '{"name":"General","parentCategoryId":null}' || exit 1
CATEGORY="$(field id)"

call POST "/api/organizations/$ORG/contact-groups" '{"name":"Customers","parentGroupId":null}' || exit 1
CONTACTGROUP="$(field id)"

# ---- The reference row of each kind ------------------------------------------------------------
call POST "/api/organizations/$ORG/contacts" "$(cat <<JSON
{"type":"Customer","name":"Reference Customer","address":"Lalitpur","pan":"301000001",
 "phone":"9801000001","email":"ref.customer@example.com","groupId":"$CONTACTGROUP","openingBalance":0}
JSON
)" || exit 1
CONTACT="$(field id)"

# Service, deliberately: a Goods line consumes stock regardless of TrackInventory (phase-30), so a
# Service reference invoice is the one whose GL shape the bulk rows can replicate exactly.
call POST "/api/organizations/$ORG/products" "$(cat <<JSON
{"type":"Service","name":"Reference Service","categoryId":"$CATEGORY","primaryUnitId":"$UNIT",
 "hsCode":null,"availableForSale":true,"sellingPrice":1000,"purchasePrice":600,
 "vatRate":"ThirteenPercentVat","reOrderLevel":0,"trackInventory":false,"sku":null,"barcode":null}
JSON
)" || exit 1
PRODUCT="$(field id)"

call POST "/api/organizations/$ORG/invoices" "$(cat <<JSON
{"contactId":"$CONTACT","warehouseId":"$WAREHOUSE","date":"2024-01-15","reference":"REF-SEED",
 "lines":[{"productId":"$PRODUCT","quantity":1,"rate":1000,"vatRate":"ThirteenPercentVat","discountPct":0}]}
JSON
)" || exit 1
INVOICE="$(field id)"

call POST "/api/organizations/$ORG/invoices/$INVOICE/approve" '' || exit 1

# The purchase side, so the P&L has an expense half and the Purchase Register / supplier ageing have
# rows to read. Same Service-line reasoning as the invoice.
call POST "/api/organizations/$ORG/contacts" "$(cat <<JSON
{"type":"Supplier","name":"Reference Supplier","address":"Bhaktapur","pan":"302000001",
 "phone":"9802000001","email":"ref.supplier@example.com","groupId":null,"openingBalance":0}
JSON
)" || exit 1
SUPPLIER="$(field id)"

call POST "/api/organizations/$ORG/purchase-bills" "$(cat <<JSON
{"contactId":"$SUPPLIER","warehouseId":"$WAREHOUSE","date":"2024-01-20","reference":"REF-PB",
 "supplierInvoiceReference":"SUP-0001","isImport":false,"importCountry":null,"importDate":null,
 "importDocumentNo":null,"tdsTypeId":null,
 "lines":[{"productId":"$PRODUCT","quantity":1,"rate":600,"vatRate":"ThirteenPercentVat",
           "expenditureClassification":"Others","discountPct":0}]}
JSON
)" || exit 1
BILL="$(field id)"

call POST "/api/organizations/$ORG/purchase-bills/$BILL/approve" '' || exit 1

cat >"$OUT" <<ENV
ORG=$ORG
SUPPLIER=$SUPPLIER
BILL=$BILL
WAREHOUSE=$WAREHOUSE
UNIT=$UNIT
CATEGORY=$CATEGORY
CONTACTGROUP=$CONTACTGROUP
CONTACT=$CONTACT
PRODUCT=$PRODUCT
INVOICE=$INVOICE
ACCT_SALES=${ACCT[sales]}
ACCT_AR=${ACCT[ar]}
ACCT_VATPAY=${ACCT[vatpay]}
ACCT_CASH=${ACCT[cash]}
ENV

echo >&2
echo "wrote $OUT" >&2
cat "$OUT" >&2
