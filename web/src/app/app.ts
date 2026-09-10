import { HttpClient } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import { NavigationEnd, Router, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs/operators';

import { environment } from '../environments/environment';
import { DatePreferenceService } from './shared/formatting/date-preference';
import { HistoryService } from './shared/navigation/history.service';
import { DateRangePicker } from './shared/platform/date-range-picker';
import { DateRangeService } from './shared/platform/date-range.service';
import { GlobalSearch } from './shared/platform/global-search';
import { HistoryMenu } from './shared/platform/history-menu';
import { LeftNav } from './shared/platform/left-nav';

/**
 * The application shell.
 *
 * Phase 0 made it a health-check probe and a `<router-outlet>`; **phase 33 gave it the platform
 * chrome** — the global search box and the History popover the reference product carries in its top
 * bar on every screen.
 *
 * <b>Why here rather than on the dashboards.</b> The chrome is global in the reference product, and
 * putting it on two pages would have made a "global" search reachable from two of a hundred and
 * thirty routes. The shell is also the only place that sees *every* navigation, which is what
 * History has to observe. What this deliberately does **not** do is grow into the full left-nav
 * shell: that is a re-layout of every existing page, and NFR-6.1 (one interaction model across every
 * screen) is phase 34's whole subject.
 *
 * The bar renders only inside an organization, because everything on it is organization-scoped: the
 * search endpoint takes a tenant, and so does the preference store. On the login, registration and
 * organization-picker screens there is nothing for it to search.
 */
@Component({
  selector: 'app-root',
  imports: [RouterOutlet, GlobalSearch, HistoryMenu, LeftNav, DateRangePicker],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App implements OnInit {
  protected readonly title = signal('ErpApp');
  protected readonly apiStatus = signal<'checking' | 'healthy' | 'unreachable'>('checking');

  /** The organization in scope, parsed from the url — null outside `/organizations/{id}/…`. */
  protected readonly organizationId = signal<string | null>(null);

  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly history = inject(HistoryService);
  private readonly datePreference = inject(DatePreferenceService);
  private readonly dateRange = inject(DateRangeService);

  ngOnInit(): void {
    this.http.get<{ status: string }>(`${environment.apiBaseUrl}/health`).subscribe({
      next: () => this.apiStatus.set('healthy'),
      error: () => this.apiStatus.set('unreachable'),
    });

    this.router.events
      .pipe(filter((e): e is NavigationEnd => e instanceof NavigationEnd))
      .subscribe((e) => this.onNavigated(e.urlAfterRedirects));

    this.history.start();
  }

  private onNavigated(url: string): void {
    const organizationId = organizationIdOf(url);

    if (organizationId === this.organizationId()) {
      return;
    }

    this.organizationId.set(organizationId);

    // Adopt this organization's stored calendar choice. Phase 23 left this service as the seam to
    // move behind an endpoint; phase 33 built the endpoint, and this is the call that uses it.
    if (organizationId) {
      this.datePreference.activate(organizationId);

      // Phase 34b — the same seam for the global date range. Both are per-user, per-organization
      // rows in the store phase 33 built.
      this.dateRange.activate(organizationId);
    }
  }
}

/** `/organizations/{guid}/…` → the guid; anything else → null. */
export function organizationIdOf(url: string): string | null {
  const segments = url.split('?')[0].split('#')[0].split('/').filter((s) => s.length > 0);

  return segments[0] === 'organizations' && segments.length >= 2 ? segments[1] : null;
}
