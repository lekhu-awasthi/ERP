import { ChangeDetectionStrategy, Component, computed, input, output, signal } from '@angular/core';

/**
 * Phase 40 — the search box for a list that is not paginated (phase 34b's carried item #3).
 *
 * <b>Why this is not {@link ListChrome}.</b> The chrome answers a paginated screen's whole question:
 * it owns a heading, a result count over the server's filtered set, a location filter and a pager,
 * and it emits a term the page sends to the server. The Configurations lookups have none of that —
 * they fetch every row through `listAll`, render an inline add/edit form above the rows, and are
 * reached from the Configurations index rather than from the nav. Giving them the chrome would mean
 * giving them a second `<h1>` and a pager over one page. What they are missing is one control.
 *
 * <b>Why client-side.</b> 34b left this saying the server "now supports `?search=` on all of them,
 * and no screen sends it". It does — but sending it would mean a request per keystroke for a list
 * the browser already holds in full, and a round trip that can reorder against the typing. The rows
 * are all here; filtering them is a `computed()`. The server's `?search=` stays the right answer for
 * the paginated lists, which cannot hold their rows.
 *
 * The count is announced the same way {@link ListChrome}'s is, and for the same reason (WCAG 4.1.3):
 * a search that swaps the rows underneath and says nothing leaves a screen-reader user with no way
 * to know whether the term matched.
 */
@Component({
  selector: 'app-lookup-filter',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="d-flex justify-content-between align-items-end flex-wrap gap-3 mb-3">
      <div style="min-width: 15rem;">
        <label class="form-label small text-muted mb-1" [attr.for]="controlId()">Search</label>
        <div class="input-group input-group-sm">
          <span class="input-group-text bg-white">
            <i aria-hidden="true" class="bi bi-search"></i>
          </span>
          <input
            [attr.id]="controlId()"
            type="search"
            class="form-control"
            [placeholder]="placeholder()"
            [value]="term()"
            (input)="onInput($event)"
          />
          @if (term()) {
            <button type="button" class="btn btn-outline-secondary" (click)="clear()">
              <i aria-hidden="true" class="bi bi-x-lg"></i>
              <span class="visually-hidden">Clear search</span>
            </button>
          }
        </div>
      </div>

      <p class="text-muted small mb-0" aria-live="polite">{{ countLabel() }}</p>
    </div>
  `,
})
export class LookupFilter {
  /** Unique per screen, because a page may hold two of these (Reporting Tags has categories and options). */
  readonly controlId = input.required<string>();
  readonly placeholder = input('Search…');
  /** How many rows are showing after the filter, and how many there are in total. */
  readonly shown = input.required<number>();
  readonly total = input.required<number>();

  readonly termChange = output<string>();

  /** A plain signal written by the input's handler — never a `computed()` over a control's value,
   * which caches forever in a zoneless app (phase-17). */
  protected readonly term = signal('');

  protected readonly countLabel = computed(() =>
    this.term() ? `${this.shown()} of ${this.total()} shown` : `${this.total()} ${this.total() === 1 ? 'row' : 'rows'}`,
  );

  protected onInput(event: Event): void {
    this.term.set((event.target as HTMLInputElement).value);
    this.termChange.emit(this.term());
  }

  protected clear(): void {
    this.term.set('');
    this.termChange.emit('');
  }
}

/**
 * The match, in one place so five screens cannot disagree about what a term means.
 *
 * <p>Case-insensitive and substring, which is what SQL Server's default collation makes `?search=`
 * do on the paginated lists — so the two halves of the app answer the same question. (The
 * <i>server</i>'s case-insensitivity comes from the collation rather than from the expression; see
 * `SearchTerm` for why a handler cannot pass a `StringComparison`.)</p>
 */
export function matchesLookup(term: string, ...fields: readonly (string | null | undefined)[]): boolean {
  const needle = term.trim().toLowerCase();
  return needle === '' || fields.some((f) => (f ?? '').toLowerCase().includes(needle));
}
