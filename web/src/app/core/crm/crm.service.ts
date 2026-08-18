import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import {
  CreateDealRequest,
  CreateDealResult,
  DealListDto,
  DealStatus,
  MoveDealToStageRequest,
  UpdateDealRequest,
} from './crm.models';

@Injectable({ providedIn: 'root' })
export class CrmService {
  private readonly http = inject(HttpClient);

  private baseUrl(organizationId: string): string {
    return `${environment.apiBaseUrl}/api/organizations/${organizationId}`;
  }

  listDeals(organizationId: string, contactId: string | null, status: DealStatus | null): Observable<DealListDto> {
    const params: Record<string, string> = {};
    if (contactId) {
      params['contactId'] = contactId;
    }
    if (status) {
      params['status'] = status;
    }
    return this.http.get<DealListDto>(`${this.baseUrl(organizationId)}/deals`, { withCredentials: true, params });
  }

  createDeal(organizationId: string, request: CreateDealRequest): Observable<CreateDealResult> {
    return this.http.post<CreateDealResult>(`${this.baseUrl(organizationId)}/deals`, request, { withCredentials: true });
  }

  updateDeal(organizationId: string, id: string, request: UpdateDealRequest): Observable<void> {
    return this.http.put<void>(`${this.baseUrl(organizationId)}/deals/${id}`, request, { withCredentials: true });
  }

  moveDealToStage(organizationId: string, id: string, request: MoveDealToStageRequest): Observable<void> {
    return this.http.put<void>(`${this.baseUrl(organizationId)}/deals/${id}/stage`, request, { withCredentials: true });
  }

  markDealWon(organizationId: string, id: string): Observable<void> {
    return this.http.post<void>(`${this.baseUrl(organizationId)}/deals/${id}/mark-won`, {}, { withCredentials: true });
  }

  markDealLost(organizationId: string, id: string): Observable<void> {
    return this.http.post<void>(`${this.baseUrl(organizationId)}/deals/${id}/mark-lost`, {}, { withCredentials: true });
  }
}
