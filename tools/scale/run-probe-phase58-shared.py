"""Phase 58 -- drive probe-phase58-shared-io.sql with the StockMovements composite plain and covering.

The index belongs to the accounting ledger; phase 58 made it covering because the physical balance
sums this table for the shared types. The script ends by restoring the SHIPPED (covering) shape,
which is what migration Phase58CoveringStockIndexes creates. Logical reads, never the wall clock
(phase 50).

Usage (from tools/scale): python run-probe-phase58-shared.py <server> <org> <warehouse>
"""
import re
import subprocess
import sys

server, org, wh = sys.argv[1:4]
BASE = ['sqlcmd', '-S', server, '-d', 'ErpApp', '-E', '-C']
IX = 'IX_StockMovements_OrganizationId_ProductId_WarehouseId_TransactionDate'
T = 'inventory.StockMovements'
PLAIN = f'CREATE INDEX {IX} ON {T} (OrganizationId, ProductId, WarehouseId, TransactionDate)'
SHIPPED = PLAIN + ' INCLUDE (Direction, Quantity, SourceDocumentType)'


def q(sql):
    out = subprocess.run(BASE + ['-W', '-h', '-1', '-Q', 'SET NOCOUNT ON; SET QUOTED_IDENTIFIER ON; ' + sql],
                         capture_output=True, text=True)
    if out.returncode or 'Msg ' in out.stdout:
        raise SystemExit(out.stdout + out.stderr)
    return [l.strip() for l in out.stdout.splitlines() if l.strip()]


prod = q(f"SELECT TOP 1 ProductId FROM {T} WHERE OrganizationId = '{org}' AND CreatedAt = '2000-01-01T00:00:00+00:00' "
         f"GROUP BY ProductId ORDER BY COUNT(*) DESC, ProductId")[0]


def measure():
    q(f'UPDATE STATISTICS {T} WITH FULLSCAN')
    out = subprocess.run(BASE + ['-i', 'probe-phase58-shared-io.sql', '-v', f'ORG={org}', f'PROD={prod}', f'WH={wh}'],
                         capture_output=True, text=True).stdout
    result, path = {}, None
    for line in out.splitlines():
        m = re.match(r'=== (\w)\.', line)
        if m:
            path = m.group(1)
        # Anchor on what precedes the number: a bare greedy match reads "lob logical reads 0" (phase 54).
        m = re.search(r"Table 'StockMovements'\. Scan count \d+, logical reads (\d+)", line)
        if m and path:
            result[path] = result.get(path, 0) + int(m.group(1))
    return result


def set_index(sql):
    q(f'DROP INDEX {IX} ON {T}')
    q(sql)


rows = q(f"SELECT COUNT(*) FROM {T} WHERE OrganizationId = '{org}'")[0]
print(f'{rows} accounting movements on the tenant; probe product {prod}\n')
print(f"{'variant':60} {'S shared half':>14} {'R shared load':>14} {'L history':>10}")
try:
    for name, sql in [
        ('before phase 58: plain composite', PLAIN),
        ('shipped: covering (Direction, Quantity, SourceDocumentType)', SHIPPED),
    ]:
        set_index(sql)
        r = measure()
        print(f"{name:60} {r.get('S', 0):>14} {r.get('R', 0):>14} {r.get('L', 0):>10}")
finally:
    set_index(SHIPPED)
    print('\nshipped (covering) index restored')
