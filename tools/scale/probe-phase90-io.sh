#!/usr/bin/env bash
# Phase 90 -- logical reads for the two "measure before narrowing" debts phase 88 and phase 89 left:
#   * the VAT return reads the registers from the beginning of the books (phase 89 Decision G);
#   * OutstandingDocumentReader's settlement queries stay organization-wide when a payment form asks
#     for ONE contact's open documents (phase 88).
#
# Same method as probe-cheque-io.sh (phase 50): per path, clear the plan cache, issue the request
# three times, then sum the per-execution average logical reads, CPU and elapsed time of every cached
# statement that touches one of the document tables. Logical reads are a property of the plan, not
# of the machine. The client's median wall time is printed beside them, for scale only.
#
# Run tools/scale/seed-phase90.sql against the tenant first: without it the settlement tables are
# empty and a per-contact picker looks cheap for the wrong reason.
#
# Usage:  bash tools/scale/probe-phase90-io.sh <label> [api]
# Writes: tools/scale/probe-phase90-<label>.csv

set -uo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
LABEL="${1:-run}"
API="${2:-https://localhost:7104}"
SQLSRV='DESKTOP-H0R00ME\SQLEXPRESS'
JAR="$HERE/.cookies-probe"
CSV="$HERE/probe-phase90-$LABEL.csv"

# shellcheck source=/dev/null
source "$HERE/.seed-ids.env"

secret() { dotnet user-secrets list --project "$ROOT/src/Api" 2>/dev/null | sed -n "s/^$1 = //p"; }
EMAIL="$(secret 'Testing:Email')"
PASSWORD="$(secret 'Testing:Password')"

rm -f "$JAR"
login_code="$(curl -sk -o /dev/null -w '%{http_code}' -X POST "$API/api/auth/login" -c "$JAR" \
  -H 'Content-Type: application/json' -d "{\"email\":\"$EMAIL\",\"password\":\"$PASSWORD\"}")"
[[ "$login_code" == "200" ]] || { echo "FATAL: login returned $login_code" >&2; exit 1; }

# The tenant must hold the settlement rows, or the picker paths measure empty tables.
tenders="$(sqlcmd -S "$SQLSRV" -d ErpApp -E -C -h -1 -W -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM sales.InvoiceTenders t JOIN sales.Invoices i ON i.Id = t.InvoiceId WHERE i.OrganizationId = '$ORG';" | tr -d ' \r')"
[[ "$tenders" -gt 1000 ]] || { echo "FATAL: $tenders tenders on $ORG -- run seed-phase90.sql first" >&2; exit 1; }

# A customer and a supplier that each hold documents.
CUSTOMER="$(sqlcmd -S "$SQLSRV" -d ErpApp -E -C -h -1 -W -Q "SET NOCOUNT ON; SELECT TOP 1 ContactId FROM sales.Invoices WHERE OrganizationId = '$ORG' AND Status = 'Approved' ORDER BY Code;" | tr -d ' \r')"
SUPPLIER_ID="$(sqlcmd -S "$SQLSRV" -d ErpApp -E -C -h -1 -W -Q "SET NOCOUNT ON; SELECT TOP 1 ContactId FROM purchasing.PurchaseBills WHERE OrganizationId = '$ORG' AND Status = 'Approved' ORDER BY Code;" | tr -d ' \r')"

B="/api/organizations/$ORG"

PATHS=$(cat <<LIST
vat.fy2083	$B/reports/vat-summary?fiscalYear=2083
vat.fy2082	$B/reports/vat-summary?fiscalYear=2082
vat.fy2080	$B/reports/vat-summary?fiscalYear=2080
picker.customer	$B/payments/open-allocation-targets?contactId=$CUSTOMER&direction=Received&currencyCode=NPR
picker.supplier	$B/payments/open-allocation-targets?contactId=$SUPPLIER_ID&direction=Paid&currencyCode=NPR
suggest.customer	$B/payments/default-allocations?contactId=$CUSTOMER&amount=1000&direction=Received&currencyCode=NPR
ageing.customers	$B/reports/customer-ageing-summary?asOfDate=2026-09-30&page=1&pageSize=50
ageing.suppliers	$B/reports/supplier-ageing-summary?asOfDate=2026-09-30&page=1&pageSize=50
LIST
)

printf 'path,status,statements,avg_logical_reads,avg_cpu_ms,avg_elapsed_ms,median_wall_ms\n' >"$CSV"
printf '%-18s %6s %10s %18s %11s %14s %14s\n' path status statements avg_logical_reads avg_cpu_ms avg_elapsed_ms median_wall_ms

while IFS=$'\t' read -r name path; do
  [[ -n "$name" ]] || continue

  sqlcmd -S "$SQLSRV" -d ErpApp -E -C -h -1 -Q "DBCC FREEPROCCACHE WITH NO_INFOMSGS;" >/dev/null 2>&1

  walls=()
  status=""
  for _ in 1 2 3; do
    out="$(curl -sk -o /dev/null -w '%{http_code} %{time_total}' -b "$JAR" "$API$path")"
    status="${out%% *}"
    walls+=("$(awk -v t="${out##* }" 'BEGIN { printf "%d", t * 1000 }')")
  done
  median="$(printf '%s\n' "${walls[@]}" | sort -n | sed -n 2p)"

  IFS=',' read -r stmts reads cpu elapsed <<<"$(sqlcmd -S "$SQLSRV" -d ErpApp -E -C -h -1 -W -s',' -Q "
    SET NOCOUNT ON;
    SELECT COUNT(*),
           ISNULL(SUM(qs.total_logical_reads / qs.execution_count), 0),
           CAST(ISNULL(SUM(qs.total_worker_time / qs.execution_count), 0) / 1000.0 AS decimal(10,1)),
           CAST(ISNULL(SUM(qs.total_elapsed_time / qs.execution_count), 0) / 1000.0 AS decimal(10,1))
    FROM sys.dm_exec_query_stats qs
    CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) t
    WHERE t.text NOT LIKE '%dm_exec_query_stats%'
      AND (t.text LIKE '%[[]Invoice%' OR t.text LIKE '%[[]CreditNote%' OR t.text LIKE '%[[]PurchaseBill%'
           OR t.text LIKE '%[[]DebitNote%' OR t.text LIKE '%[[]Expense%' OR t.text LIKE '%[[]PaymentAllocations]%'
           OR t.text LIKE '%[[]JournalVoucher%' OR t.text LIKE '%[[]Contacts]%' OR t.text LIKE '%[[]Payments]%');" 2>/dev/null | tr -d ' \r')"

  printf '%-18s %6s %10s %18s %11s %14s %14s\n' "$name" "$status" "$stmts" "$reads" "$cpu" "$elapsed" "$median"
  printf '%s,%s,%s,%s,%s,%s,%s\n' "$name" "$status" "$stmts" "$reads" "$cpu" "$elapsed" "$median" >>"$CSV"
done <<<"$PATHS"
