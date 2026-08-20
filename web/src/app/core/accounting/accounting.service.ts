import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';

import { environment } from '../../../environments/environment';
import { MAX_PAGE_SIZE, PagedResult } from '../common/paged-result';
import {
  Account,
  AccountGroup,
  AccountOpeningBalanceDto,
  AccountRootType,
  ApproveCashTransferResult,
  ApproveJournalVoucherResult,
  VoidCashTransferResult,
  VoidJournalVoucherResult,
  BalanceSheetDto,
  BankAccountDto,
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
  IncomeStatementDto,
  VatSummaryReportDto,
  JournalVoucher,
  JournalVoucherDetail,
  JournalVoucherLineInput,
  JournalVoucherRequest,
  JournalVoucherStatus,
  OpeningBalanceLineRequest,
  OpeningBalanceLineResult,
  PostedGlLineDto,
  TrialBalanceDto,
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

  /** Bounded master-data / picker lists (Phase 16c) -- no visible pager, just request everything
   * in one page and unwrap, keeping every caller's Observable<T[]> contract intact. */
  private listAll<T>(url: string, extraParams: Record<string, string> = {}): Observable<T[]> {
    return this.http
      .get<PagedResult<T>>(url, {
        withCredentials: true,
        params: { ...extraParams, page: '1', pageSize: String(MAX_PAGE_SIZE) },
      })
      .pipe(map((result) => result.items));
  }

  listAccountGroups(organizationId: string): Observable<AccountGroup[]> {
    return this.listAll<AccountGroup>(`${this.baseUrl(organizationId)}/account-groups`);
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

  listAccounts(organizationId: string, rootType?: AccountRootType, page = 1, pageSize = 50): Observable<PagedResult<Account>> {
    const params: Record<string, string> = { page: String(page), pageSize: String(pageSize) };
    if (rootType) params['rootType'] = rootType;
    return this.http.get<PagedResult<Account>>(`${this.baseUrl(organizationId)}/accounts`, { withCredentials: true, params });
  }

  /** Picker use (e.g. a GL Account dropdown) -- everything in one page, no pager. */
  listAllAccounts(organizationId: string, rootType?: AccountRootType): Observable<Account[]> {
    return this.listAccounts(organizationId, rootType, 1, MAX_PAGE_SIZE).pipe(map((result) => result.items));
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

  listJournalVouchers(
    organizationId: string, status?: JournalVoucherStatus, page = 1, pageSize = 50,
  ): Observable<PagedResult<JournalVoucher>> {
    const params: Record<string, string> = { page: String(page), pageSize: String(pageSize) };
    if (status) params['status'] = status;
    return this.http.get<PagedResult<JournalVoucher>>(`${this.baseUrl(organizationId)}/journal-vouchers`, {
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

  voidJournalVoucher(organizationId: string, id: string): Observable<VoidJournalVoucherResult> {
    return this.http.post<VoidJournalVoucherResult>(
      `${this.baseUrl(organizationId)}/journal-vouchers/${id}/void`,
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

  listCashTransfers(
    organizationId: string, status?: CashTransferStatus, page = 1, pageSize = 50,
  ): Observable<PagedResult<CashTransfer>> {
    const params: Record<string, string> = { page: String(page), pageSize: String(pageSize) };
    if (status) params['status'] = status;
    return this.http.get<PagedResult<CashTransfer>>(`${this.baseUrl(organizationId)}/cash-transfers`, {
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

  voidCashTransfer(organizationId: string, id: string): Observable<VoidCashTransferResult> {
    return this.http.post<VoidCashTransferResult>(
      `${this.baseUrl(organizationId)}/cash-transfers/${id}/void`,
      null,
      { withCredentials: true },
    );
  }

  listBankAccounts(organizationId: string, isActive = true, page = 1, pageSize = 50): Observable<PagedResult<BankAccountDto>> {
    return this.http.get<PagedResult<BankAccountDto>>(`${this.baseUrl(organizationId)}/bank-accounts`, {
      withCredentials: true,
      params: { isActive: String(isActive), page: String(page), pageSize: String(pageSize) },
    });
  }

  listAccountOpeningBalances(
    organizationId: string,
    page = 1,
    pageSize = 50,
  ): Observable<PagedResult<AccountOpeningBalanceDto>> {
    return this.http.get<PagedResult<AccountOpeningBalanceDto>>(`${this.baseUrl(organizationId)}/opening-balances/accounts`, {
      withCredentials: true,
      params: { page: String(page), pageSize: String(pageSize) },
    });
  }

  saveAccountOpeningBalance(
    organizationId: string,
    accountId: string,
    request: OpeningBalanceLineRequest,
  ): Observable<OpeningBalanceLineResult> {
    return this.http.put<OpeningBalanceLineResult>(
      `${this.baseUrl(organizationId)}/opening-balances/accounts/${accountId}`,
      request,
      { withCredentials: true },
    );
  }

  getTrialBalance(organizationId: string, asOfDate: string): Observable<TrialBalanceDto> {
    return this.http.get<TrialBalanceDto>(`${this.baseUrl(organizationId)}/reports/trial-balance`, {
      withCredentials: true,
      params: { asOfDate },
    });
  }

  getBalanceSheet(organizationId: string, asOfDate: string): Observable<BalanceSheetDto> {
    return this.http.get<BalanceSheetDto>(`${this.baseUrl(organizationId)}/reports/balance-sheet`, {
      withCredentials: true,
      params: { asOfDate },
    });
  }

  getIncomeStatement(organizationId: string, fromDate: string, toDate: string): Observable<IncomeStatementDto> {
    return this.http.get<IncomeStatementDto>(`${this.baseUrl(organizationId)}/reports/income-statement`, {
      withCredentials: true,
      params: { fromDate, toDate },
    });
  }

  getVatSummaryReport(organizationId: string, fromDate: string, toDate: string): Observable<VatSummaryReportDto> {
    return this.http.get<VatSummaryReportDto>(`${this.baseUrl(organizationId)}/reports/vat-summary`, {
      withCredentials: true,
      params: { fromDate, toDate },
    });
  }

  /** No "current view" vs "full dataset" distinction -- VAT Summary is always the complete fixed
   * 2x3-bucket result (see the endpoint's own comment), so there's only one export variant. */
  exportVatSummaryReport(organizationId: string, fromDate: string, toDate: string): Observable<Blob> {
    return this.http.get(`${this.baseUrl(organizationId)}/reports/vat-summary/export`, {
      withCredentials: true,
      params: { fromDate, toDate },
      responseType: 'blob',
    });
  }
}
