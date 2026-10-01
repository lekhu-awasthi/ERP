import { PosMode } from '../organizations/organizations.models';

// Phase 60 -- the ERP side of point-of-sale configuration (docs/phase-60-status.md). Mirrors
// Application.Pos's DTOs; enums arrive as strings (JsonStringEnumConverter).

/** Which tab a till opens on. Mirrors Domain.Pos.PosTab. */
export type PosTab = 'Retail' | 'DineIn' | 'TakeAway' | 'Delivery';

export const POS_TAB_LABELS: Readonly<Record<PosTab, string>> = {
  Retail: 'Retail',
  DineIn: 'Dine In',
  TakeAway: 'Take Away',
  Delivery: 'Delivery',
};

export const POS_MODE_LABELS: Readonly<Record<PosMode, string>> = {
  None: 'No till',
  Retail: 'Retail',
  Restaurant: 'Bar / Restaurant',
};

export interface PosWalkInCustomer {
  id: string;
  code: string;
  name: string;
}

export interface PosLocationSummary {
  id: string;
  code: string;
  name: string;
  isActive: boolean;
  isHeadOffice: boolean;
  posMode: PosMode;
  hasSettings: boolean;
  linkedPaymentModeCount: number;
}

export interface PosConfiguration {
  posRetailEnabled: boolean;
  posRestaurantEnabled: boolean;
  walkInCustomer: PosWalkInCustomer | null;
  locations: PosLocationSummary[];
}

export interface PosLocationSettings {
  locationId: string;
  locationCode: string;
  locationName: string;
  locationIsActive: boolean;
  posMode: PosMode;
  /** False while the location reads as the defaults and has never been saved. */
  isSaved: boolean;
  serviceChargeEnabled: boolean;
  serviceChargeRate: number;
  serviceChargeAccountId: string | null;
  roundOffEnabled: boolean;
  roundOffAccountId: string | null;
  cashVerificationRequired: boolean;
  denominations: number[];
  defaultTab: PosTab | null;
  effectiveDefaultTab: PosTab | null;
  availableTabs: PosTab[];
  printEstimateBill: boolean;
  printInvoice: boolean;
  printCreditNote: boolean;
  printKot: boolean;
  paymentModeIds: string[];
}

export interface UpdatePosLocationSettingsRequest {
  serviceChargeEnabled: boolean;
  serviceChargeRate: number;
  serviceChargeAccountId: string | null;
  roundOffEnabled: boolean;
  roundOffAccountId: string | null;
  cashVerificationRequired: boolean;
  denominations: number[];
  defaultTab: PosTab | null;
  printEstimateBill: boolean;
  printInvoice: boolean;
  printCreditNote: boolean;
  printKot: boolean;
}

/** The vendor's default and "Reset to default" list. Mirrors PosLocationSettings.DefaultDenominations. */
export const DEFAULT_DENOMINATIONS: readonly number[] = [1000, 500, 100, 50, 20, 10, 5, 2, 1];
