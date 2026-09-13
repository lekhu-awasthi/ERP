import { Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { GlobalSearchCollection, GlobalSearchHitDto } from '../../../core/platform/platform.models';
import { PlatformService } from '../../../core/platform/platform.service';
import { NavigationCatalog } from '../../../shared/navigation/navigation-catalog';
import {
  hitDescription,
  hitLabel,
  hitQueryParams,
  hitRouterLink,
} from '../../../shared/navigation/search-routes';

/** The filter's options, plus the "everything" choice the page opens on. */
interface KindOption {
  readonly value: GlobalSearchCollection | null;
  readonly label: string;
}

/**
 * Phase 39 — the search results page, closing phase 33's carried item #4.
 *
 * <b>It was conditional, and the condition came true.</b> Phase 33 deferred this with a specific
 * re-entry test: "worth it once a tenant has enough data for the per-collection cap of 5 to bite".
 * Phase 34c ran that test against its 50,000-invoice dataset and answered plainly — an ordinary term
 * returns the cap from every collection, so the dropdown is a sample rather than an answer. This is
 * the screen that gives the whole answer.
 *
 * <b>The kind filter is not chrome.</b> 34c also measured the unfiltered fan-out — eighteen queries —
 * at 595–1,106 ms p95, over its own 500 ms budget in every pass. Narrowing to one collection runs a
 * fraction of that, so the control that makes a long result list readable is the same one that makes
 * it affordable to produce. See `GlobalSearchQuery.Collection`.
 *
 * <b>Neither half is new.</b> The record half comes from the same server query the dropdown uses,
 * with a higher cap; the navigation half comes from the same {@link NavigationCatalog}. A results
 * page that answered differently from the dropdown above it would be worse than none.
 */
@Component({
  selector: 'app-search-results-page',
  imports: [RouterLink],
  templateUrl: './search-results-page.html',
})
export class SearchResultsPage {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly platform = inject(PlatformService);
  private readonly catalog = inject(NavigationCatalog);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  /**
   * The term comes from the url, not from a component field. That is what makes a result page
   * shareable, back-button-correct and reloadable — and it is why this reads `queryParamMap` as a
   * signal rather than a snapshot: arriving here from the dropdown twice with different terms is one
   * navigation to the same component, and a snapshot would show the first term for ever (phase-3
   * bug #1, in its query-parameter form).
   */
  private readonly queryParams = toSignal(this.route.queryParamMap, {
    initialValue: this.route.snapshot.queryParamMap,
  });

  protected readonly term = computed(() => this.queryParams().get('q')?.trim() ?? '');

  protected readonly kind = computed<GlobalSearchCollection | null>(
    () => (this.queryParams().get('kind') as GlobalSearchCollection | null) ?? null,
  );

  protected readonly loading = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly hits = signal<readonly GlobalSearchHitDto[]>([]);

  /** How many records the page asks for. `GlobalSearchQueryHandler.MaxLimit` clamps it server-side. */
  private static readonly Limit = 50;

  protected readonly kinds: readonly KindOption[] = [
    { value: null, label: 'Everything' },
    { value: 'Document', label: 'Documents' },
    { value: 'Contact', label: 'Contacts' },
    { value: 'Product', label: 'Products' },
    { value: 'Account', label: 'Accounts' },
  ];

  /**
   * Screens matching the term. Client-side and instant, from the router config — the same catalogue
   * the dropdown's navigation half renders from. Shown only under "Everything": a user who has
   * narrowed to Contacts is not asking about screens.
   */
  protected readonly screens = computed(() =>
    this.kind() === null ? this.catalog.search(this.term(), 12) : [],
  );

  protected readonly isEmpty = computed(
    () => !this.loading() && this.hits().length === 0 && this.screens().length === 0,
  );

  constructor() {
    // An effect is the right shape here precisely because its source is *external*: the url. Phase
    // 34b's warning is about an effect over a signal the same handler just wrote -- it cannot tell
    // "already being acted on" from "needs action" -- and neither half applies to a route parameter
    // nothing in this component sets directly. Arriving, filtering and the back button are then all
    // one rule instead of three.
    effect(() => {
      const term = this.term();
      const kind = this.kind();

      untracked(() => this.load(term, kind));
    });
  }

  protected select(kind: GlobalSearchCollection | null): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { q: this.term(), kind },
      // The filter is part of where you are, not a step in how you got here: a Back press should
      // leave the results page rather than walk four filters backwards.
      replaceUrl: true,
    });
  }

  protected label(hit: GlobalSearchHitDto): string {
    return hitLabel(hit);
  }

  protected description(hit: GlobalSearchHitDto): string {
    return hitDescription(hit);
  }

  protected badge(hit: GlobalSearchHitDto): string {
    return hit.collection === 'Document' ? (hit.documentType ?? 'Document') : hit.collection;
  }

  protected link(hit: GlobalSearchHitDto): unknown[] | null {
    return hitRouterLink(this.organizationId, hit);
  }

  protected linkParams(hit: GlobalSearchHitDto): Record<string, string> | undefined {
    return hitQueryParams(hit);
  }

  protected screenLink(url: string): unknown[] {
    return ['/organizations', this.organizationId, ...url.split('/').filter((x) => x.length > 0)];
  }

  private load(term: string, kind: GlobalSearchCollection | null): void {
    // The server refuses a shorter term anyway; asking is what turns a 400 into an empty page.
    if (term.length < 2) {
      this.hits.set([]);

      return;
    }

    this.loading.set(true);
    this.errorMessage.set(null);

    this.platform.search(this.organizationId, term, SearchResultsPage.Limit, kind).subscribe({
      next: (hits) => {
        this.hits.set(hits);
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.hits.set([]);
        this.errorMessage.set(extractErrorMessage(error));
      },
    });
  }
}
