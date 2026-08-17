import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import { TransactionApprovalQueueDto } from './workflow.models';

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
}
