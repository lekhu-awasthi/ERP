import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';

import { environment } from '../../../environments/environment';
import { MAX_PAGE_SIZE, PagedResult } from '../common/paged-result';
import { SmsLogListDto } from '../crm/crm.models';
import {
  ActivityListDto,
  AttachmentListDto,
  AttachmentResult,
  CommentListDto,
  CommentRowDto,
  Contact,
  ContactAgeingSummaryDto,
  ContactGroup,
  ContactOverviewDto,
  ContactPersonnelListDto,
  ContactPersonnelRequest,
  ContactPersonnelResult,
  ContactStatementDto,
  ContactType,
  CreateContactGroupRequest,
  CreateContactGroupResult,
  CreateContactRequest,
  CreateContactResult,
  UpdateContactGroupRequest,
  UpdateContactGroupResult,
  UpdateContactRequest,
  UpdateContactResult,
} from './contacts.models';

@Injectable({ providedIn: 'root' })
export class ContactsService {
  private readonly http = inject(HttpClient);

  private baseUrl(organizationId: string): string {
    return `${environment.apiBaseUrl}/api/organizations/${organizationId}`;
  }

  /** Bounded master-data / picker lists (Phase 16c) -- no visible pager, just request everything
   * in one page and unwrap, keeping every caller's Observable<T[]> contract intact. */
  private listAll<T>(url: string, extraParams: Record<string, string> = {}): Observable<T[]> {
    return this.http
      .get<PagedResult<T>>(url, {
        withCredentials: true,
        params: { ...extraParams, page: '1', pageSize: String(MAX_PAGE_SIZE) },
      })
      .pipe(map((result) => result.items));
  }

  listContactGroups(organizationId: string): Observable<ContactGroup[]> {
    return this.listAll<ContactGroup>(`${this.baseUrl(organizationId)}/contact-groups`);
  }

  createContactGroup(organizationId: string, request: CreateContactGroupRequest): Observable<CreateContactGroupResult> {
    return this.http.post<CreateContactGroupResult>(`${this.baseUrl(organizationId)}/contact-groups`, request, {
      withCredentials: true,
    });
  }

  updateContactGroup(
    organizationId: string,
    id: string,
    request: UpdateContactGroupRequest,
  ): Observable<UpdateContactGroupResult> {
    return this.http.put<UpdateContactGroupResult>(`${this.baseUrl(organizationId)}/contact-groups/${id}`, request, {
      withCredentials: true,
    });
  }

  deleteContactGroup(organizationId: string, id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl(organizationId)}/contact-groups/${id}`, { withCredentials: true });
  }

  listContacts(organizationId: string, type?: ContactType, page = 1, pageSize = 50): Observable<PagedResult<Contact>> {
    const params: Record<string, string> = { page: String(page), pageSize: String(pageSize) };
    if (type) params['type'] = type;
    return this.http.get<PagedResult<Contact>>(`${this.baseUrl(organizationId)}/contacts`, { withCredentials: true, params });
  }

  /** Picker use (e.g. a document's Customer/Supplier dropdown) -- everything in one page, no pager. */
  listAllContacts(organizationId: string, type?: ContactType): Observable<Contact[]> {
    return this.listContacts(organizationId, type, 1, MAX_PAGE_SIZE).pipe(map((result) => result.items));
  }

  getContact(organizationId: string, id: string): Observable<Contact> {
    return this.http.get<Contact>(`${this.baseUrl(organizationId)}/contacts/${id}`, { withCredentials: true });
  }

  createContact(organizationId: string, request: CreateContactRequest): Observable<CreateContactResult> {
    return this.http.post<CreateContactResult>(`${this.baseUrl(organizationId)}/contacts`, request, {
      withCredentials: true,
    });
  }

  updateContact(organizationId: string, id: string, request: UpdateContactRequest): Observable<UpdateContactResult> {
    return this.http.put<UpdateContactResult>(`${this.baseUrl(organizationId)}/contacts/${id}`, request, {
      withCredentials: true,
    });
  }

  deactivateContact(organizationId: string, id: string): Observable<void> {
    return this.http.post<void>(`${this.baseUrl(organizationId)}/contacts/${id}/deactivate`, null, {
      withCredentials: true,
    });
  }

  getContactOverview(organizationId: string, id: string): Observable<ContactOverviewDto> {
    return this.http.get<ContactOverviewDto>(`${this.baseUrl(organizationId)}/contacts/${id}/overview`, {
      withCredentials: true,
    });
  }

  getCustomerAgeingSummary(
    organizationId: string,
    asOfDate: string,
    contactGroupId: string | null,
    page = 1,
    pageSize = 50,
  ): Observable<ContactAgeingSummaryDto> {
    return this.getAgeingSummary(organizationId, 'customer-ageing-summary', asOfDate, contactGroupId, page, pageSize);
  }

  getSupplierAgeingSummary(
    organizationId: string,
    asOfDate: string,
    contactGroupId: string | null,
    page = 1,
    pageSize = 50,
  ): Observable<ContactAgeingSummaryDto> {
    return this.getAgeingSummary(organizationId, 'supplier-ageing-summary', asOfDate, contactGroupId, page, pageSize);
  }

  exportCustomerAgeingSummary(
    organizationId: string, asOfDate: string, contactGroupId: string | null, full: boolean, page: number, pageSize: number,
  ): Observable<Blob> {
    return this.exportAgeingSummary(organizationId, 'customer-ageing-summary', asOfDate, contactGroupId, full, page, pageSize);
  }

  exportSupplierAgeingSummary(
    organizationId: string, asOfDate: string, contactGroupId: string | null, full: boolean, page: number, pageSize: number,
  ): Observable<Blob> {
    return this.exportAgeingSummary(organizationId, 'supplier-ageing-summary', asOfDate, contactGroupId, full, page, pageSize);
  }

  private getAgeingSummary(
    organizationId: string,
    route: 'customer-ageing-summary' | 'supplier-ageing-summary',
    asOfDate: string,
    contactGroupId: string | null,
    page: number,
    pageSize: number,
  ): Observable<ContactAgeingSummaryDto> {
    const params: Record<string, string> = { asOfDate, page: String(page), pageSize: String(pageSize) };
    if (contactGroupId) params['contactGroupId'] = contactGroupId;
    return this.http.get<ContactAgeingSummaryDto>(`${this.baseUrl(organizationId)}/reports/${route}`, {
      withCredentials: true,
      params,
    });
  }

  private exportAgeingSummary(
    organizationId: string,
    route: 'customer-ageing-summary' | 'supplier-ageing-summary',
    asOfDate: string,
    contactGroupId: string | null,
    full: boolean,
    page: number,
    pageSize: number,
  ): Observable<Blob> {
    const params: Record<string, string> = {
      asOfDate, full: String(full), page: String(page), pageSize: String(pageSize),
    };
    if (contactGroupId) params['contactGroupId'] = contactGroupId;
    return this.http.get(`${this.baseUrl(organizationId)}/reports/${route}/export`, {
      withCredentials: true,
      params,
      responseType: 'blob',
    });
  }

  getCustomerStatement(
    organizationId: string,
    contactId: string,
    fromDate: string,
    toDate: string,
    page = 1,
    pageSize = 50,
  ): Observable<ContactStatementDto> {
    return this.getStatement(organizationId, 'customer-statement', contactId, fromDate, toDate, page, pageSize);
  }

  getSupplierStatement(
    organizationId: string,
    contactId: string,
    fromDate: string,
    toDate: string,
    page = 1,
    pageSize = 50,
  ): Observable<ContactStatementDto> {
    return this.getStatement(organizationId, 'supplier-statement', contactId, fromDate, toDate, page, pageSize);
  }

  exportCustomerStatement(
    organizationId: string, contactId: string, fromDate: string, toDate: string, full: boolean, page: number, pageSize: number,
  ): Observable<Blob> {
    return this.exportStatement(organizationId, 'customer-statement', contactId, fromDate, toDate, full, page, pageSize);
  }

  exportSupplierStatement(
    organizationId: string, contactId: string, fromDate: string, toDate: string, full: boolean, page: number, pageSize: number,
  ): Observable<Blob> {
    return this.exportStatement(organizationId, 'supplier-statement', contactId, fromDate, toDate, full, page, pageSize);
  }

  private getStatement(
    organizationId: string,
    route: 'customer-statement' | 'supplier-statement',
    contactId: string,
    fromDate: string,
    toDate: string,
    page: number,
    pageSize: number,
  ): Observable<ContactStatementDto> {
    return this.http.get<ContactStatementDto>(`${this.baseUrl(organizationId)}/reports/${route}`, {
      withCredentials: true,
      params: { contactId, fromDate, toDate, page: String(page), pageSize: String(pageSize) },
    });
  }

  // --- Contact Personnel (Phase 18) ---

  listContactPersonnel(organizationId: string, contactId: string, page = 1, pageSize = 50): Observable<ContactPersonnelListDto> {
    return this.http.get<ContactPersonnelListDto>(`${this.baseUrl(organizationId)}/contacts/${contactId}/contact-personnel`, {
      withCredentials: true,
      params: { page: String(page), pageSize: String(pageSize) },
    });
  }

  createContactPersonnel(
    organizationId: string, contactId: string, request: ContactPersonnelRequest,
  ): Observable<ContactPersonnelResult> {
    return this.http.post<ContactPersonnelResult>(
      `${this.baseUrl(organizationId)}/contacts/${contactId}/contact-personnel`, request, { withCredentials: true },
    );
  }

  updateContactPersonnel(
    organizationId: string, contactId: string, id: string, request: ContactPersonnelRequest,
  ): Observable<ContactPersonnelResult> {
    return this.http.put<ContactPersonnelResult>(
      `${this.baseUrl(organizationId)}/contacts/${contactId}/contact-personnel/${id}`, request, { withCredentials: true },
    );
  }

  deleteContactPersonnel(organizationId: string, contactId: string, id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl(organizationId)}/contacts/${contactId}/contact-personnel/${id}`, {
      withCredentials: true,
    });
  }

  // --- Attachments (Phase 18) -- "Documents" tab ---

  listAttachments(organizationId: string, contactId: string, page = 1, pageSize = 50): Observable<AttachmentListDto> {
    return this.http.get<AttachmentListDto>(`${this.baseUrl(organizationId)}/contacts/${contactId}/attachments`, {
      withCredentials: true,
      params: { page: String(page), pageSize: String(pageSize) },
    });
  }

  uploadAttachment(organizationId: string, contactId: string, file: File): Observable<AttachmentResult> {
    const formData = new FormData();
    formData.append('file', file);
    return this.http.post<AttachmentResult>(`${this.baseUrl(organizationId)}/contacts/${contactId}/attachments`, formData, {
      withCredentials: true,
    });
  }

  downloadAttachment(organizationId: string, id: string): Observable<Blob> {
    return this.http.get(`${this.baseUrl(organizationId)}/attachments/${id}/download`, {
      withCredentials: true,
      responseType: 'blob',
    });
  }

  deleteAttachment(organizationId: string, id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl(organizationId)}/attachments/${id}`, { withCredentials: true });
  }

  // --- Comments, Activities, per-contact SMS history (Phase 18) -- the "Activity" tab's 3 sub-tabs
  // (Email Logs has no backend capability at all, see contact-detail-page's own comment) ---

  listComments(organizationId: string, contactId: string, page = 1, pageSize = 50): Observable<CommentListDto> {
    return this.http.get<CommentListDto>(`${this.baseUrl(organizationId)}/contacts/${contactId}/comments`, {
      withCredentials: true,
      params: { page: String(page), pageSize: String(pageSize) },
    });
  }

  addComment(organizationId: string, contactId: string, content: string): Observable<CommentRowDto> {
    return this.http.post<CommentRowDto>(
      `${this.baseUrl(organizationId)}/contacts/${contactId}/comments`, { content }, { withCredentials: true },
    );
  }

  listActivities(organizationId: string, contactId: string, page = 1, pageSize = 50): Observable<ActivityListDto> {
    return this.http.get<ActivityListDto>(`${this.baseUrl(organizationId)}/contacts/${contactId}/activities`, {
      withCredentials: true,
      params: { page: String(page), pageSize: String(pageSize) },
    });
  }

  listContactSmsHistory(organizationId: string, contactId: string, page = 1, pageSize = 50): Observable<SmsLogListDto> {
    return this.http.get<SmsLogListDto>(`${this.baseUrl(organizationId)}/contacts/${contactId}/sms-history`, {
      withCredentials: true,
      params: { page: String(page), pageSize: String(pageSize) },
    });
  }

  private exportStatement(
    organizationId: string,
    route: 'customer-statement' | 'supplier-statement',
    contactId: string,
    fromDate: string,
    toDate: string,
    full: boolean,
    page: number,
    pageSize: number,
  ): Observable<Blob> {
    return this.http.get(`${this.baseUrl(organizationId)}/reports/${route}/export`, {
      withCredentials: true,
      params: { contactId, fromDate, toDate, full: String(full), page: String(page), pageSize: String(pageSize) },
      responseType: 'blob',
    });
  }
}
