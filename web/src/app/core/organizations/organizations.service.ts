import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';

import { environment } from '../../../environments/environment';
import { MAX_PAGE_SIZE, PagedResult } from '../common/paged-result';
import { ListQueryOptions, applyListOptions } from '../../shared/pagination/list-query-options';
import {
  AccountingDefaults,
  CreateOrganizationRequest,
  OrganizationLogoResult,
  OrganizationProfile,
  CreateOrganizationResponse,
  CreateRoleRequest,
  CreateRoleResult,
  BillingLocation,
  BillingLocationSettings,
  CreateBillingLocationRequest,
  CreateBillingLocationResult,
  UpdateBillingLocationRequest,
  UpdateBillingLocationSettingsRequest,
  CreateCurrencyRequest,
  CreateCurrencyResult,
  Currency,
  CurrencyCatalogEntry,
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
  UpdateCurrencyRequest,
  UpdateCurrencyResult,
  UpdateWarehouseRequest,
  UpdateWarehouseResult,
  Warehouse,
  WorkspaceNameAvailability,
  GeneralSettings,
  SetTenantSubscriptionRequest,
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

  listCurrencies(organizationId: string): Observable<Currency[]> {
    return this.listAll<Currency>(`${this.baseUrl}/${organizationId}/currencies`);
  }

  listCurrencyCatalog(organizationId: string): Observable<CurrencyCatalogEntry[]> {
    return this.http.get<CurrencyCatalogEntry[]>(`${this.baseUrl}/${organizationId}/currencies/catalog`, {
      withCredentials: true,
    });
  }

  createCurrency(organizationId: string, request: CreateCurrencyRequest): Observable<CreateCurrencyResult> {
    return this.http.post<CreateCurrencyResult>(`${this.baseUrl}/${organizationId}/currencies`, request, {
      withCredentials: true,
    });
  }

  updateCurrency(organizationId: string, id: string, request: UpdateCurrencyRequest): Observable<UpdateCurrencyResult> {
    return this.http.put<UpdateCurrencyResult>(`${this.baseUrl}/${organizationId}/currencies/${id}`, request, {
      withCredentials: true,
    });
  }

  deleteCurrency(organizationId: string, id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${organizationId}/currencies/${id}`, { withCredentials: true });
  }

  // Phase 32 (FR-2.3/FR-3.3). No delete: the live list deactivates instead, and a location a
  // document points at must never disappear.
  listBillingLocations(organizationId: string, includeInactive = false): Observable<BillingLocation[]> {
    return this.http.get<BillingLocation[]>(`${this.baseUrl}/${organizationId}/billing-locations`, {
      params: { includeInactive: String(includeInactive) },
      withCredentials: true,
    });
  }

  createBillingLocation(
    organizationId: string,
    request: CreateBillingLocationRequest,
  ): Observable<CreateBillingLocationResult> {
    return this.http.post<CreateBillingLocationResult>(
      `${this.baseUrl}/${organizationId}/billing-locations`,
      request,
      { withCredentials: true },
    );
  }

  updateBillingLocation(
    organizationId: string,
    id: string,
    request: UpdateBillingLocationRequest,
  ): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/${organizationId}/billing-locations/${id}`, request, {
      withCredentials: true,
    });
  }

  getBillingLocationSettings(organizationId: string): Observable<BillingLocationSettings> {
    return this.http.get<BillingLocationSettings>(
      `${this.baseUrl}/${organizationId}/billing-location-settings`,
      { withCredentials: true },
    );
  }

  updateBillingLocationSettings(
    organizationId: string,
    request: UpdateBillingLocationSettingsRequest,
  ): Observable<BillingLocationSettings> {
    return this.http.put<BillingLocationSettings>(
      `${this.baseUrl}/${organizationId}/billing-location-settings`,
      request,
      { withCredentials: true },
    );
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
  // Phase 31 -- Configurations > General.
  getGeneralSettings(organizationId: string): Observable<GeneralSettings> {
    return this.http.get<GeneralSettings>(`${this.baseUrl}/${organizationId}/general-settings`, {
      withCredentials: true,
    });
  }

  updateGeneralSettings(organizationId: string, request: GeneralSettings): Observable<GeneralSettings> {
    return this.http.put<GeneralSettings>(`${this.baseUrl}/${organizationId}/general-settings`, request, {
      withCredentials: true,
    });
  }

  // Phase 31 -- the renewal setter 20f left out, so an expired organization is not permanently
  // read-only from inside the product.
  setSubscription(organizationId: string, request: SetTenantSubscriptionRequest): Observable<TenantSubscription> {
    return this.http.put<TenantSubscription>(`${this.baseUrl}/${organizationId}/subscription`, request, {
      withCredentials: true,
    });
  }

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


  // --- Phase 39: Organization Profile and its logo -------------------------------------------

  getProfile(organizationId: string): Observable<OrganizationProfile> {
    return this.http.get<OrganizationProfile>(`${this.baseUrl}/${organizationId}/profile`, {
      withCredentials: true,
    });
  }

  /** The logo's own url. Authenticated like every other read, so an `<img [src]>` pointed at it
   * needs `withCredentials` on the element -- which is why the profile page fetches a Blob and binds
   * an object url instead of pointing `src` straight here. */
  logoUrl(organizationId: string): string {
    return `${this.baseUrl}/${organizationId}/logo`;
  }

  getLogo(organizationId: string): Observable<Blob> {
    return this.http.get(this.logoUrl(organizationId), { withCredentials: true, responseType: 'blob' });
  }

  uploadLogo(organizationId: string, file: File): Observable<OrganizationLogoResult> {
    const form = new FormData();
    form.append('file', file);

    return this.http.post<OrganizationLogoResult>(this.logoUrl(organizationId), form, {
      withCredentials: true,
    });
  }

  removeLogo(organizationId: string): Observable<OrganizationLogoResult> {
    return this.http.delete<OrganizationLogoResult>(this.logoUrl(organizationId), {
      withCredentials: true,
    });
  }

  updateMembershipRole(organizationId: string, membershipId: string, request: UpdateMembershipRoleRequest): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/${organizationId}/memberships/${membershipId}/role`, request, {
      withCredentials: true,
    });
  }

  listRoles(
    organizationId: string,
    page = 1,
    pageSize = MAX_PAGE_SIZE,
    options?: ListQueryOptions,
  ): Observable<PagedResult<Role>> {
    const params: Record<string, string> = { page: String(page), pageSize: String(pageSize) };
    applyListOptions(params, options);
    return this.http.get<PagedResult<Role>>(`${this.baseUrl}/${organizationId}/roles`, {
      withCredentials: true,
      params,
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
