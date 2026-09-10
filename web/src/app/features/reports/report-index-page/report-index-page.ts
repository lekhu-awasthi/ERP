import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { NavigationCatalog } from '../../../shared/navigation/navigation-catalog';
import { buildReportIndex } from '../../../shared/navigation/nav-tree';

/**
 * Phase 34b — the Reports catalogue.
 *
 * **Why this page exists at all.** The left nav shipped in this phase is derived from
 * `NavigationCatalog`, and this codebase has **52** routes under `reports/` — more nav rows than
 * every other area put together. Rendering them as an accordion would have made the nav unusable,
 * so Reports is a leaf and this is what it opens. That is not a workaround: it is what the reference
 * product does, confirmed live on 2026-09-10, where Reports is an `ant-menu-item` (a leaf, unlike
 * the seven `ant-menu-submenu` areas) opening a catalogue page grouped under exactly these headings.
 *
 * Both halves are derived. The rows come from the catalogue, so a report a later phase adds appears
 * here with no edit; only the *heading* it appears under is written down, in `REPORT_CATEGORIES`,
 * because a route carries no signal for it. `nav-tree.spec.ts` fails the build when a `reports/`
 * route has no heading, so the failure mode is a red test rather than a report nobody can find.
 */
@Component({
  selector: 'app-report-index-page',
  imports: [RouterLink],
  templateUrl: './report-index-page.html',
})
export class ReportIndexPage {
  private readonly route = inject(ActivatedRoute);
  private readonly catalog = inject(NavigationCatalog);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  /**
   * The filter box. A plain signal written by the input's own handler rather than a `computed()`
   * over a `FormControl.value` — the app is zoneless, and that `computed` would cache forever
   * (phase-17).
   */
  protected readonly filter = signal('');

  private readonly categories = computed(() => buildReportIndex(this.catalog.entries()));

  protected readonly totalCount = computed(() =>
    this.categories().reduce((sum, group) => sum + group.reports.length, 0),
  );

  protected readonly visible = computed(() => {
    const needle = this.filter().trim().toLowerCase();

    if (needle.length === 0) {
      return this.categories();
    }

    return this.categories()
      .map((group) => ({
        category: group.category,
        reports: group.reports.filter(
          (r) => r.name.toLowerCase().includes(needle) || group.category.toLowerCase().includes(needle),
        ),
      }))
      .filter((group) => group.reports.length > 0);
  });

  protected readonly matchCount = computed(() =>
    this.visible().reduce((sum, group) => sum + group.reports.length, 0),
  );

  protected onFilter(event: Event): void {
    this.filter.set((event.target as HTMLInputElement).value);
  }

  protected linkFor(url: string): unknown[] {
    return ['/organizations', this.organizationId, ...url.split('/').filter((s) => s.length > 0)];
  }
}
