/**
 * Roadmap Phase 21b -- full-tenant data export (FR-2.8 / NFR-4.3).
 *
 * <p>Note the vocabulary: this is an <b>export</b>, never a "backup". FR-2.8 uses the word
 * "backup/export", but there is no restore path anywhere in this product and none planned, so a
 * button labelled Backup would promise something it cannot keep. The artifact is a human-readable
 * multi-sheet .xlsx, and the workbook says so on its own first sheet. See
 * docs/phase-21b-status.md, Decision A.</p>
 *
 * <p>Phase 38 added three categories, a per-category selection and a date range. All three are
 * optional: asking for nothing is still "export my data", every category, every date.</p>
 */

/**
 * A sheet of the workbook. The first five are FR-2.8's own list; the last three are Phase 38's, by
 * the rule that a category earns its place when its rows cannot be reconstructed from the others --
 * the General Ledger carries what a document did to the accounts, never its quantities or rates.
 */
export type ExportCategory =
  | 'Products'
  | 'Contacts'
  | 'ChartOfAccounts'
  | 'LedgerTransactions'
  | 'StockMovements'
  | 'SalesDocuments'
  | 'PurchaseDocuments'
  | 'Payments';

/** Label and ordering for the picker, in the workbook's own sheet order. `dated` says whether a
 * date range narrows it, which the screen shows so a full product list inside a one-month export
 * does not read as a bug. */
export const EXPORT_CATEGORIES: readonly { value: ExportCategory; label: string; dated: boolean }[] = [
  { value: 'Products', label: 'Products', dated: false },
  { value: 'Contacts', label: 'Contacts', dated: false },
  { value: 'ChartOfAccounts', label: 'Chart of Accounts', dated: false },
  { value: 'LedgerTransactions', label: 'Ledger Transactions', dated: true },
  { value: 'StockMovements', label: 'Stock Movements', dated: true },
  { value: 'SalesDocuments', label: 'Sales Documents', dated: true },
  { value: 'PurchaseDocuments', label: 'Purchase Documents', dated: true },
  { value: 'Payments', label: 'Payments', dated: true },
];

/** `Completed` means a file exists (possibly with truncated sheets -- see `truncationNotice`);
 * `Failed` means no file was produced at all. */
export type ExportJobStatus = 'Queued' | 'Running' | 'Completed' | 'Failed' | 'Cancelled';

export interface ExportJobSummary {
  id: string;
  status: ExportJobStatus;
  failureReason: string | null;
  fileName: string | null;
  fileSizeBytes: number | null;
  totalCategoryCount: number;
  processedCategoryCount: number;
  totalRowCount: number;
  /** Set when a category hit the per-sheet row cap. The file is still complete and downloadable. */
  truncationNotice: string | null;
  cancellationRequested: boolean;
  /** The only thing the Download button should key off: false while a job is still running, false
   * once retention has deleted the file. The storage key itself never crosses the wire. */
  hasArtifact: boolean;
  initiatedByUserId: string;
  initiatedByName: string;
  createdAt: string;
  startedAt: string | null;
  completedAt: string | null;
  /** When retention will delete the file (Decision E). */
  expiresAt: string | null;
  artifactPurgedAt: string | null;

  /** Phase 38 -- what this export was asked for, stored on the job rather than re-derived, so a
   * week-old artifact can still say what it contains. */
  categories: ExportCategory[];
  fromDate: string | null;
  toDate: string | null;
}
