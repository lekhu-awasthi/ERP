import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import {
  Account,
  AccountGroup,
  AccountRootType,
  ApproveCashTransferResult,
  ApproveJournalVoucherResult,
  CashTransfer,
  CashTransferDetail,
  CashTransferRequest,
  CashTransferStatus,
  CreateAccountGroupRequest,
  CreateAccountGroupResult,
  CreateAccountRequest,
  CreateAccountResult,
  CreateCashTransferResult,
  CreateJournalVoucherResult,
  JournalVoucher,
  JournalVoucherDetail,
  JournalVoucherLineInput,
  JournalVoucherRequest,
  JournalVoucherStatus,
  PostedGlLineDto,
  UpdateAccountGroupRequest,
  UpdateAccountGroupResult,
  UpdateAccountRequest,
  UpdateAccountResult,
  UpdateCashTransferResult,
  UpdateJournalVoucherResult,
} from './accounting.models';

@Injectable({ providedIn: 'root' })
export class AccountingService {
  private readonly http = inject(HttpClient);

  private baseUrl(organizationId: string): string {
    return `${environment.apiBaseUrl}/api/organizations/${organizationId}`;
  }

  listAccountGroups(organizationId: string): Observable<AccountGroup[]> {
    return this.http.get<AccountGroup[]>(`${this.baseUrl(organizationId)}/account-groups`, { withCredentials: true });
  }

  createAccountGroup(organizationId: string, request: CreateAccountGroupRequest): Observable<CreateAccountGroupResult> {
    return this.http.post<CreateAccountGroupResult>(`${this.baseUrl(organizationId)}/account-groups`, request, {
      withCredentials: true,
    });
  }

  updateAccountGroup(
    organizationId: string,
    id: string,
    request: UpdateAccountGroupRequest,
  ): Observable<UpdateAccountGroupResult> {
    return this.http.put<UpdateAccountGroupResult>(`${this.baseUrl(organizationId)}/account-groups/${id}`, request, {
      withCredentials: true,
    });
  }

  deleteAccountGroup(organizationId: string, id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl(organizationId)}/account-groups/${id}`, { withCredentials: true });
  }

  listAccounts(organizationId: string, rootType?: AccountRootType): Observable<Account[]> {
    const params: Record<string, string> = rootType ? { rootType } : {};
    return this.http.get<Account[]>(`${this.baseUrl(organizationId)}/accounts`, { withCredentials: true, params });
  }

  getAccount(organizationId: string, id: string): Observable<Account> {
    return this.http.get<Account>(`${this.baseUrl(organizationId)}/accounts/${id}`, { withCredentials: true });
  }

  createAccount(organizationId: string, request: CreateAccountRequest): Observable<CreateAccountResult> {
    return this.http.post<CreateAccountResult>(`${this.baseUrl(organizationId)}/accounts`, request, {
      withCredentials: true,
    });
  }

  updateAccount(organizationId: string, id: string, request: UpdateAccountRequest): Observable<UpdateAccountResult> {
    return this.http.put<UpdateAccountResult>(`${this.baseUrl(organizationId)}/accounts/${id}`, request, {
      withCredentials: true,
    });
  }

  listJournalVouchers(organizationId: string, status?: JournalVoucherStatus): Observable<JournalVoucher[]> {
    const params: Record<string, string> = status ? { status } : {};
    return this.http.get<JournalVoucher[]>(`${this.baseUrl(organizationId)}/journal-vouchers`, {
      withCredentials: true,
      params,
    });
  }

  getJournalVoucher(organizationId: string, id: string): Observable<JournalVoucherDetail> {
    return this.http.get<JournalVoucherDetail>(`${this.baseUrl(organizationId)}/journal-vouchers/${id}`, {
      withCredentials: true,
    });
  }

  createJournalVoucher(organizationId: string, request: JournalVoucherRequest): Observable<CreateJournalVoucherResult> {
    return this.http.post<CreateJournalVoucherResult>(`${this.baseUrl(organizationId)}/journal-vouchers`, request, {
      withCredentials: true,
    });
  }

  updateJournalVoucher(
    organizationId: string,
    id: string,
    request: JournalVoucherRequest,
  ): Observable<UpdateJournalVoucherResult> {
    return this.http.put<UpdateJournalVoucherResult>(`${this.baseUrl(organizationId)}/journal-vouchers/${id}`, request, {
      withCredentials: true,
    });
  }

  approveJournalVoucher(organizationId: string, id: string): Observable<ApproveJournalVoucherResult> {
    return this.http.post<ApproveJournalVoucherResult>(
      `${this.baseUrl(organizationId)}/journal-vouchers/${id}/approve`,
      null,
      { withCredentials: true },
    );
  }

  previewGlPosting(
    organizationId: string,
    date: string,
    reference: string | null,
    lines: JournalVoucherLineInput[],
  ): Observable<PostedGlLineDto[]> {
    return this.http.post<PostedGlLineDto[]>(
      `${this.baseUrl(organizationId)}/journal-vouchers/preview-gl-posting`,
      { date, reference, lines },
      { withCredentials: true },
    );
  }

  listCashTransfers(organizationId: string, status?: CashTransferStatus): Observable<CashTransfer[]> {
    const params: Record<string, string> = status ? { status } : {};
    return this.http.get<CashTransfer[]>(`${this.baseUrl(organizationId)}/cash-transfers`, {
      withCredentials: true,
      params,
    });
  }

  getCashTransfer(organizationId: string, id: string): Observable<CashTransferDetail> {
    return this.http.get<CashTransferDetail>(`${this.baseUrl(organizationId)}/cash-transfers/${id}`, {
      withCredentials: true,
    });
  }

  createCashTransfer(organizationId: string, request: CashTransferRequest): Observable<CreateCashTransferResult> {
    return this.http.post<CreateCashTransferResult>(`${this.baseUrl(organizationId)}/cash-transfers`, request, {
      withCredentials: true,
    });
  }

  updateCashTransfer(
    organizationId: string,
    id: string,
    request: CashTransferRequest,
  ): Observable<UpdateCashTransferResult> {
    return this.http.put<UpdateCashTransferResult>(`${this.baseUrl(organizationId)}/cash-transfers/${id}`, request, {
      withCredentials: true,
    });
  }

  approveCashTransfer(organizationId: string, id: string): Observable<ApproveCashTransferResult> {
    return this.http.post<ApproveCashTransferResult>(
      `${this.baseUrl(organizationId)}/cash-transfers/${id}/approve`,
      null,
      { withCredentials: true },
    );
  }
}
