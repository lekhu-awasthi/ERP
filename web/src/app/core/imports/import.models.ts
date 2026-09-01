import { PagedResult } from '../common/paged-result';

/**
 * Roadmap Phase 21a -- bulk import (FR-2.9 / NFR-4.3).
 *
 * `ImportEntityType` is narrower than the reference product's seven-option Upload Type dropdown by
 * design: three ship, four are deferred as mechanical follow-up (see the backing enum's own doc
 * comment for why the product's "Contact" option is a different aggregate entirely and would have
 * produced the wrong importer).
 */
export type ImportEntityType = 'Product' | 'Customer' | 'Supplier';

export type ImportMode = 'CreateNew' | 'UpdateExisting';

/**
 * `Completed` is reached whether or not rows were rejected -- partial success is the normal outcome
 * of a bulk import, so `Failed` means only that the file itself could not be processed. See the
 * backing enum.
 */
export type ImportJobStatus = 'Queued' | 'Running' | 'Completed' | 'Failed' | 'Cancelled';

export type ImportJobRowStatus = 'Pending' | 'Succeeded' | 'Failed';

export interface ImportJobSummary {
  id: string;
  entityType: ImportEntityType;
  mode: ImportMode;
  fileName: string;
  status: ImportJobStatus;
  failureReason: string | null;
  totalRowCount: number;
  processedRowCount: number;
  succeededRowCount: number;
  failedRowCount: number;
  cancellationRequested: boolean;
  initiatedByUserId: string;
  initiatedByName: string;
  createdAt: string;
  startedAt: string | null;
  completedAt: string | null;
}

export interface ImportJobRow {
  /** The spreadsheet's own 1-based row number, header included, so it points at what the user sees. */
  rowNumber: number;
  status: ImportJobRowStatus;
  columnName: string | null;
  message: string | null;
  targetId: string | null;
  targetCode: string | null;
}

export interface ImportJobDetail {
  job: ImportJobSummary;
  rows: PagedResult<ImportJobRow>;
}
