export interface CreditTerm {
  id: string;
  organizationId: string;
  name: string;
  dueDays: number;
  isActive: boolean;
  createdAt: string;
}

export interface CreateCreditTermRequest {
  name: string;
  dueDays: number;
}

export interface UpdateCreditTermRequest {
  name: string;
  dueDays: number;
  isActive: boolean;
}

export interface PaymentMode {
  id: string;
  organizationId: string;
  name: string;
  isActive: boolean;
  createdAt: string;
}

export interface CreatePaymentModeRequest {
  name: string;
}

export interface UpdatePaymentModeRequest {
  name: string;
  isActive: boolean;
}
