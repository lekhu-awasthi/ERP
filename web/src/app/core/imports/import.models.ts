import { PagedResult } from '../common/paged-result';

/**
 * Roadmap Phase 21a -- bulk import (FR-2.9 / NFR-4.3).
 *
 * `ImportEntityType` is narrower than the reference product's seven-option Upload Type dropdown by
 * design: three ship, four are deferred as mechanical follow-up (see the backing enum's own doc
 * comment for why the product's "Contact" option is a different aggregate entirely and would have
 * produced the wrong importer).
 */
export type ImportEntityType =
  | 'Product'
  | 'Customer'
  | 'Supplier'
  // Phase 21c (FR-2.10) -- migrated tax-register rows. Not in the reference product's Upload Type
  // dropdown at all: they live on their own Migration screen there, and here too. They ride the
  // same ImportJob because the job is the same job; only the screens differ.
  | 'MigratedSalesRegister'
  | 'MigratedPurchaseRegister'
  // Phase 38 -- the four the reference product's dropdown has and Phase 21a deferred, plus one
  // addition. 'ContactPersonnel' is that product's "Contact" option, which is a person attached to
  // a customer or supplier rather than a contact; 'ProductVariant' is not in its list at all.
  | 'Account'
  | 'ProductCategory'
  | 'AccountGroup'
  | 'ContactPersonnel'
  | 'ProductVariant';

/** The two upload types the Migration screen owns; the Import / Export screen owns the other three. */
export const MIGRATION_ENTITY_TYPES: readonly ImportEntityType[] = [
  'MigratedSalesRegister',
  'MigratedPurchaseRegister',
];

export const MASTER_DATA_ENTITY_TYPES: readonly ImportEntityType[] = [
  'Product',
  'Customer',
  'Supplier',
  'Account',
  'ProductCategory',
  'AccountGroup',
  'ContactPersonnel',
  'ProductVariant',
];

/**
 * The upload types that offer Create New Records only.
 *
 * Two of them are the reference product's own asymmetry, read live in Phase 21a: it offers both
 * modes for five of its seven types and Create alone for Product Category and Account Group. The
 * server rejects the other combination at upload with a 400, so this list exists to stop the user
 * reaching that -- not to be the rule.
 */
export const CREATE_ONLY_ENTITY_TYPES: readonly ImportEntityType[] = [
  'ProductCategory',
  'AccountGroup',
  'ProductVariant',
];

export type ImportMode = 'CreateNew' | 'UpdateExisting';

/**
 * `Completed` is reached whether or not rows were rejected -- partial success is the normal outcome
 * of a bulk import, so `Failed` means only that the file itself could not be processed. See the
 * backing enum.
 */
export type ImportJobStatus =
  | 'Queued'
  | 'Running'
  | 'Completed'
  | 'Failed'
  | 'Cancelled'
  // Phase 38 -- the dry run and the wait for a person. See ImportJob's own doc comment for why the
  // pass a runner is in is derived from two columns rather than stored a third time.
  | 'Validating'
  | 'PendingConfirmation';

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

  /** Phase 38 -- whether this upload asked for a dry run before anything was written. */
  reviewBeforeApply: boolean;

  /** When Confirm Upload was pressed; null while a reviewed job is still waiting. */
  reviewConfirmedAt: string | null;

  /** "N records validated" -- the rows the dry run did not reject, i.e. what Confirm Upload applies. */
  validatedRowCount: number;
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
