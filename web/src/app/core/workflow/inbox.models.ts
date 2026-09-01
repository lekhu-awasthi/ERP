/**
 * Phase 22 (FR-10.3) -- the Document inbox.
 *
 * Kept in its own file rather than appended to workflow.models.ts: the inbox is the Workflow
 * context's third feature and the only one with a file-storage and extraction surface, so mixing it
 * into the approval-queue/task models would make neither easier to read.
 */

/** The four "+ Add as" targets FR-10.3 names, which are exactly the reference product's own
 * AI-assisted set (erp-module-scan.md line 110's four sparkle entries). 'Payment' is Quick
 * Payment/Quick Receipt -- Phase 17 built that as a thin variant of the Payment aggregate, not its
 * own document type. Mirrors InboxConversionTargets.Supported on the server. */
export type InboxTargetType = 'Invoice' | 'PurchaseBill' | 'Expense' | 'Payment';

export const INBOX_TARGET_TYPES: readonly InboxTargetType[] = ['PurchaseBill', 'Expense', 'Invoice', 'Payment'];

export const INBOX_TARGET_LABELS: Readonly<Record<InboxTargetType, string>> = {
  PurchaseBill: 'Purchase Bill',
  Expense: 'Expense',
  Invoice: 'Invoice',
  Payment: 'Quick Payment',
};

export type UploadedDocumentStatus = 'Pending' | 'Done';

/**
 * `Failed` and `Unavailable` are ordinary, expected outcomes, not errors -- a document whose
 * extraction never ran, timed out or came back garbage is still fully convertible by hand, and the
 * screen says so rather than showing an error state.
 */
export type DocumentExtractionStatus = 'NotAttempted' | 'Succeeded' | 'Failed' | 'Unavailable';

export interface ExtractedDocumentLine {
  description: string | null;
  quantity: number | null;
  rate: number | null;
  amount: number | null;
}

/** A suggestion under review, never data. Every field is nullable on purpose: a null renders as an
 * empty box the user obviously must fill, whereas a wrong-but-confident value renders as a
 * pre-filled box they may not re-read. */
export interface ExtractedDocumentData {
  partyName: string | null;
  partyPan: string | null;
  documentDate: string | null;
  reference: string | null;
  totalAmount: number | null;
  vatAmount: number | null;
  lines: ExtractedDocumentLine[];
}

export interface InboxDocument {
  id: string;
  fileName: string;
  sizeBytes: number;
  contentType: string;
  description: string | null;
  label: string | null;
  status: UploadedDocumentStatus;
  uploadedByUserId: string;
  uploadedByName: string;
  uploadedAt: string;
  /** The single condition the "+ Add as", Delete and Reopen controls gate on -- never `status`,
   * which a user can also set by hand. */
  isLinked: boolean;
  linkedTransactionType: InboxTargetType | null;
  linkedTransactionId: string | null;
  linkedAt: string | null;
  extractionStatus: DocumentExtractionStatus;
  extractionModelId: string | null;
  extractionFailureReason: string | null;
  extractionAttemptedAt: string | null;
  /** Whether an extractor could read this file at all (images and PDFs). A spreadsheet in the inbox
   * is a perfectly good manually-convertible document; it just has nothing to extract from. */
  isExtractable: boolean;
  extractedData: ExtractedDocumentData | null;
}

export interface InboxPrefillLine {
  productId: string | null;
  descriptionRaw: string | null;
  quantity: number | null;
  rate: number | null;
  amount: number | null;
}

/**
 * The server-computed pre-fill a conversion hands to the target form. Target-agnostic on purpose --
 * one shape for all four targets, because they overlap almost entirely at this level.
 *
 * `hasExtraction: false` is the normal case for a manual conversion: the user gets a blank form with
 * the scan beside it, which is the whole base feature.
 */
export interface InboxPrefill {
  documentId: string;
  fileName: string;
  contentType: string;
  targetType: InboxTargetType;
  hasExtraction: boolean;
  extractionModelId: string | null;
  contactId: string | null;
  partyNameRaw: string | null;
  partyPanRaw: string | null;
  date: string | null;
  reference: string | null;
  totalAmount: number | null;
  vatAmount: number | null;
  lines: InboxPrefillLine[];
}

/** `extractorConfigured` is about the deployment, `enabled` about this tenant -- both must be true
 * for extraction to run, so the screen can name whichever one is missing. */
export interface AiDocumentExtractionSetting {
  enabled: boolean;
  extractorConfigured: boolean;
  modelId: string | null;
}

export interface UpdateInboxDocumentRequest {
  description: string | null;
  label: string | null;
  status: UploadedDocumentStatus;
}
