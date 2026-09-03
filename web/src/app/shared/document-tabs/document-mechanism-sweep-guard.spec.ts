/// <reference types="vite/client" />

/**
 * Phase 27a, <b>the client half of proving the sweep complete.</b>
 *
 * The server guard (DocumentMechanismSweepGuardTests) proves every document type resolves a
 * permission key, an existence check and a parent-enum counterpart. None of that says the screen
 * actually renders the control. This does: it reads every detail-page template off disk at test
 * time and fails the build when one is missing a mechanism its document type is supposed to carry.
 *
 * The failure mode being guarded is the one a sweep phase actually dies of -- phase 29 adding a
 * document type, wiring the backend because the compiler makes it, and quietly shipping a detail
 * page with no Custom Fields block. Nobody notices for a year, and then a tenant asks where their
 * field went.
 *
 * Same shape and same two self-checks as phase-23's sweep-guard.spec.ts: the scan must find files at
 * all (so a broken glob cannot make every assertion pass vacuously), and every entry in the table
 * must point at a template that still exists.
 */
const templates = import.meta.glob('/src/app/features/**/*.html', {
  query: '?raw',
  import: 'default',
  eager: true,
}) as Record<string, string>;

/** One row per document type, naming the page that must carry that type's mechanisms. */
interface SweptPage {
  readonly documentType: string;
  readonly template: string;
  /** False only for the two types live-confirmed to have no Custom Fields section at all. */
  readonly customFields: boolean;
}

/**
 * Every transactional document type and the detail page that owns it. Payment appears twice
 * deliberately: this codebase splits Customer and Supplier payment into two components over one
 * aggregate, and both screens must carry the mechanisms.
 *
 * This list is explicit rather than derived, because there is no reliable way to infer "which
 * template belongs to DocumentType X" from disk -- and the thing that keeps it honest is the server
 * guard, which fails independently if a DocumentType exists that nobody has classified.
 */
const SWEPT_PAGES: readonly SweptPage[] = [
  { documentType: 'Quotation', template: '/src/app/features/sales/quotation-detail-page/quotation-detail-page.html', customFields: true },
  { documentType: 'SalesOrder', template: '/src/app/features/sales/sales-order-detail-page/sales-order-detail-page.html', customFields: true },
  { documentType: 'Invoice', template: '/src/app/features/sales/invoice-detail-page/invoice-detail-page.html', customFields: true },
  { documentType: 'CreditNote', template: '/src/app/features/sales/credit-note-detail-page/credit-note-detail-page.html', customFields: true },
  { documentType: 'Payment', template: '/src/app/features/sales/payment-detail-page/payment-detail-page.html', customFields: true },
  { documentType: 'Payment', template: '/src/app/features/purchasing/supplier-payment-detail-page/supplier-payment-detail-page.html', customFields: true },
  { documentType: 'PurchaseOrder', template: '/src/app/features/purchasing/purchase-order-detail-page/purchase-order-detail-page.html', customFields: true },
  { documentType: 'PurchaseBill', template: '/src/app/features/purchasing/purchase-bill-detail-page/purchase-bill-detail-page.html', customFields: true },
  { documentType: 'Expense', template: '/src/app/features/purchasing/expense-detail-page/expense-detail-page.html', customFields: true },
  { documentType: 'DebitNote', template: '/src/app/features/purchasing/debit-note-detail-page/debit-note-detail-page.html', customFields: true },
  { documentType: 'JournalVoucher', template: '/src/app/features/accounting/journal-voucher-detail-page/journal-voucher-detail-page.html', customFields: true },
  { documentType: 'CashTransfer', template: '/src/app/features/accounting/cash-transfer-detail-page/cash-transfer-detail-page.html', customFields: true },
  // Live-confirmed: Configurations > Custom Fields renders 16 sections and neither of these two is
  // among them, so they carry Reporting Tags and the tabs but no Custom Fields block.
  { documentType: 'WarehouseTransfer', template: '/src/app/features/inventory/warehouse-transfer-detail-page/warehouse-transfer-detail-page.html', customFields: false },
  { documentType: 'InventoryAdjustment', template: '/src/app/features/inventory/inventory-adjustment-detail-page/inventory-adjustment-detail-page.html', customFields: false },
  { documentType: 'ProductionOrder', template: '/src/app/features/manufacturing/production-order-detail-page/production-order-detail-page.html', customFields: true },
  { documentType: 'ProductionJournal', template: '/src/app/features/manufacturing/production-journal-detail-page/production-journal-detail-page.html', customFields: true },
];

/** The four types whose LIST grid carries a custom-status picker (phase 20b's third shape). */
const CUSTOM_STATUS_PAGES: readonly { documentType: string; template: string }[] = [
  { documentType: 'Quotation', template: '/src/app/features/sales/quotation-list-page/quotation-list-page.html' },
  { documentType: 'SalesOrder', template: '/src/app/features/sales/sales-order-list-page/sales-order-list-page.html' },
  { documentType: 'PurchaseOrder', template: '/src/app/features/purchasing/purchase-order-list-page/purchase-order-list-page.html' },
  { documentType: 'ProductionOrder', template: '/src/app/features/manufacturing/production-order-list-page/production-order-list-page.html' },
];

/** The Opening Balances screen tags per row, one document type per tab. */
const OPENING_BALANCE_TEMPLATE = '/src/app/features/configuration/opening-balances-page/opening-balances-page.html';

function source(path: string): string {
  const found = templates[path];
  expect(found, `template not found on disk: ${path}`).toBeDefined();
  return found;
}

/** `documentType="X"` on the given element, tolerating attribute order and line breaks. */
function declares(html: string, element: string, documentType: string): boolean {
  const tag = new RegExp(`<${element}\\b[\\s\\S]*?/>`, 'g');
  return [...html.matchAll(tag)].some((m) => m[0].includes(`documentType="${documentType}"`));
}

describe('Phase 27a document-mechanism sweep completeness', () => {
  it('finds the templates to scan at all (guards against a glob that silently matches nothing)', () => {
    // Without this, a broken glob would make every assertion below pass vacuously -- which is the
    // classic way a guard test stops guarding anything.
    expect(Object.keys(templates).length).toBeGreaterThan(60);
  });

  it('lists a template that still exists for every swept page', () => {
    for (const page of [...SWEPT_PAGES, ...CUSTOM_STATUS_PAGES, { template: OPENING_BALANCE_TEMPLATE }]) {
      expect(templates[page.template], `swept template no longer exists: ${page.template}`).toBeDefined();
    }
  });

  it('gives every transactional detail page the Tasks / Documents / Activity tabs', () => {
    const missing = SWEPT_PAGES.filter((page) => !source(page.template).includes('<app-document-tabs')).map(
      (page) => page.template,
    );
    expect(
      missing,
      `Add <app-document-tabs> to these detail pages:\n  ${missing.join('\n  ')}`,
    ).toEqual([]);
  });

  it('wraps every detail page body behind the tabs, so a non-Overview tab actually replaces it', () => {
    // A page that renders the strip but never checks isOverview() would show its own body under
    // every tab -- the tabs would look wired and do nothing.
    const missing = SWEPT_PAGES.filter((page) => !/@if \(\w+\.isOverview\(\)\)/.test(source(page.template))).map(
      (page) => page.template,
    );
    expect(
      missing,
      `These pages render the tab strip but never gate their body on it:\n  ${missing.join('\n  ')}`,
    ).toEqual([]);
  });

  it('gives every transactional detail page a reporting-tags editor for its own document type', () => {
    const missing = SWEPT_PAGES.filter(
      (page) => !declares(source(page.template), 'app-reporting-tags-editor', page.documentType),
    ).map((page) => `${page.documentType} — ${page.template}`);
    expect(
      missing,
      `Add <app-reporting-tags-editor documentType="..."> to:\n  ${missing.join('\n  ')}`,
    ).toEqual([]);
  });

  it('gives every custom-fields document type an editor for its own document type', () => {
    const missing = SWEPT_PAGES.filter(
      (page) => page.customFields && !declares(source(page.template), 'app-custom-fields-editor', page.documentType),
    ).map((page) => `${page.documentType} — ${page.template}`);
    expect(
      missing,
      `Add <app-custom-fields-editor documentType="..."> to:\n  ${missing.join('\n  ')}`,
    ).toEqual([]);
  });

  it('keeps the custom-fields editor off the two types that have no such block in the product', () => {
    // The inverse assertion matters as much as the positive one: without it, "sweep everything"
    // would look like the safe default and the live-confirmed narrower list would quietly widen.
    const wrongly = SWEPT_PAGES.filter(
      (page) => !page.customFields && source(page.template).includes('<app-custom-fields-editor'),
    ).map((page) => `${page.documentType} — ${page.template}`);
    expect(
      wrongly,
      `Warehouse Transfer and Inventory Adjustment carry no Custom Fields section in the reference\n` +
        `product (live-confirmed). Remove it from:\n  ${wrongly.join('\n  ')}`,
    ).toEqual([]);
  });

  it('gives every custom-status document type a picker in its list grid', () => {
    const missing = CUSTOM_STATUS_PAGES.filter(
      (page) => !declares(source(page.template), 'app-custom-status-picker', page.documentType),
    ).map((page) => `${page.documentType} — ${page.template}`);
    expect(
      missing,
      `Custom status lives in the LIST grid, not the detail page (phase 20b). Add\n` +
        `<app-custom-status-picker documentType="..."> to:\n  ${missing.join('\n  ')}`,
    ).toEqual([]);
  });

  it('keeps the custom-status picker out of every detail page', () => {
    // Phase 20b's most surprising finding: this control has no detail-page presence at all. A
    // future reader "completing" the sweep by adding it to the detail pages would be undoing a
    // live-confirmed decision, so the guard says so.
    const wrongly = SWEPT_PAGES.filter((page) => source(page.template).includes('<app-custom-status-picker')).map(
      (page) => page.template,
    );
    expect(
      wrongly,
      `Custom status renders only in the list grid (live-confirmed, phase 20b). Remove it from:\n  ${wrongly.join('\n  ')}`,
    ).toEqual([]);
  });

  it('tags both Opening Balances tabs, which are taggable without being transactional', () => {
    const html = source(OPENING_BALANCE_TEMPLATE);
    expect(declares(html, 'app-reporting-tags-editor', 'OpeningBalance')).toBe(true);
    expect(declares(html, 'app-reporting-tags-editor', 'OpeningStock')).toBe(true);
  });
});
