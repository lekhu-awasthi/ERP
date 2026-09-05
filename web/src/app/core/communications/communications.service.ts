import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import { DocumentType } from '../sales/sales.models';
import {
  CreateEmailTemplateRequest,
  EmailLogListDto,
  EmailTemplateContext,
  EmailTemplateDto,
  EmailTemplateListDto,
  PreparedEmail,
  SendEmailRequest,
  SendEmailResult,
  UpdateEmailTemplateRequest,
} from './communications.models';

/** Phase 30 -- the Send Email dialog, the email log, and Email templates. */
@Injectable({ providedIn: 'root' })
export class CommunicationsService {
  private readonly http = inject(HttpClient);

  private baseUrl(organizationId: string): string {
    return `${environment.apiBaseUrl}/api/organizations/${organizationId}`;
  }

  /**
   * `documentType` null means the send is about a Contact rather than a document -- the Contact
   * detail page's own Send Email action.
   */
  prepareEmail(
    organizationId: string,
    documentType: DocumentType | null,
    parentId: string,
  ): Observable<PreparedEmail> {
    // Annotated Record<string, string>: a union including {} silently resolves the get() overload
    // to arraybuffer (phase-3 bug #4).
    const params: Record<string, string> = { parentId };
    if (documentType) {
      params['documentType'] = documentType;
    }

    return this.http.get<PreparedEmail>(`${this.baseUrl(organizationId)}/emails/prepare`, {
      params,
      withCredentials: true,
    });
  }

  /**
   * multipart/form-data, because the dialog's drop zone carries arbitrary files alongside the
   * message. Address lists are sent as repeated fields, which the endpoint accepts alongside a
   * single comma-separated one.
   */
  sendEmail(organizationId: string, request: SendEmailRequest): Observable<SendEmailResult> {
    const form = new FormData();
    form.append('requestId', request.requestId);
    form.append('parentId', request.parentId);
    form.append('subject', request.subject);
    form.append('body', request.body);
    form.append('attachDocumentPdf', String(request.attachDocumentPdf));

    if (request.documentType) {
      form.append('documentType', request.documentType);
    }
    if (request.templateId) {
      form.append('templateId', request.templateId);
    }
    if (request.replyTo) {
      form.append('replyTo', request.replyTo);
    }

    request.to.forEach((address) => form.append('to', address));
    request.cc.forEach((address) => form.append('cc', address));
    request.bcc.forEach((address) => form.append('bcc', address));
    request.files.forEach((file) => form.append('files', file, file.name));

    return this.http.post<SendEmailResult>(`${this.baseUrl(organizationId)}/emails`, form, {
      withCredentials: true,
    });
  }

  listEmailLogs(
    organizationId: string,
    documentType: DocumentType | null,
    parentId: string,
    page = 1,
    pageSize = 50,
  ): Observable<EmailLogListDto> {
    const params: Record<string, string> = {
      parentId,
      page: String(page),
      pageSize: String(pageSize),
    };
    if (documentType) {
      params['documentType'] = documentType;
    }

    return this.http.get<EmailLogListDto>(`${this.baseUrl(organizationId)}/emails`, {
      params,
      withCredentials: true,
    });
  }

  listEmailTemplates(
    organizationId: string,
    context: EmailTemplateContext | null = null,
    includeInactive = false,
  ): Observable<EmailTemplateListDto> {
    const params: Record<string, string> = { includeInactive: String(includeInactive) };
    if (context) {
      params['context'] = context;
    }

    return this.http.get<EmailTemplateListDto>(`${this.baseUrl(organizationId)}/email-templates`, {
      params,
      withCredentials: true,
    });
  }

  createEmailTemplate(
    organizationId: string,
    request: CreateEmailTemplateRequest,
  ): Observable<EmailTemplateDto> {
    return this.http.post<EmailTemplateDto>(
      `${this.baseUrl(organizationId)}/email-templates`,
      request,
      { withCredentials: true },
    );
  }

  updateEmailTemplate(
    organizationId: string,
    id: string,
    request: UpdateEmailTemplateRequest,
  ): Observable<EmailTemplateDto> {
    return this.http.put<EmailTemplateDto>(
      `${this.baseUrl(organizationId)}/email-templates/${id}`,
      request,
      { withCredentials: true },
    );
  }

  setDefaultEmailTemplate(organizationId: string, id: string): Observable<void> {
    return this.http.post<void>(
      `${this.baseUrl(organizationId)}/email-templates/${id}/set-default`,
      {},
      { withCredentials: true },
    );
  }
}
