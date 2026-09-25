"""Phase 58 -- drive probe-phase58-io.sql once per index variant and tabulate logical reads.

Each variant changes the physical table's indexes, measures all three paths, and the script ends by
restoring the shipped pair exactly as EF named them. Logical reads, never the wall clock (phase 50).

Usage (from tools/scale): python run-probe-phase58.py <server> <org> <warehouse>
"""
import re
import subprocess
import sys

server, org, wh = sys.argv[1:4]
BASE = ['sqlcmd', '-S', server, '-d', 'ErpApp', '-E', '-C']
T = 'inventory.PhysicalStockMovements'
PRODUCT_IX = 'IX_PhysicalStockMovements_OrganizationId_ProductId_WarehouseId_TransactionDate'
SOURCE_IX = 'IX_PhysicalStockMovements_OrganizationId_SourceDocumentType_SourceDocumentId'
PLAIN_PRODUCT = f'CREATE INDEX {PRODUCT_IX} ON {T} (OrganizationId, ProductId, WarehouseId, TransactionDate)'
# What migration Phase58CoveringPhysicalIndex ships, and what restore() puts back.
SHIPPED_PRODUCT = PLAIN_PRODUCT + ' INCLUDE (Direction, Quantity)'
SHIPPED_SOURCE = f'CREATE INDEX {SOURCE_IX} ON {T} (OrganizationId, SourceDocumentType, SourceDocumentId)'


def q(sql):
    out = subprocess.run(BASE + ['-W', '-h', '-1', '-Q', 'SET NOCOUNT ON; SET QUOTED_IDENTIFIER ON; ' + sql],
                         capture_output=True, text=True)
    if out.returncode or 'Msg ' in out.stdout:
        raise SystemExit(out.stdout + out.stderr)
    return [l.strip() for l in out.stdout.splitlines() if l.strip()]


def drop(name):
    q(f"IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = '{name}') DROP INDEX {name} ON {T}")


def restore():
    drop(PRODUCT_IX)
    drop(SOURCE_IX)
    q(SHIPPED_PRODUCT)
    q(SHIPPED_SOURCE)


# The busiest seeded product (every product carries NumRows / NumProducts rows) and one document.
prod = q(f"SELECT TOP 1 ProductId FROM {T} WHERE OrganizationId = '{org}' AND CreatedAt = '2000-01-01T00:00:00+00:00' "
         f"GROUP BY ProductId ORDER BY COUNT(*) DESC, ProductId")[0]
doc = q(f"SELECT TOP 1 SourceDocumentId FROM {T} WHERE OrganizationId = '{org}' AND SourceDocumentType = N'DeliveryNote' "
        f"AND CreatedAt = '2000-01-01T00:00:00+00:00' ORDER BY SourceDocumentId")[0]


def measure():
    q(f'UPDATE STATISTICS {T} WITH FULLSCAN')
    out = subprocess.run(BASE + ['-i', 'probe-phase58-io.sql', '-v', f'ORG={org}', f'PROD={prod}', f'WH={wh}', f'DOC={doc}'],
                         capture_output=True, text=True).stdout
    result, path = {}, None
    for line in out.splitlines():
        m = re.match(r'=== (\w)\.', line)
        if m:
            path = m.group(1)
        # Anchor on what precedes the number: a bare greedy match reads "lob logical reads 0" (phase 54).
        m = re.search(r"Table 'PhysicalStockMovements'\. Scan count \d+, logical reads (\d+)", line)
        if m and path:
            result[path] = result.get(path, 0) + int(m.group(1))
    return result


variants = [
    ('shipped: covering product index + source index', lambda: restore()),
    ('product index without INCLUDE (first scaffold)', lambda: (restore(), drop(PRODUCT_IX), q(PLAIN_PRODUCT))),
    ('no source index', lambda: (restore(), drop(SOURCE_IX))),
    ('no product index', lambda: (restore(), drop(PRODUCT_IX))),
    ('neither index', lambda: (restore(), drop(PRODUCT_IX), drop(SOURCE_IX))),
]

rows = q(f"SELECT COUNT(*) FROM {T} WHERE OrganizationId = '{org}'")[0]
print(f'{rows} rows on the tenant; probe product {prod}, document {doc}\n')
print(f"{'variant':48} {'A report':>10} {'B availability':>15} {'C void':>8}")
try:
    for name, setup in variants:
        setup()
        r = measure()
        print(f"{name:48} {r.get('A', 0):>10} {r.get('B', 0):>15} {r.get('C', 0):>8}")
finally:
    restore()
    print('\nshipped indexes restored')
