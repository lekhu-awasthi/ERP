import { Signal, computed, effect, signal } from '@angular/core';

import { DateRangeService } from '../platform/date-range.service';

/**
 * Phase 34b — what a list screen sends beyond its page number.
 *
 * The two halves have different owners, which is why they travel together in one object: the term
 * belongs to the screen (its own search box), the range belongs to the shell (the top bar's global
 * date filter). A screen passes both or neither, so a reader of a `load()` can see the whole of what
 * narrowed the result.
 */
export interface ListQueryOptions {
  readonly search?: string;
  /** ISO `yyyy-MM-dd`. Only sent by screens over an aggregate with a business date. */
  readonly fromDate?: string;
  readonly toDate?: string;
  /**
   * Phase 35a — the screen's Billing Location filter. A third owner joins the two above: the
   * *tenant's* configuration decides whether this dimension exists at all, which is why the control
   * hides itself rather than the page deciding (see `LocationListFilter`). Only ever set by a list
   * over a location-bearing document type; a master-data list leaves it undefined and sends nothing.
   */
  readonly locationId?: string;
}

/** Folds the options into an existing query-parameter bag, omitting anything unset. */
export function applyListOptions(
  params: Record<string, string>,
  options?: ListQueryOptions,
): Record<string, string> {
  if (options?.search) {
    params['search'] = options.search;
  }

  if (options?.fromDate) {
    params['fromDate'] = options.fromDate;
  }

  if (options?.toDate) {
    params['toDate'] = options.toDate;
  }

  if (options?.locationId) {
    params['locationId'] = options.locationId;
  }

  return params;
}

/**
 * The per-screen filter state, so 23 list pages do not each hand-roll a search signal, a page reset
 * and a date-range read.
 *
 * <b>A plain class, constructed by the page</b>, rather than an `@Injectable`: two list screens can
 * be alive at once (a route being torn down while the next initialises), and a root-provided service
 * would share one term between them. It holds no dependency of its own beyond the shell's range.
 *
 * The term lives in a `signal` written by the search box's own handler — never a `computed()` over a
 * `FormControl.value`, which caches forever in a zoneless app (phase-17).
 */
export class ListFilter {
  readonly search = signal('');

  /**
   * Phase 35a — the screen's chosen billing location, empty for "All locations". Lives here beside
   * the term for the same reason the term does: a reader of a `load()` sees everything that
   * narrowed the result in one object, and fifteen list pages do not each hand-roll the signal.
   */
  readonly location = signal('');

  /**
   * @param dateRange the shell's global range, or null for a screen whose aggregate has no business
   *   date — master data and configuration lookups, which is also what the reference product does
   *   (its `products` and `contact-groups` lists receive no range while its documents do).
   * @param reload what to call when the *global* range changes under a screen that is already open.
   *
   * <b>The `reload` argument is not optional in spirit, and the browser pass is what proved it.</b>
   * Without it the first render of a list showed "Last 30 days: 2026-08-11 – 2026-09-10" in its
   * chrome while listing an invoice dated 2026-07-20: the range is loaded from the per-user store
   * *after* the page's constructor has already issued its request, so the label described one
   * window and the rows came from another. A filter the page displays but did not apply is worse
   * than no filter — it is a wrong answer that looks checked.
   */
  constructor(
    private readonly dateRange: DateRangeService | null = null,
    reload: (() => void) | null = null,
  ) {
    if (!dateRange || !reload) {
      return;
    }

    let previous = `${dateRange.from()}|${dateRange.to()}`;

    // Constructed from a component's field initialiser, so this runs inside an injection context and
    // is torn down with the component. The first run is skipped by comparison rather than by a flag,
    // because the range may legitimately settle to the same window it started at.
    effect(() => {
      const current = `${dateRange.from()}|${dateRange.to()}`;

      if (current === previous) {
        return;
      }

      previous = current;
      reload();
    });
  }

  /**
   * True when this screen's rows are narrowed by the top bar, so the chrome can say so.
   *
   * A getter, not an initialised field: whether a parameter property is assigned before or after
   * field initialisers depends on `useDefineForClassFields`, and a field here would read `undefined`
   * under one setting and work under the other.
   */
  get dateScoped(): boolean {
    return this.dateRange !== null;
  }

  readonly options: Signal<ListQueryOptions> = computed(() => ({
    search: this.search() || undefined,
    fromDate: this.dateRange?.from(),
    toDate: this.dateRange?.to(),
    locationId: this.location() || undefined,
  }));
}
