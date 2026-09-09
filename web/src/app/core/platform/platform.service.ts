import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import { GlobalSearchHitDto, UserPreferenceDto } from './platform.models';

@Injectable({ providedIn: 'root' })
export class PlatformService {
  private readonly http = inject(HttpClient);

  private baseUrl(organizationId: string): string {
    return `${environment.apiBaseUrl}/api/organizations/${organizationId}`;
  }

  /**
   * The record half of the top bar's search. The navigation half never reaches the server — see
   * `navigation-catalog.ts`.
   *
   * `params` is annotated `Record<string, string>` deliberately: a union including `{}` silently
   * resolves to HttpClient's `arraybuffer` overload (phase-3 bug #4).
   */
  search(organizationId: string, term: string, limit?: number): Observable<GlobalSearchHitDto[]> {
    const params: Record<string, string> = { term };

    if (limit !== undefined) {
      params['limit'] = String(limit);
    }

    return this.http.get<GlobalSearchHitDto[]>(`${this.baseUrl(organizationId)}/search`, {
      params: new HttpParams({ fromObject: params }),
      withCredentials: true,
    });
  }

  /** Every preference this user has set in this organization, in one round trip. */
  getPreferences(organizationId: string): Observable<UserPreferenceDto[]> {
    return this.http.get<UserPreferenceDto[]>(`${this.baseUrl(organizationId)}/preferences`, {
      withCredentials: true,
    });
  }

  /** Replaces one preference wholesale. `value` is already serialized JSON. */
  setPreference(organizationId: string, key: string, value: string): Observable<UserPreferenceDto> {
    return this.http.put<UserPreferenceDto>(
      `${this.baseUrl(organizationId)}/preferences/${encodeURIComponent(key)}`,
      { value },
      { withCredentials: true },
    );
  }
}
