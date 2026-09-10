import { Component, computed, inject, input, output, signal } from '@angular/core';

import { DateRangeService } from '../platform/date-range.service';

/**
 * Phase 34b (NFR-6.1) — the one interaction model every list screen presents.
 *
 * <b>The measurement this exists to fix.</b> At the start of this phase, of 46 list pages: 41 had an
 * `<h1>`, 27 a pager, and **1** a search box. Nobody had decided the other 45 should not have one —
 * the question had simply never been asked on any screen but the one. This component is the answer
 * being in a single place, so the next list gets it by wiring rather than by remembering.
 *
 * <b>What it deliberately is not</b> is a list. It renders the chrome *above* the rows — title,
 * result count, search box, sort control, and a slot for the page's own actions — and the host page
 * keeps rendering its rows however it already did. That is Decision C: 36 of 46 lists are
 * `list-group` card rows and 10 are tables, 34a made both conformant, and converting them would be a
 * large visual diff with no accessibility gain. The consistency NFR-6.1 asks for is the *controls*,
 * not the row markup — and the price of that decision, stated: our rows are not sortable by clicking
 * a column header, so sorting lives in the select here instead.
 *
 * It is deliberately dumb in the same way {@link PaginationControl} is: it owns no data and issues
 * no request. The host page reloads.
 */
@Component({
  selector: 'app-list-chrome',
  imports: [],
  templateUrl: './list-chrome.html',
  styleUrl: './list-chrome.scss',
})
export class ListChrome {
  /** The screen's name. Rendered as the page's `<h1>`, which 5 of 46 list pages did not have. */
  readonly heading = input.required<string>();
  readonly description = input<string>('');
  /** A Bootstrap Icons name, without the `bi-` prefix. */
  readonly icon = input<string>('list-ul');

  /** The server's count over the whole filtered set — never the length of the current page. */
  readonly totalCount = input<number | null>(null);
  readonly loading = input(false);

  readonly searchPlaceholder = input('Search…');
  /** Set false on a screen whose query takes no term (the exempt ones — see `SearchSweepGuardTests`). */
  readonly searchable = input(true);

  /**
   * Whether this screen's list is scoped by the shell's global date range, so the chrome can *say*
   * so. A filter the user cannot see is a filter they will report as missing data — the range lives
   * in the top bar, several hundred pixels from the rows it is hiding.
   */
  readonly dateScoped = input(false);

  readonly sortOptions = input<readonly ListSortOption[]>([]);
  readonly sort = input<string>('');

  readonly searchChange = output<string>();
  readonly sortChange = output<string>();

  protected readonly dateRange = inject(DateRangeService);

  /**
   * The box's live value. A plain signal written by the input's own handler — not a `computed()`
   * over a `FormControl.value`, which caches forever in a zoneless app (phase-17).
   */
  protected readonly term = signal('');

  private timer: ReturnType<typeof setTimeout> | null = null;

  protected readonly countLabel = computed(() => {
    const total = this.totalCount();

    if (this.loading()) {
      return 'Loading…';
    }

    if (total === null) {
      return '';
    }

    return total === 1 ? '1 result' : `${total} results`;
  });

  // There is deliberately no `effect()` watching `term` for the empty case. The first draft had one,
  // to make clearing the box reload immediately -- and it cancelled the pending emit *instead of*
  // letting it run, so clearing the search never restored the unfiltered list. The browser pass
  // caught it: searching "0003" narrowed 2 rows to 1, and clearing left it at 1. Immediacy is
  // already handled below by giving an empty value a 0ms delay; the effect only ever raced it.

  protected onInput(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.term.set(value);

    if (this.timer) {
      clearTimeout(this.timer);
    }

    // A request per keystroke over 25 endpoints is the obvious way to make a search box feel worse
    // than no search box. 300ms is below the threshold at which typing feels laggy and well above a
    // fast typist's inter-key interval.
    this.timer = setTimeout(() => this.searchChange.emit(value.trim()), value.length === 0 ? 0 : 300);
  }

  protected clear(): void {
    this.term.set('');
    this.searchChange.emit('');
  }

  protected onSort(event: Event): void {
    this.sortChange.emit((event.target as HTMLSelectElement).value);
  }
}

export interface ListSortOption {
  readonly value: string;
  readonly label: string;
}
