import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import {
  CreateTaskRequest,
  CreateTaskResult,
  TaskListDto,
  TaskParentType,
  TaskStatus,
  TransactionApprovalQueueDto,
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
}
