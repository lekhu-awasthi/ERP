import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import {
  AccountingDefaults,
  CreateOrganizationRequest,
  CreateOrganizationResponse,
  CreateWarehouseRequest,
  CreateWarehouseResult,
  InviteUserRequest,
  InviteUserResponse,
  MyOrganizations,
  OrganizationMember,
  UpdateWarehouseRequest,
  UpdateWarehouseResult,
  Warehouse,
  WorkspaceNameAvailability,
} from './organizations.models';

@Injectable({ providedIn: 'root' })
export class OrganizationsService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/api/organizations`;

  checkWorkspaceNameAvailability(workspaceName: string): Observable<WorkspaceNameAvailability> {
    const params = new HttpParams().set('workspaceName', workspaceName);
    return this.http.get<WorkspaceNameAvailability>(`${this.baseUrl}/workspace-name-availability`, {
      params,
      withCredentials: true,
    });
  }

  myOrganizations(): Observable<MyOrganizations> {
    return this.http.get<MyOrganizations>(`${this.baseUrl}/mine`, { withCredentials: true });
  }

  createOrganization(request: CreateOrganizationRequest): Observable<CreateOrganizationResponse> {
    return this.http.post<CreateOrganizationResponse>(this.baseUrl, request, { withCredentials: true });
  }

  inviteUser(organizationId: string, request: InviteUserRequest): Observable<InviteUserResponse> {
    return this.http.post<InviteUserResponse>(`${this.baseUrl}/${organizationId}/invitations`, request, {
      withCredentials: true,
    });
  }

  acceptInvitation(membershipId: string): Observable<void> {
    return this.http.post<void>(
      `${this.baseUrl}/memberships/${membershipId}/accept-invitation`,
      {},
      { withCredentials: true },
    );
  }

  acceptRequest(membershipId: string): Observable<void> {
    return this.http.post<void>(
      `${this.baseUrl}/memberships/${membershipId}/accept-request`,
      {},
      { withCredentials: true },
    );
  }

  listWarehouses(organizationId: string): Observable<Warehouse[]> {
    return this.http.get<Warehouse[]>(`${this.baseUrl}/${organizationId}/warehouses`, { withCredentials: true });
  }

  createWarehouse(organizationId: string, request: CreateWarehouseRequest): Observable<CreateWarehouseResult> {
    return this.http.post<CreateWarehouseResult>(`${this.baseUrl}/${organizationId}/warehouses`, request, {
      withCredentials: true,
    });
  }

  updateWarehouse(organizationId: string, id: string, request: UpdateWarehouseRequest): Observable<UpdateWarehouseResult> {
    return this.http.put<UpdateWarehouseResult>(`${this.baseUrl}/${organizationId}/warehouses/${id}`, request, {
      withCredentials: true,
    });
  }

  deleteWarehouse(organizationId: string, id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${organizationId}/warehouses/${id}`, { withCredentials: true });
  }

  listMembers(organizationId: string): Observable<OrganizationMember[]> {
    return this.http.get<OrganizationMember[]>(`${this.baseUrl}/${organizationId}/members`, { withCredentials: true });
  }

  getAccountingDefaults(organizationId: string): Observable<AccountingDefaults> {
    return this.http.get<AccountingDefaults>(`${this.baseUrl}/${organizationId}/accounting-defaults`, {
      withCredentials: true,
    });
  }

  updateAccountingDefaults(organizationId: string, request: AccountingDefaults): Observable<AccountingDefaults> {
    return this.http.put<AccountingDefaults>(`${this.baseUrl}/${organizationId}/accounting-defaults`, request, {
      withCredentials: true,
    });
  }
}
