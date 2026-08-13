export type MembershipRole = 'Admin' | 'Member';

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
  role: MembershipRole;
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
  role: MembershipRole;
  invitedAt: string;
}

export interface MyOrganizations {
  organizations: OrganizationSummary[];
  requests: PendingRequest[];
  invitations: PendingInvitation[];
}

export interface InviteUserRequest {
  email: string;
  role: MembershipRole;
}

export interface InviteUserResponse {
  membershipId: string;
  email: string;
  role: MembershipRole;
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
}
