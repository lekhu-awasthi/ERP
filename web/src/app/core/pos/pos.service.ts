import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import { PosMode } from '../organizations/organizations.models';
import { PosConfiguration, PosLocationSettings, UpdatePosLocationSettingsRequest } from './pos.models';

/** Phase 60 -- Configurations > Point of Sale. The till's own calls (phase 62) will join these. */
@Injectable({ providedIn: 'root' })
export class PosService {
  private readonly http = inject(HttpClient);

  getConfiguration(organizationId: string): Observable<PosConfiguration> {
    return this.http.get<PosConfiguration>(`${this.baseUrl(organizationId)}/configuration`, { withCredentials: true });
  }

  getLocationSettings(organizationId: string, locationId: string): Observable<PosLocationSettings> {
    return this.http.get<PosLocationSettings>(`${this.locationUrl(organizationId, locationId)}/settings`, {
      withCredentials: true,
    });
  }

  updateLocationSettings(
    organizationId: string,
    locationId: string,
    request: UpdatePosLocationSettingsRequest,
  ): Observable<PosLocationSettings> {
    return this.http.put<PosLocationSettings>(`${this.locationUrl(organizationId, locationId)}/settings`, request, {
      withCredentials: true,
    });
  }

  setLocationMode(organizationId: string, locationId: string, posMode: PosMode): Observable<PosLocationSettings> {
    return this.http.put<PosLocationSettings>(`${this.locationUrl(organizationId, locationId)}/mode`, { posMode }, {
      withCredentials: true,
    });
  }

  setLocationPaymentModes(
    organizationId: string,
    locationId: string,
    paymentModeIds: string[],
  ): Observable<PosLocationSettings> {
    return this.http.put<PosLocationSettings>(
      `${this.locationUrl(organizationId, locationId)}/payment-modes`,
      { paymentModeIds },
      { withCredentials: true },
    );
  }

  private baseUrl(organizationId: string): string {
    return `${environment.apiBaseUrl}/api/organizations/${organizationId}/pos`;
  }

  private locationUrl(organizationId: string, locationId: string): string {
    return `${this.baseUrl(organizationId)}/locations/${locationId}`;
  }
}
