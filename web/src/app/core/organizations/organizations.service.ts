import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';

import { environment } from '../../../environments/environment';
import { MAX_PAGE_SIZE, PagedResult } from '../common/paged-result';
import {
  AccountingDefaults,
  CreateOrganizationRequest,
  CreateOrganizationResponse,
  CreateRoleRequest,
  CreateRoleResult,
  CreateWarehouseRequest,
  CreateWarehouseResult,
  InviteUserRequest,
  InviteUserResponse,
  MyOrganizations,
  OrganizationLockDate,
  OrganizationMember,
  Role,
  RolePermissionMatrix,
  TenantSubscription,
  UpdateMembershipRoleRequest,
  UpdateRolePermissionsRequest,
  UpdateRoleRequest,
  UpdateRoleResult,
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

  /** Bounded master-data / picker lists (Phase 16c) -- no visible pager, just request everything
   * in one page and unwrap, keeping every caller's Observable<T[]> contract intact. */
  private listAll<T>(url: string): Observable<T[]> {
    return this.http
      .get<PagedResult<T>>(url, { withCredentials: true, params: { page: '1', pageSize: String(MAX_PAGE_SIZE) } })
      .pipe(map((result) => result.items));
  }

  listWarehouses(organizationId: string): Observable<Warehouse[]> {
    return this.listAll<Warehouse>(`${this.baseUrl}/${organizationId}/warehouses`);
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
    return this.listAll<OrganizationMember>(`${this.baseUrl}/${organizationId}/members`);
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

  // Phase 20f (FR-2.6). No setter counterpart -- the entitlements are immutable after creation.
  getSubscription(organizationId: string): Observable<TenantSubscription> {
    return this.http.get<TenantSubscription>(`${this.baseUrl}/${organizationId}/subscription`, {
      withCredentials: true,
    });
  }

  getLockDate(organizationId: string): Observable<OrganizationLockDate> {
    return this.http.get<OrganizationLockDate>(`${this.baseUrl}/${organizationId}/lock-date`, { withCredentials: true });
  }

  setLockDate(organizationId: string, lockDate: string | null): Observable<OrganizationLockDate> {
    return this.http.put<OrganizationLockDate>(`${this.baseUrl}/${organizationId}/lock-date`, { lockDate }, {
      withCredentials: true,
    });
  }

  updateMembershipRole(organizationId: string, membershipId: string, request: UpdateMembershipRoleRequest): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/${organizationId}/memberships/${membershipId}/role`, request, {
      withCredentials: true,
    });
  }

  listRoles(organizationId: string, page = 1, pageSize = MAX_PAGE_SIZE): Observable<PagedResult<Role>> {
    return this.http.get<PagedResult<Role>>(`${this.baseUrl}/${organizationId}/roles`, {
      withCredentials: true,
      params: { page: String(page), pageSize: String(pageSize) },
    });
  }

  /** Picker use (e.g. the invite-user role dropdown) -- everything in one page, no pager. */
  listAllRoles(organizationId: string): Observable<Role[]> {
    return this.listRoles(organizationId, 1, MAX_PAGE_SIZE).pipe(map((result) => result.items));
  }

  createRole(organizationId: string, request: CreateRoleRequest): Observable<CreateRoleResult> {
    return this.http.post<CreateRoleResult>(`${this.baseUrl}/${organizationId}/roles`, request, {
      withCredentials: true,
    });
  }

  updateRole(organizationId: string, id: string, request: UpdateRoleRequest): Observable<UpdateRoleResult> {
    return this.http.put<UpdateRoleResult>(`${this.baseUrl}/${organizationId}/roles/${id}`, request, {
      withCredentials: true,
    });
  }

  deleteRole(organizationId: string, id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${organizationId}/roles/${id}`, { withCredentials: true });
  }

  getRolePermissionMatrix(organizationId: string, id: string): Observable<RolePermissionMatrix> {
    return this.http.get<RolePermissionMatrix>(`${this.baseUrl}/${organizationId}/roles/${id}/permissions`, {
      withCredentials: true,
    });
  }

  updateRolePermissions(organizationId: string, id: string, request: UpdateRolePermissionsRequest): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/${organizationId}/roles/${id}/permissions`, request, {
      withCredentials: true,
    });
  }
}
