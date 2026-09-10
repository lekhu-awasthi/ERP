#!/usr/bin/env bash
# Phase 34c -- the measurement harness. Decision C (docs/phase-34a-status.md) fixes what is measured:
#
#   p95 wall time for each paginated list's FIRST page; the same list's LAST page (offset pagination
#   degrades at the tail, and every list here uses Skip/Take); the three financial statements; the
#   two heaviest registers; and global search.
#
# Everything here is server wall time as the client sees it (curl's %{time_total} against a
# localhost API over HTTPS), which is the number NFR-5.1 is about. Each row is RUNS timed samples
# after WARMUP untimed ones, and the report prints p50/p95/max WITH the run count -- a single number
# would not be a measurement.
#
# Usage:  bash tools/scale/run-measurement.sh <label> [runs] [api]
#   e.g.  bash tools/scale/run-measurement.sh before-seed
#         bash tools/scale/run-measurement.sh after-seed 12
#
# Reads tools/scale/.seed-ids.env for the organization, and writes
# tools/scale/results-<label>.csv  (raw, one row per endpoint)

set -uo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
LABEL="${1:-run}"
RUNS="${2:-10}"
API="${3:-https://localhost:7104}"
WARMUP=2
JAR="$HERE/.cookies-measure"
CSV="$HERE/results-$LABEL.csv"

# shellcheck source=/dev/null
source "$HERE/.seed-ids.env"

secret() { dotnet user-secrets list --project "$ROOT/src/Api" 2>/dev/null | sed -n "s/^$1 = //p"; }
EMAIL="$(secret 'Testing:Email')"
PASSWORD="$(secret 'Testing:Password')"

rm -f "$JAR"
login_code="$(curl -sk -o /dev/null -w '%{http_code}' -X POST "$API/api/auth/login" -c "$JAR" \
  -H 'Content-Type: application/json' -d "{\"email\":\"$EMAIL\",\"password\":\"$PASSWORD\"}")"
[[ "$login_code" == "200" ]] || { echo "FATAL: login returned $login_code" >&2; exit 1; }

B="/api/organizations/$ORG"
FROM=2023-07-17
TO="$(date +%Y-%m-%d)"

# Assert the organization actually holds the dataset before timing anything against it.
#
# This is not paranoia: seed-master.sh rewrites .seed-ids.env every time it runs, so seeding a
# second organization silently repoints the harness at it, and a run against an empty tenant looks
# exactly like a spectacularly fast one -- 20 ms p95 across the board, every status code 200. The
# only thing that distinguishes it is the response size, which is why that is what this checks.
probe_bytes="$(curl -sk -o /dev/null -w '%{size_download}' -b "$JAR" "$API$B/invoices?page=1&pageSize=50")"
if [[ "$probe_bytes" -lt 1000 ]]; then
  echo "FATAL: organization $ORG returned ${probe_bytes} bytes for a 50-row invoice page -- it is not" >&2
  echo "       seeded. Point tools/scale/.seed-ids.env at the seeded organization and re-run." >&2
  exit 1
fi

# name<TAB>path. The four volume lists get first page, last page, a search that hits, a search that
# misses and a date range; every other list is here at its first page so the per-request floor is
# visible next to them.
ENDPOINTS=$(cat <<LIST
invoices.page1	$B/invoices?page=1&pageSize=50
invoices.last	$B/invoices?page=1000&pageSize=50
invoices.search.hit	$B/invoices?page=1&pageSize=50&search=SO-4
invoices.search.miss	$B/invoices?page=1&pageSize=50&search=ZZQQXX
invoices.daterange	$B/invoices?page=1&pageSize=50&fromDate=2025-01-01&toDate=2025-03-31
invoices.status	$B/invoices?page=1&pageSize=50&status=Draft
contacts.page1	$B/contacts?page=1&pageSize=50
contacts.last	$B/contacts?page=1000&pageSize=50
contacts.search.hit	$B/contacts?page=1&pageSize=50&search=Everest
contacts.search.miss	$B/contacts?page=1&pageSize=50&search=ZZQQXX
products.page1	$B/products?page=1&pageSize=50
products.last	$B/products?page=400&pageSize=50
products.search.hit	$B/products?page=1&pageSize=50&search=Calibration
purchase-bills.page1	$B/purchase-bills?page=1&pageSize=50
purchase-bills.last	$B/purchase-bills?page=400&pageSize=50
accounts.page1	$B/accounts?page=1&pageSize=50
payments.page1	$B/payments?page=1&pageSize=50
journal-vouchers.page1	$B/journal-vouchers?page=1&pageSize=50
stmt.trial-balance	$B/reports/trial-balance?asOfDate=$TO
stmt.balance-sheet	$B/reports/balance-sheet?asOfDate=$TO
stmt.income-statement	$B/reports/income-statement?fromDate=$FROM&toDate=$TO
stmt.income-statement.compare	$B/reports/income-statement?fromDate=$FROM&toDate=$TO&compare=true
reg.sales-register	$B/reports/sales-register?fromDate=$FROM&toDate=$TO&page=1&pageSize=50
reg.sales-register.last	$B/reports/sales-register?fromDate=$FROM&toDate=$TO&page=1000&pageSize=50
reg.purchase-register	$B/reports/purchase-register?fromDate=$FROM&toDate=$TO&page=1&pageSize=50
rep.journal-report	$B/reports/journal-report?fromDate=$FROM&toDate=$TO&page=1&pageSize=50
rep.detail-general-ledger	$B/reports/detail-general-ledger?fromDate=$FROM&toDate=$TO&page=1&pageSize=50
rep.general-ledger-summary	$B/reports/general-ledger-summary?fromDate=$FROM&toDate=$TO&page=1&pageSize=50
rep.customer-ageing	$B/reports/customer-ageing-summary?asOfDate=$TO&page=1&pageSize=50
rep.sales-by-customer	$B/reports/sales-by-customer?fromDate=$FROM&toDate=$TO&page=1&pageSize=50
search.common	$B/search?term=Everest
search.code	$B/search?term=SVC-01
search.miss	$B/search?term=ZZQQXX
LIST
)

printf 'endpoint,runs,http,bytes,p50_ms,p95_ms,max_ms\n' >"$CSV"
printf '%-32s %6s %8s %9s %9s %9s %10s\n' endpoint http rows p50_ms p95_ms max_ms bytes

while IFS=$'\t' read -r name path; do
  [[ -n "$name" ]] || continue

  code=0; bytes=0
  for ((i = 0; i < WARMUP; i++)); do
    read -r code bytes <<<"$(curl -sk -o /dev/null -w '%{http_code} %{size_download}' -b "$JAR" "$API$path")"
  done

  samples=()
  for ((i = 0; i < RUNS; i++)); do
    t="$(curl -sk -o /dev/null -w '%{time_total}' -b "$JAR" "$API$path")"
    samples+=("$t")
  done

  read -r p50 p95 mx <<<"$(printf '%s\n' "${samples[@]}" | sort -n | awk '
    { a[NR] = $1 }
    END {
      # Nearest-rank percentile: the smallest sample at or above the p-th position. With RUNS=10
      # p95 is the top sample, which is deliberately the pessimistic reading of a small run.
      p50 = a[int((NR * 0.50) + 0.9999)]; p95 = a[int((NR * 0.95) + 0.9999)];
      printf "%.1f %.1f %.1f", p50 * 1000, p95 * 1000, a[NR] * 1000
    }')"

  printf '%-32s %6s %8s %9s %9s %9s %10s\n' "$name" "$code" "-" "$p50" "$p95" "$mx" "$bytes"
  printf '%s,%s,%s,%s,%s,%s,%s\n' "$name" "$RUNS" "$code" "$bytes" "$p50" "$p95" "$mx" >>"$CSV"
done <<<"$ENDPOINTS"

echo
echo "wrote $CSV  (runs=$RUNS, warmup=$WARMUP, org=$ORG)"
