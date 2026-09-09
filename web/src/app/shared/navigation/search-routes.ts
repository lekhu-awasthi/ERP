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
 * The only null today is an Account: the Chart of Accounts has a list screen but no per-account
 * detail page, so an account hit is rendered without a link rather than pointed at a list of 175
 * rows pretending to be the one you asked for. Recorded as a carried item in phase-33-status.md —
 * the reference product does have somewhere to go here (its result row offers "View Ledger").
 */
export function hitRouterLink(organizationId: string, hit: GlobalSearchHitDto): unknown[] | null {
  const prefix = ['/organizations', organizationId];

  switch (hit.collection) {
    case 'Contact':
      return [...prefix, 'contacts', hit.id];

    case 'Product':
      return [...prefix, 'products', hit.id];

    case 'Account':
      return null;

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
