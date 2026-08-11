export type ContactType = 'Customer' | 'Supplier' | 'Lead';

export interface ContactGroup {
  id: string;
  organizationId: string;
  name: string;
  parentGroupId: string | null;
  isActive: boolean;
  createdAt: string;
}

export interface CreateContactGroupRequest {
  name: string;
  parentGroupId: string | null;
}

export interface CreateContactGroupResult {
  id: string;
  name: string;
  parentGroupId: string | null;
}

export interface UpdateContactGroupRequest {
  name: string;
  parentGroupId: string | null;
  isActive: boolean;
}

export interface UpdateContactGroupResult {
  id: string;
  name: string;
  parentGroupId: string | null;
  isActive: boolean;
}

export interface Contact {
  id: string;
  organizationId: string;
  type: ContactType;
  name: string;
  code: string;
  address: string | null;
  pan: string | null;
  phone: string | null;
  email: string | null;
  groupId: string | null;
  isActive: boolean;
  openingBalance: number;
  createdAt: string;
}

export interface CreateContactRequest {
  type: ContactType;
  name: string;
  address: string | null;
  pan: string | null;
  phone: string | null;
  email: string | null;
  groupId: string | null;
  openingBalance: number;
}

export interface CreateContactResult {
  id: string;
  code: string;
  type: ContactType;
  name: string;
}

export interface UpdateContactRequest {
  name: string;
  address: string | null;
  pan: string | null;
  phone: string | null;
  email: string | null;
  groupId: string | null;
  openingBalance: number;
}

export interface UpdateContactResult {
  id: string;
  name: string;
}
