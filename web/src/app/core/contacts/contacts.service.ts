import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import {
  Contact,
  ContactGroup,
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
}
