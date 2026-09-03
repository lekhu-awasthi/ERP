import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { PagedResult } from '../common/paged-result';
import { environment } from '../../../environments/environment';
import {
  AuditRowDto,
  CreateTaskRequest,
  CreateTaskResult,
  RecentTransactionFilter,
  RecentTransactionRowDto,
  SystemAuditAction,
  SystemAuditDocumentType,
  TaskListDto,
  TaskParentType,
  TaskStatus,
  TransactionApprovalDocumentType,
  TransactionApprovalQueueDto,
  TransactionListRowDto,
  TransactionListStatus,
  UpdateTaskRequest,
} from './workflow.models';

@Injectable({ providedIn: 'root' })
export class WorkflowService {
  private readonly http = inject(HttpClient);

  private baseUrl(organizationId: string): string {
    return `${environment.apiBaseUrl}/api/organizations/${organizationId}`;
  }

  getTransactionApprovalQueue(organizationId: string): Observable<TransactionApprovalQueueDto> {
    return this.http.get<TransactionApprovalQueueDto>(`${this.baseUrl(organizationId)}/workflow/transaction-approval-queue`, {
      withCredentials: true,
    });
  }

  /** Phase 23: the Home dashboard's recent-activity feed. Server-paged over the merged stream --
   * the ordering only works because the merge happens server-side. */
  getRecentTransactions(
    organizationId: string,
    fromDate: string,
    toDate: string,
    filter: RecentTransactionFilter,
    page = 1,
    pageSize = 10,
  ): Observable<PagedResult<RecentTransactionRowDto>> {
    const params: Record<string, string> = {
      fromDate,
      toDate,
      filter,
      page: String(page),
      pageSize: String(pageSize),
    };
    return this.http.get<PagedResult<RecentTransactionRowDto>>(
      `${this.baseUrl(organizationId)}/workflow/recent-transactions`,
      { withCredentials: true, params },
    );
  }

  listTasks(
    organizationId: string,
    parentType: TaskParentType,
    parentId: string,
    status: TaskStatus | null,
    page = 1,
    pageSize = 50,
  ): Observable<TaskListDto> {
    const params: Record<string, string> = { parentType, parentId, page: String(page), pageSize: String(pageSize) };
    if (status) {
      params['status'] = status;
    }
    return this.http.get<TaskListDto>(`${this.baseUrl(organizationId)}/tasks`, { withCredentials: true, params });
  }

  createTask(organizationId: string, request: CreateTaskRequest): Observable<CreateTaskResult> {
    return this.http.post<CreateTaskResult>(`${this.baseUrl(organizationId)}/tasks`, request, {
      withCredentials: true,
    });
  }

  updateTask(organizationId: string, id: string, request: UpdateTaskRequest): Observable<void> {
    return this.http.put<void>(`${this.baseUrl(organizationId)}/tasks/${id}`, request, { withCredentials: true });
  }

  updateTaskStatus(organizationId: string, id: string, newStatus: TaskStatus): Observable<void> {
    return this.http.put<void>(
      `${this.baseUrl(organizationId)}/tasks/${id}/status`,
      { newStatus },
      { withCredentials: true },
    );
  }

  getSystemAuditReport(
    organizationId: string,
    userId: string | null,
    action: SystemAuditAction | null,
    documentType: SystemAuditDocumentType | null,
    fromDate: string | null,
    toDate: string | null,
    page = 1,
    pageSize = 50,
  ): Observable<PagedResult<AuditRowDto>> {
    return this.http.get<PagedResult<AuditRowDto>>(`${this.baseUrl(organizationId)}/reports/system-audit`, {
      withCredentials: true,
      params: this.systemAuditParams(userId, action, documentType, fromDate, toDate, page, pageSize),
    });
  }

  exportSystemAuditReport(
    organizationId: string,
    userId: string | null,
    action: SystemAuditAction | null,
    documentType: SystemAuditDocumentType | null,
    fromDate: string | null,
    toDate: string | null,
    full: boolean,
    page: number,
    pageSize: number,
  ): Observable<Blob> {
    return this.http.get(`${this.baseUrl(organizationId)}/reports/system-audit/export`, {
      withCredentials: true,
      params: { ...this.systemAuditParams(userId, action, documentType, fromDate, toDate, page, pageSize), full: String(full) },
      responseType: 'blob',
    });
  }

  /**
   * Phase 26a -- the Transaction list. documentType and status are repeated query params, so this
   * builds an HttpParams rather than the Record<string, string> the other report methods use:
   * a plain object cannot express a repeated key, and typing the params as a union that includes
   * `{}` is what silently selects HttpClient's arraybuffer overload (phase-3 bug #4).
   */
  getTransactionList(
    organizationId: string,
    documentTypes: TransactionApprovalDocumentType[],
    statuses: TransactionListStatus[],
    fromDate: string | null,
    toDate: string | null,
    page = 1,
    pageSize = 50,
  ): Observable<PagedResult<TransactionListRowDto>> {
    return this.http.get<PagedResult<TransactionListRowDto>>(
      `${this.baseUrl(organizationId)}/reports/transaction-list`,
      {
        withCredentials: true,
        params: this.transactionListParams(documentTypes, statuses, fromDate, toDate, page, pageSize),
      },
    );
  }

  exportTransactionList(
    organizationId: string,
    documentTypes: TransactionApprovalDocumentType[],
    statuses: TransactionListStatus[],
    fromDate: string | null,
    toDate: string | null,
    full: boolean,
    page: number,
    pageSize: number,
  ): Observable<Blob> {
    return this.http.get(`${this.baseUrl(organizationId)}/reports/transaction-list/export`, {
      withCredentials: true,
      params: this.transactionListParams(documentTypes, statuses, fromDate, toDate, page, pageSize)
        .set('full', String(full)),
      responseType: 'blob',
    });
  }

  private transactionListParams(
    documentTypes: TransactionApprovalDocumentType[],
    statuses: TransactionListStatus[],
    fromDate: string | null,
    toDate: string | null,
    page: number,
    pageSize: number,
  ): HttpParams {
    let params = new HttpParams().set('page', String(page)).set('pageSize', String(pageSize));
    for (const documentType of documentTypes) {
      params = params.append('documentType', documentType);
    }
    for (const status of statuses) {
      params = params.append('status', status);
    }
    if (fromDate) {
      params = params.set('fromDate', fromDate);
    }
    if (toDate) {
      params = params.set('toDate', toDate);
    }
    return params;
  }

  private systemAuditParams(
    userId: string | null,
    action: SystemAuditAction | null,
    documentType: SystemAuditDocumentType | null,
    fromDate: string | null,
    toDate: string | null,
    page: number,
    pageSize: number,
  ): Record<string, string> {
    const params: Record<string, string> = { page: String(page), pageSize: String(pageSize) };
    if (userId) {
      params['userId'] = userId;
    }
    if (action) {
      params['action'] = action;
    }
    if (documentType) {
      params['documentType'] = documentType;
    }
    if (fromDate) {
      params['fromDate'] = fromDate;
    }
    if (toDate) {
      params['toDate'] = toDate;
    }
    return params;
  }
}
