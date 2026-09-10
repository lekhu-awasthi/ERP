import { Component, ElementRef, HostListener, computed, effect, inject, input, signal, viewChild } from '@angular/core';
import { Router } from '@angular/router';
import { Subject, of } from 'rxjs';
import { catchError, debounceTime, distinctUntilChanged, switchMap } from 'rxjs/operators';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

import { GlobalSearchHitDto, QuickLinkDto } from '../../core/platform/platform.models';
import { PlatformService } from '../../core/platform/platform.service';
import { NavigationCatalog } from '../navigation/navigation-catalog';
import { hitDescription, hitLabel, hitRouterLink } from '../navigation/search-routes';

/** A row in the dropdown: either a stored record or a screen to navigate to. */
interface ResultRow {
  readonly label: string;
  readonly description: string;
  readonly kindBadge: string;
  readonly link: unknown[] | null;
}

/**
 * Phase 33 — the top bar's global search (Ctrl + /), confirmed live against the reference product.
 *
 * <b>Two halves, from two places.</b> The navigation half comes from {@link NavigationCatalog},
 * which derives itself from the router config, so it renders on the first keystroke with no round
 * trip and can never offer a route that does not exist. The record half comes from the server, which
 * is the only side that can answer "is there an invoice numbered this, and may you see it".
 *
 * <b>Zoneless notes.</b> The input's value is tracked in its own `signal()` written by the input
 * handler, never read from the DOM inside a `computed()` — CLAUDE.md's phase-17 gotcha, which bites
 * a search box more directly than anything else in the app. The debounce runs through an RxJS
 * `Subject` rather than a signal `effect`, so a keystroke that lands mid-flight cancels the previous
 * request via `switchMap` instead of racing it.
 */
@Component({
  selector: 'app-global-search',
  templateUrl: './global-search.html',
  styleUrl: './global-search.scss',
})
export class GlobalSearch {
  private readonly platform = inject(PlatformService);
  private readonly catalog = inject(NavigationCatalog);
  private readonly router = inject(Router);
  private readonly host = inject(ElementRef<HTMLElement>);

  readonly organizationId = input.required<string>();

  private readonly box = viewChild<ElementRef<HTMLInputElement>>('box');

  protected readonly term = signal('');
  protected readonly open = signal(false);
  protected readonly searching = signal(false);
  protected readonly hits = signal<readonly GlobalSearchHitDto[]>([]);
  protected readonly highlighted = signal(0);

  /** How many navigation rows the dropdown will show before the record rows. */
  private static readonly NavigationLimit = 6;

  private readonly typed = new Subject<string>();

  protected readonly rows = computed<ResultRow[]>(() => {
    const term = this.term().trim();

    if (term.length === 0) {
      return [];
    }

    const screens: ResultRow[] = this.catalog.search(term, GlobalSearch.NavigationLimit).map((x) => ({
      label: x.kind === 'Add' ? x.name : x.name,
      description: x.area,
      kindBadge: x.kind === 'Add' ? 'New' : 'Screen',
      link: this.catalog.routerLink(this.organizationId(), x as QuickLinkDto),
    }));

    const records: ResultRow[] = this.hits().map((hit) => ({
      label: hitLabel(hit),
      description: hitDescription(hit),
      kindBadge: hit.collection === 'Document' ? 'Document' : hit.collection,
      link: hitRouterLink(this.organizationId(), hit),
    }));

    return [...records, ...screens];
  });

  constructor() {
    this.typed
      .pipe(
        debounceTime(250),
        distinctUntilChanged(),
        switchMap((term) => {
          // Below the server's own minimum the request would be refused anyway; skip it and let the
          // navigation half answer alone, which is what makes one- and two-letter typing feel instant.
          if (term.trim().length < 2) {
            this.searching.set(false);
            return of<GlobalSearchHitDto[]>([]);
          }

          this.searching.set(true);

          return this.platform.search(this.organizationId(), term.trim()).pipe(
            // A failed search must not blank the navigation half or throw a red banner over the
            // shell: the box keeps working and simply finds no records.
            catchError(() => of<GlobalSearchHitDto[]>([])),
          );
        }),
        takeUntilDestroyed(),
      )
      .subscribe((hits) => {
        this.hits.set(hits);
        this.searching.set(false);
      });

    // Re-clamp the keyboard cursor whenever the result set changes underneath it.
    effect(() => {
      const count = this.rows().length;

      if (this.highlighted() >= count) {
        this.highlighted.set(Math.max(0, count - 1));
      }
    });
  }

  /**
   * Whether the dropdown is showing. Phase 33's template repeated this condition inline; phase 34a
   * needs the same truth in three places (`aria-expanded`, `aria-activedescendant` and the panel
   * itself), and three copies of a condition are three chances for the exposed state to disagree
   * with the visible one.
   */
  protected readonly isOpen = computed(() => this.open() && this.term().trim().length > 0);

  /**
   * The id of the highlighted row, or null when there is nothing to point at -- WCAG 4.1.2. An
   * `aria-activedescendant` naming an element that is not in the DOM is worse than none at all:
   * the screen reader announces nothing and the user cannot tell the difference from a broken box.
   */
  protected readonly activeOptionId = computed(() =>
    this.isOpen() && this.rows().length > 0 ? `global-search-option-${this.highlighted()}` : null,
  );

  /** What the live region says: the only signal a screen-reader user gets that results arrived. */
  protected readonly resultAnnouncement = computed(() => {
    if (!this.isOpen()) {
      return '';
    }

    if (this.searching()) {
      return 'Searching…';
    }

    const count = this.rows().length;

    return count === 0 ? 'No matches.' : `${count} result${count === 1 ? '' : 's'} available.`;
  });

  protected onInput(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.term.set(value);
    this.open.set(true);
    this.highlighted.set(0);
    this.typed.next(value);
  }

  protected onKeydown(event: KeyboardEvent): void {
    const rows = this.rows();

    if (event.key === 'Escape') {
      this.dismiss();
      return;
    }

    if (rows.length === 0) {
      return;
    }

    if (event.key === 'ArrowDown') {
      event.preventDefault();
      this.highlighted.set((this.highlighted() + 1) % rows.length);
      return;
    }

    if (event.key === 'ArrowUp') {
      event.preventDefault();
      this.highlighted.set((this.highlighted() - 1 + rows.length) % rows.length);
      return;
    }

    if (event.key === 'Enter') {
      event.preventDefault();
      this.go(rows[this.highlighted()]);
    }
  }

  protected go(row: ResultRow): void {
    if (!row.link) {
      return;
    }

    this.dismiss();
    void this.router.navigate(row.link);
  }

  protected dismiss(): void {
    this.open.set(false);
    this.term.set('');
    this.hits.set([]);

    const input = this.box()?.nativeElement;

    if (input) {
      input.value = '';
      input.blur();
    }
  }

  /**
   * Ctrl + / focuses the box, exactly as the reference product's own hint in the input's suffix
   * advertises. Bound on the document rather than the input, since the point is to reach it from
   * anywhere.
   */
  @HostListener('document:keydown', ['$event'])
  protected onDocumentKeydown(event: KeyboardEvent): void {
    if ((event.ctrlKey || event.metaKey) && event.key === '/') {
      event.preventDefault();
      this.box()?.nativeElement.focus();
      this.open.set(true);
    }
  }

  /** Clicking anywhere else closes the dropdown; a `.dropdown-menu` would not, as phase 22 found. */
  @HostListener('document:click', ['$event'])
  protected onDocumentClick(event: MouseEvent): void {
    if (this.open() && !this.host.nativeElement.contains(event.target as Node)) {
      this.open.set(false);
    }
  }
}
