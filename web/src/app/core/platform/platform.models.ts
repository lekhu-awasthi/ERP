/** Phase 33 — the platform chrome's wire types. */

/**
 * Which stored collection a search hit belongs to. Mirrors the server enum; `Document` collapses all
 * fifteen transactional types, with `documentType` naming the one.
 */
export type GlobalSearchCollection = 'Contact' | 'Product' | 'Account' | 'Document';

export type SearchDocumentType =
  | 'Quotation'
  | 'SalesOrder'
  | 'Invoice'
  | 'CreditNote'
  | 'Payment'
  | 'PurchaseOrder'
  | 'PurchaseBill'
  | 'Expense'
  | 'DebitNote'
  | 'JournalVoucher'
  | 'CashTransfer'
  | 'WarehouseTransfer'
  | 'InventoryAdjustment'
  | 'ProductionOrder'
  | 'ProductionJournal';

/**
 * One stored row the search matched. Carries what the thing is, never where it lives — the route
 * table is client knowledge, so `search-routes.ts` turns this into a link.
 *
 * `name` is null for a document: a document has no name, only a number, which is why it is matched
 * on its code alone. Confirmed live against the reference product.
 */
export interface GlobalSearchHitDto {
  readonly collection: GlobalSearchCollection;
  readonly documentType: SearchDocumentType | null;
  readonly id: string;
  readonly code: string;
  readonly name: string | null;
  readonly subKind: string | null;
}

export type QuickLinkKind = 'List' | 'Add';

/**
 * One tile in the Quick Links tray, and simultaneously one entry in the navigation catalogue the
 * search's own navigation half renders from — the reference product's two features share one
 * vocabulary of navigation targets, and so do these.
 *
 * `url` is always an application-relative path (`/sales/invoices`), never absolute. The server
 * validator enforces that, because a stored url a client later navigates to would otherwise be an
 * open-redirect surface.
 */
export interface QuickLinkDto {
  readonly name: string;
  readonly area: string;
  readonly kind: QuickLinkKind;
  readonly url: string;
}

export interface UserPreferenceDto {
  readonly key: string;
  readonly value: string;
  readonly updatedAt: string;
}

/** The closed vocabulary, mirroring the server's `UserPreferenceKeys`. */
export const UserPreferenceKeys = {
  quickLinks: 'quick-links',
  calendar: 'calendar',
} as const;
