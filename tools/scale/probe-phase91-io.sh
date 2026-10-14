#!/usr/bin/env bash
# Phase 91 -- logical reads for the till refund's owed-on-sale read (phase 90 § 5): PosRefundPlanner.OwedOnSaleAsync
# names the sale's contact, which on a till is the walk-in, who owns every counter sale -- so phase 90's narrowing by
# contact leaves it reading all of them. Measured through the refund screen's own read of a sale
# (GET .../pos/tills/{location}/sales/{invoice}/refundable), which calls it once.
#
# Same method as probe-phase90-io.sh: per path, clear the plan cache, issue the request three times, then sum the
# per-execution average logical reads, CPU and elapsed time of every cached statement touching a document table.
#
# Run tools/scale/seed-phase91.sql against the tenant first: without it the walk-in owns no sales and the read is
# cheap for the wrong reason.
#
# Usage:  bash tools/scale/probe-phase91-io.sh <label> [api]
# Writes: tools/scale/probe-phase91-<label>.csv

set -uo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
LABEL="${1:-run}"
API="${2:-https://localhost:7104}"
SQLSRV='DESKTOP-H0R00ME\SQLEXPRESS'
JAR="$HERE/.cookies-probe"
CSV="$HERE/probe-phase91-$LABEL.csv"

# shellcheck source=/dev/null
source "$HERE/.seed-ids.env"

secret() { dotnet user-secrets list --project "$ROOT/src/Api" 2>/dev/null | sed -n "s/^$1 = //p"; }
EMAIL="$(secret 'Testing:Email')"
PASSWORD="$(secret 'Testing:Password')"

rm -f "$JAR"
login_code="$(curl -sk -o /dev/null -w '%{http_code}' -X POST "$API/api/auth/login" -c "$JAR" \
  -H 'Content-Type: application/json' -d "{\"email\":\"$EMAIL\",\"password\":\"$PASSWORD\"}")"
[[ "$login_code" == "200" ]] || { echo "FATAL: login returned $login_code" >&2; exit 1; }

q() { sqlcmd -S "$SQLSRV" -d ErpApp -E -C -h -1 -W -Q "SET NOCOUNT ON; $1" | tr -d ' \r'; }

# The walk-in must own the till sales, or the path measures a contact with one invoice.
owned="$(q "SELECT COUNT(*) FROM sales.Invoices i JOIN contacts.Contacts c ON c.Id = i.ContactId WHERE i.OrganizationId = '$ORG' AND i.Channel = 'Pos' AND c.IsWalkInCustomer = 1;")"
[[ "$owned" -gt 1000 ]] || { echo "FATAL: the walk-in owns $owned till sales on $ORG -- run seed-phase91.sql first" >&2; exit 1; }

LOCATION="$(q "SELECT Id FROM tenancy.BillingLocations WHERE OrganizationId = '$ORG' AND PosMode = 'Retail';")"
# A sale with nothing refunded, and one an earlier refund has already taken a line of.
FRESH="$(q "SELECT TOP 1 i.Id FROM sales.Invoices i WHERE i.OrganizationId = '$ORG' AND i.Channel = 'Pos' AND NOT EXISTS (SELECT 1 FROM sales.CreditNotes n WHERE n.ReferrerId = i.Id) ORDER BY i.Code DESC;")"
REFUNDED="$(q "SELECT TOP 1 n.ReferrerId FROM sales.CreditNotes n WHERE n.OrganizationId = '$ORG' AND n.Channel = 'Pos' ORDER BY n.Code;")"

B="/api/organizations/$ORG/pos/tills/$LOCATION/sales"

PATHS=$(cat <<LIST
refundable.fresh	$B/$FRESH/refundable
refundable.refunded	$B/$REFUNDED/refundable
LIST
)

printf 'path,status,statements,avg_logical_reads,avg_cpu_ms,avg_elapsed_ms,median_wall_ms\n' >"$CSV"
printf '%-20s %6s %10s %18s %11s %14s %14s\n' path status statements avg_logical_reads avg_cpu_ms avg_elapsed_ms median_wall_ms

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
      AND (t.text LIKE '%[[]Invoice%' OR t.text LIKE '%[[]CreditNote%' OR t.text LIKE '%[[]PaymentAllocations]%'
           OR t.text LIKE '%[[]JournalVoucher%' OR t.text LIKE '%[[]Contacts]%' OR t.text LIKE '%[[]Payments]%');" 2>/dev/null | tr -d ' \r')"

  printf '%-20s %6s %10s %18s %11s %14s %14s\n' "$name" "$status" "$stmts" "$reads" "$cpu" "$elapsed" "$median"
  printf '%s,%s,%s,%s,%s,%s,%s\n' "$name" "$status" "$stmts" "$reads" "$cpu" "$elapsed" "$median" >>"$CSV"
done <<<"$PATHS"
