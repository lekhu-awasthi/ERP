import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import {
  CreateOrganizationRequest,
  CreateOrganizationResponse,
  InviteUserRequest,
  InviteUserResponse,
  MyOrganizations,
  WorkspaceNameAvailability,
} from './organizations.models';

@Injectable({ providedIn: 'root' })
export class OrganizationsService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/api/organizations`;

  checkWorkspaceNameAvailability(workspaceName: string): Observable<WorkspaceNameAvailability> {
    const params = new HttpParams().set('workspaceName', workspaceName);
    return this.http.get<WorkspaceNameAvailability>(`${this.baseUrl}/workspace-name-availability`, {
      params,
      withCredentials: true,
    });
  }

  myOrganizations(): Observable<MyOrganizations> {
    return this.http.get<MyOrganizations>(`${this.baseUrl}/mine`, { withCredentials: true });
  }

  createOrganization(request: CreateOrganizationRequest): Observable<CreateOrganizationResponse> {
    return this.http.post<CreateOrganizationResponse>(this.baseUrl, request, { withCredentials: true });
  }

  inviteUser(organizationId: string, request: InviteUserRequest): Observable<InviteUserResponse> {
    return this.http.post<InviteUserResponse>(`${this.baseUrl}/${organizationId}/invitations`, request, {
      withCredentials: true,
    });
  }

  acceptInvitation(membershipId: string): Observable<void> {
    return this.http.post<void>(
      `${this.baseUrl}/memberships/${membershipId}/accept-invitation`,
      {},
      { withCredentials: true },
    );
  }

  acceptRequest(membershipId: string): Observable<void> {
    return this.http.post<void>(
      `${this.baseUrl}/memberships/${membershipId}/accept-request`,
      {},
      { withCredentials: true },
    );
  }
}
