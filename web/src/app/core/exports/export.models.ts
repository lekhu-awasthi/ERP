/**
 * Roadmap Phase 21b -- full-tenant data export (FR-2.8 / NFR-4.3).
 *
 * <p>Note the vocabulary: this is an <b>export</b>, never a "backup". FR-2.8 uses the word
 * "backup/export", but there is no restore path anywhere in this product and none planned, so a
 * button labelled Backup would promise something it cannot keep. The artifact is a human-readable
 * multi-sheet .xlsx of the five categories FR-2.8 names, and the workbook says so on its own first
 * sheet. See docs/phase-21b-status.md, Decision A.</p>
 */

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
}
