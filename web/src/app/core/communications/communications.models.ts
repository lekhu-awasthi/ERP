// Phase 30 (Communications -- Send Email, email templates, email logs; FR-11.1 / FR-4.5).

import { PagedResult } from '../common/paged-result';
import { DocumentType } from '../sales/sales.models';

/**
 * What an email template is written for. Live-confirmed off the reference product's own Template
 * Type picker (docs/phase-30-status.md, Step 1.1) -- note this is NOT the four-member
 * CustomTemplateType: the two vocabularies are disjoint and served by different resources.
 */
export type EmailTemplateContext =
  | 'General'
  | 'Quotation'
  | 'SalesOrder'
  | 'Invoice'
  | 'CreditNote'
  | 'CustomerPayment'
  | 'SupplierPayment'
  | 'PurchaseOrder'
  | 'BalanceConfirmation';

export type EmailSendStatus = 'Queued' | 'Sending' | 'Sent' | 'Failed';

export interface EmailTemplateOption {
  id: string;
  name: string;
  isDefault: boolean;
}

export interface EmailMergeField {
  group: string;
  label: string;
  token: string;
}

/**
 * The draft the dialog opens on. Subject and body arrive with merge fields already substituted --
 * see PrepareEmailQuery for why that resolution happens server-side and once.
 */
export interface PreparedEmail {
  context: EmailTemplateContext;
  contextName: string;
  templates: EmailTemplateOption[];
  defaultTemplateId: string | null;
  subject: string;
  body: string;
  replyTo: string | null;
  cc: string[];
  bcc: string[];
  /** What the live "More..." picker offers: the contact's own address, then its personnel's. */
  suggestedTo: string[];
  canAttachDocumentPdf: boolean;
  documentCode: string | null;
  /** Tokens still standing, so the composer sees a typo before a customer does. */
  unresolvedTokens: string[];
}

export interface SendEmailResult {
  emailSendLogId: string;
  /** True when this request id had already been accepted -- the do-exactly-once path. */
  alreadyQueued: boolean;
}

export interface EmailLogRow {
  id: string;
  createdAt: string;
  completedAt: string | null;
  recipients: string;
  cc: string | null;
  bcc: string | null;
  subject: string;
  status: EmailSendStatus;
  failureReason: string | null;
  sentByUserName: string;
  attachedDocumentPdf: boolean;
  attachmentNames: string[];
}

export type EmailLogListDto = PagedResult<EmailLogRow>;

export interface EmailTemplateDto {
  id: string;
  name: string;
  context: EmailTemplateContext;
  contextName: string;
  subject: string;
  body: string;
  replyTo: string | null;
  cc: string | null;
  bcc: string | null;
  isDefault: boolean;
  isActive: boolean;
}

export interface EmailTemplateContextOption {
  context: EmailTemplateContext;
  name: string;
}

export interface EmailTemplateListDto {
  templates: EmailTemplateDto[];
  contexts: EmailTemplateContextOption[];
  mergeFields: EmailMergeField[];
}

export interface CreateEmailTemplateRequest {
  name: string;
  context: EmailTemplateContext;
  subject: string;
  body: string;
  replyTo: string | null;
  cc: string | null;
  bcc: string | null;
}

export interface UpdateEmailTemplateRequest {
  name: string;
  subject: string;
  body: string;
  replyTo: string | null;
  cc: string | null;
  bcc: string | null;
  isActive: boolean;
}

/**
 * What the dialog submits. `requestId` is minted once when the dialog opens and is the idempotency
 * key: re-submitting it (a double-click, a retry) yields one email, while reopening the dialog
 * mints a fresh one and that resend is a new row. See EmailSendLog.
 */
export interface SendEmailRequest {
  requestId: string;
  documentType: DocumentType | null;
  parentId: string;
  templateId: string | null;
  to: string[];
  cc: string[];
  bcc: string[];
  replyTo: string | null;
  subject: string;
  body: string;
  attachDocumentPdf: boolean;
  files: File[];
}
