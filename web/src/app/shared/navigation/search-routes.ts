import { GlobalSearchHitDto, SearchDocumentType } from '../../core/platform/platform.models';

/**
 * Phase 33 — where a search hit lives.
 *
 * <b>The server deliberately does not answer this.</b> `GlobalSearchQuery` returns what a row *is*
 * (collection, document type, id) and never a url, because the route table is client knowledge and a
 * server-side copy of it would be a second source of truth that drifts. The reference product splits
 * the same way for its own record hits — only its navigation rows carry a url.
 */

/** The detail route of each transactional type, relative to `/organizations/{id}`. */
const DOCUMENT_ROUTES: Readonly<Record<SearchDocumentType, readonly string[]>> = {
  Quotation: ['sales', 'quotations'],
  SalesOrder: ['sales', 'sales-orders'],
  Invoice: ['sales', 'invoices'],
  CreditNote: ['sales', 'credit-notes'],
  // Payment is the one type whose route needs the hit's own subKind -- see paymentRoute below.
  Payment: ['payments'],
  PurchaseOrder: ['purchasing', 'purchase-orders'],
  PurchaseBill: ['purchasing', 'purchase-bills'],
  Expense: ['purchasing', 'expenses'],
  DebitNote: ['purchasing', 'debit-notes'],
  JournalVoucher: ['accounting', 'journal-vouchers'],
  CashTransfer: ['accounting', 'cash-transfers'],
  WarehouseTransfer: ['inventory', 'warehouse-transfers'],
  InventoryAdjustment: ['inventory', 'inventory-adjustments'],
  ProductionOrder: ['manufacturing', 'production-orders'],
  ProductionJournal: ['manufacturing', 'production-journals'],
};

/**
 * The router link for one hit, or null when the app has no screen to open it on.
 *
 * <b>Phase 35a closed the one null.</b> Phase 33 left an Account hit unlinked — "no detail page" —
 * because the Chart of Accounts has a list and nothing per account. The answer was not to build
 * that page: the reference product does not have one either, and what its result row offers is a
 * **View Ledger** action (module scan, global-search section: "Contacts and Accounts additionally
 * carry a View Ledger quick-action button inside the result row"). So an Account resolves to the
 * Detail General Ledger with the account already chosen, which is the same destination the Chart of
 * Accounts row action now uses. See docs/phase-35a-status.md Decision A.
 *
 * A Contact keeps its own detail page as the primary target: it has one, and its Overview tab has
 * carried a "View Full Statement" link to that contact's ledger since phase 10. The live row's
 * second button is deliberately not reproduced here — the results list is an ARIA `listbox` and an
 * interactive control inside a `role="option"` breaks the combobox pattern phase 33 built.
 */
export function hitRouterLink(organizationId: string, hit: GlobalSearchHitDto): unknown[] | null {
  const prefix = ['/organizations', organizationId];

  switch (hit.collection) {
    case 'Contact':
      return [...prefix, 'contacts', hit.id];

    case 'Product':
      return [...prefix, 'products', hit.id];

    case 'Account':
      return [...prefix, 'reports', 'detail-general-ledger'];

    case 'Document': {
      if (!hit.documentType) {
        return null;
      }

      const segments =
        hit.documentType === 'Payment' ? paymentRoute(hit.subKind) : DOCUMENT_ROUTES[hit.documentType];

      return segments ? [...prefix, ...segments, hit.id] : null;
    }

    default:
      return null;
  }
}

/**
 * One Payment aggregate, two detail screens. The direction rides in `subKind`, which is that field's
 * own purpose; a hit that somehow arrives without one falls back to the customer-payment screen,
 * which is the direction the bare `/payments` route serves.
 */
function paymentRoute(direction: string | null): readonly string[] {
  return direction === 'Paid' ? ['purchasing', 'supplier-payments'] : ['payments'];
}

/**
 * Phase 35a -- the query parameters a hit's route needs, or undefined. Only an Account has any: its
 * destination is a report, and the account it is a report *of* rides in the url the way
 * `customer-statement`'s subject has since phase 10.
 */
export function hitQueryParams(hit: GlobalSearchHitDto): Record<string, string> | undefined {
  return hit.collection === 'Account' ? { accountId: hit.id } : undefined;
}

/** The second line of a result row: the sub-kind for master data, the document type for a document. */
export function hitDescription(hit: GlobalSearchHitDto): string {
  if (hit.collection === 'Document') {
    return hit.documentType ? spaceOutPascalCase(hit.documentType) : 'Document';
  }

  return hit.subKind ? `${hit.subKind}` : hit.collection;
}

/** The first line: a name where there is one, the document number where there is not. */
export function hitLabel(hit: GlobalSearchHitDto): string {
  return hit.name ? `${hit.name} (${hit.code})` : hit.code;
}

function spaceOutPascalCase(value: string): string {
  return value.replace(/([a-z])([A-Z])/g, '$1 $2');
}
