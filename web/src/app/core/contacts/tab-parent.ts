import { DocumentType } from '../sales/sales.models';

/**
 * Phase 27a -- what a Tasks / Documents / Activity tab hangs off.
 *
 * Phase 18 built those tabs for a Contact and threaded a bare `contactId` through the service and
 * both components. Phase 27a puts the same three tabs on all 15 transactional detail pages
 * (live-confirmed: Overview / Tasks / Documents / Activity, identical on Invoice, Journal Voucher
 * and Warehouse Transfer), so the parent is now one of two shapes rather than always a Contact.
 *
 * Modelling it as a discriminated union rather than adding a second optional id keeps the illegal
 * state -- neither id, or both -- unrepresentable, and gives `tabParentPath` one place to decide the
 * URL prefix instead of every call site doing it.
 */
export type TabParent =
  | { readonly kind: 'Contact'; readonly contactId: string }
  | { readonly kind: 'Document'; readonly documentType: DocumentType; readonly documentId: string }
  // Phase 43 (39 carried item #1) — the two record parents. A third and fourth member rather than
  // a widened `documentType`: neither is a document, and the server routes them through their own
  // endpoint family for exactly that reason (see RecordTabsEndpoints).
  | { readonly kind: 'Deal'; readonly dealId: string }
  | { readonly kind: 'Task'; readonly taskId: string };

/** The route prefix these tabs' endpoints live under, matching ContactsEndpoints and
 * DocumentTabsEndpoints respectively. */
export function tabParentPath(parent: TabParent): string {
  switch (parent.kind) {
    case 'Contact':
      return `contacts/${parent.contactId}`;
    case 'Deal':
      return `deals/${parent.dealId}`;
    case 'Task':
      return `tasks/${parent.taskId}`;
    default:
      return `documents/${parent.documentType}/${parent.documentId}`;
  }
}

/** The parent's own id, for the components that need it independently of the URL. */
export function tabParentId(parent: TabParent): string {
  switch (parent.kind) {
    case 'Contact':
      return parent.contactId;
    case 'Deal':
      return parent.dealId;
    case 'Task':
      return parent.taskId;
    default:
      return parent.documentId;
  }
}

/**
 * Whether this parent has an SMS History sub-tab on its Activity tab. Contacts do (a contact has a
 * phone number and Phase 18 built per-contact SMS history); documents do not -- live-confirmed, the
 * document Activity tab shows exactly three sub-tabs, Comments / Activities / Emails, where the
 * Contact tab shows four. Deals and Tasks are the document shape: the live Task detail page reads
 * Comments / Activities / Emails (2026-09-13), and neither record has a phone number of its own.
 */
export function hasSmsHistory(parent: TabParent): boolean {
  return parent.kind === 'Contact';
}
