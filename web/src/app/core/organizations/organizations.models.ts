// Phase 14 (Role Reference) replaced the hardcoded Admin/Member MembershipRole selector
// everywhere a role is assigned (invite, membership reassignment) with a real RoleId picked from
// ListRolesQuery -- the two role-display fields below (OrganizationSummary.role,
// PendingInvitation.role) already carried the Role entity's own Name as a plain string (see
// MyOrganizationsQuery's DTO doc comment), so they need no change here; only the two
// *assignment*-side shapes (InviteUserRequest, UpdateMembershipRoleRequest) move from an enum
// union to a RoleId.

export interface CreateOrganizationRequest {
  name: string;
  industry: string;
  address: string | null;
  accountingStartDate: string; // yyyy-MM-dd
  isVatRegistered: boolean;
  workspaceName: string;
  email: string | null;
  phone: string | null;
  panNumber: string | null;
  website: string | null;
  trackInventory: boolean;
  multipleLocations: boolean;
  multipleWarehouses: boolean;
  multiCurrency: boolean;
  manufacturing: boolean;
  posRetail: boolean;
  posRestaurant: boolean;
  /** Phase 27b -- Cloudflare Turnstile token from the wizard's final step (FR-1.1). */
  turnstileToken: string;
}

export interface CreateOrganizationResponse {
  organizationId: string;
  name: string;
  workspaceName: string;
}

export interface WorkspaceNameAvailability {
  isAvailable: boolean;
}

export interface OrganizationSummary {
  organizationId: string;
  name: string;
  workspaceName: string;
  industry: string;
  role: string;
}

export interface PendingRequest {
  membershipId: string;
  organizationId: string;
  organizationName: string;
  requestedAt: string;
}

export interface PendingInvitation {
  membershipId: string;
  organizationId: string;
  organizationName: string;
  role: string;
  invitedAt: string;
}

export interface MyOrganizations {
  organizations: OrganizationSummary[];
  requests: PendingRequest[];
  invitations: PendingInvitation[];
}

export interface InviteUserRequest {
  email: string;
  roleId: string;
}

export interface InviteUserResponse {
  membershipId: string;
  email: string;
  roleId: string;
  roleName: string;
}

export interface Warehouse {
  id: string;
  organizationId: string;
  name: string;
  isActive: boolean;
  createdAt: string;
}

export interface CreateWarehouseRequest {
  name: string;
}

export interface CreateWarehouseResult {
  id: string;
  name: string;
}

export interface UpdateWarehouseRequest {
  name: string;
  isActive: boolean;
}

export interface UpdateWarehouseResult {
  id: string;
  name: string;
  isActive: boolean;
}

// Phase 28 (FR-2.5) -- the tenant's active currency list, rendered on the Organization's own
// Features tab in the reference product (confirmed live 2026-09-04), which is why it lives in the
// organizations models beside Warehouse rather than under configuration.
export interface Currency {
  id: string;
  organizationId: string;
  code: string;
  name: string;
  symbol: string;
  isActive: boolean;
  createdAt: string;
}

export interface CurrencyCatalogEntry {
  code: string;
  name: string;
  symbol: string;
  alreadyActivated: boolean;
}

export interface CreateCurrencyRequest {
  code: string;
  name?: string | null;
  symbol?: string | null;
}

export interface CreateCurrencyResult {
  id: string;
  code: string;
  name: string;
  symbol: string;
}

export interface UpdateCurrencyRequest {
  name: string;
  symbol: string;
  isActive: boolean;
}

export interface UpdateCurrencyResult {
  id: string;
  code: string;
  name: string;
  symbol: string;
  isActive: boolean;
}

/** The base currency every tenant is seeded with and can never remove -- mirrors
 * Domain.Common.CurrencyCatalog.BaseCode. */
export const BASE_CURRENCY_CODE = 'NPR';

// Phase 13 -- powers the Task feature's Assigned-To picker. membershipId/roleId (Phase 14) also
// let the Role Reference page's Members section reassign a member's Role.
export interface OrganizationMember {
  membershipId: string;
  userId: string;
  fullName: string;
  email: string;
  roleId: string;
  roleName: string;
}

export interface AccountingDefaults {
  defaultSalesAccountId: string | null;
  defaultAccountsReceivableId: string | null;
  defaultVatPayableAccountId: string | null;
  defaultPurchaseAccountId: string | null;
  defaultAccountsPayableId: string | null;
  defaultVatReceivableAccountId: string | null;
  defaultTdsPayableAccountId: string | null;
  defaultInventoryAccountId: string | null;
  defaultCogsAccountId: string | null;
  defaultInventoryAdjustmentAccountId: string | null;
  /** Phase 25's account, and phase 28's two, were added to the API but never to this interface or
   * its screen -- so three server-side requirements had no way to be configured. Found and closed
   * in phase 29, which needs a fourth (see docs/phase-29-status.md). */
  defaultProductionCostAccountId: string | null;
  defaultForexGainAccountId: string | null;
  defaultForexLossAccountId: string | null;
  /** Phase 29 (FR-6.15) -- credited when a Purchase Bill capitalises an Additional Cost. */
  defaultLandedCostClearingAccountId: string | null;
}

// Phase 16a (lock-date enforcement) -- lockDate is an ISO date string (yyyy-MM-dd) or null (unset).
export interface OrganizationLockDate {
  organizationId: string;
  lockDate: string | null;
}

// Phase 14 (Role Reference).
export interface Role {
  id: string;
  name: string;
  description: string | null;
  isSystemRole: boolean;
}

export interface CreateRoleRequest {
  name: string;
  description: string | null;
}

export interface CreateRoleResult {
  id: string;
  name: string;
  description: string | null;
}

export interface UpdateRoleRequest {
  name: string;
  description: string | null;
}

export interface UpdateRoleResult {
  id: string;
  name: string;
  description: string | null;
}

export interface PermissionMatrixEntry {
  permissionKey: string;
  isGranted: boolean;
}

export interface PermissionMatrixGroup {
  module: string;
  permissions: PermissionMatrixEntry[];
}

export interface RolePermissionMatrix {
  roleId: string;
  roleName: string;
  isSystemRole: boolean;
  groups: PermissionMatrixGroup[];
}

export interface UpdateRolePermissionsRequest {
  grants: Record<string, boolean>;
}

export interface UpdateMembershipRoleRequest {
  roleId: string;
}

// Phase 20f (tenant feature-flag enforcement, FR-2.6). Read-only: the Accounting Features are
// chosen once in the New Organization wizard's Step 2 and are immutable afterwards, matching the
// reference product (whose own subscription screen is read-only and directs you to vendor support
// to change one). There is deliberately no update request shape here.
export type TenantFeatureKey =
  | 'TrackInventory'
  | 'MultipleLocations'
  | 'MultipleWarehouses'
  | 'MultiCurrency'
  | 'Manufacturing'
  | 'PosRetail'
  | 'PosRestaurant';

export interface TenantFeatureState {
  feature: TenantFeatureKey;
  displayName: string;
  description: string;
  isEnabled: boolean;
}

export interface TenantSubscription {
  organizationId: string;
  planName: string;
  trialStartsAt: string;
  trialEndsAt: string;
  isTrialActive: boolean;
  daysRemaining: number;
  irdSyncEnabled: boolean;
  features: TenantFeatureState[];
}
