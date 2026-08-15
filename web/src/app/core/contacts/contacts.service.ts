import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import {
  Contact,
  ContactAgeingSummaryDto,
  ContactGroup,
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

  listContactGroups(organizationId: string): Observable<ContactGroup[]> {
    return this.http.get<ContactGroup[]>(`${this.baseUrl(organizationId)}/contact-groups`, { withCredentials: true });
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

  listContacts(organizationId: string, type?: ContactType): Observable<Contact[]> {
    const params: Record<string, string> = type ? { type } : {};
    return this.http.get<Contact[]>(`${this.baseUrl(organizationId)}/contacts`, { withCredentials: true, params });
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

  getCustomerAgeingSummary(
    organizationId: string,
    asOfDate: string,
    contactGroupId: string | null,
  ): Observable<ContactAgeingSummaryDto> {
    return this.getAgeingSummary(organizationId, 'customer-ageing-summary', asOfDate, contactGroupId);
  }

  getSupplierAgeingSummary(
    organizationId: string,
    asOfDate: string,
    contactGroupId: string | null,
  ): Observable<ContactAgeingSummaryDto> {
    return this.getAgeingSummary(organizationId, 'supplier-ageing-summary', asOfDate, contactGroupId);
  }

  private getAgeingSummary(
    organizationId: string,
    route: 'customer-ageing-summary' | 'supplier-ageing-summary',
    asOfDate: string,
    contactGroupId: string | null,
  ): Observable<ContactAgeingSummaryDto> {
    const params: Record<string, string> = contactGroupId ? { asOfDate, contactGroupId } : { asOfDate };
    return this.http.get<ContactAgeingSummaryDto>(`${this.baseUrl(organizationId)}/reports/${route}`, {
      withCredentials: true,
      params,
    });
  }

  getCustomerStatement(
    organizationId: string,
    contactId: string,
    fromDate: string,
    toDate: string,
  ): Observable<ContactStatementDto> {
    return this.getStatement(organizationId, 'customer-statement', contactId, fromDate, toDate);
  }

  getSupplierStatement(
    organizationId: string,
    contactId: string,
    fromDate: string,
    toDate: string,
  ): Observable<ContactStatementDto> {
    return this.getStatement(organizationId, 'supplier-statement', contactId, fromDate, toDate);
  }

  private getStatement(
    organizationId: string,
    route: 'customer-statement' | 'supplier-statement',
    contactId: string,
    fromDate: string,
    toDate: string,
  ): Observable<ContactStatementDto> {
    return this.http.get<ContactStatementDto>(`${this.baseUrl(organizationId)}/reports/${route}`, {
      withCredentials: true,
      params: { contactId, fromDate, toDate },
    });
  }
}
