import { Injectable, inject, signal } from '@angular/core';
import { NavigationEnd, Router } from '@angular/router';
import { filter } from 'rxjs/operators';

import { EXCLUDED_PATHS } from './navigation-catalog';

/** One visited screen. `area` is both the label and the deduplication key — see the service. */
export interface HistoryEntry {
  readonly area: string;
  readonly name: string;
  readonly url: string;
}

const STORAGE_KEY = 'erp.history';

/**
 * Phase 33 — the top bar's History popover. **Client-only, by observation.**
 *
 * <b>Decision C, settled by the confirm-live pass rather than by costing it.</b> The reference
 * product's History icon reads straight out of its own `localStorage["history"]`: opening the
 * popover makes no request, and — the part that matters — *navigating makes no request either*.
 * There is no server-side record of what anyone opened. The cost argument (a write on every document
 * open) pointed the same way, but it did not have to be made.
 *
 * <b>It is a list of screens, one per area, not a list of records.</b> Also observed, and it
 * corrects what the roadmap and the module scan both recorded. Navigating Tigg Subscriptions then
 * Users & Permissions leaves one Configurations entry, not two, while Sales / CRM / Inventory /
 * Accounting entries coexist. The reference product does store a record-detail *url* — but it
 * derives the label from the route, so that entry still renders as "CRM · Contacts" and never shows
 * the record's name. This copies that exactly, deduplicating on {@link HistoryEntry.area}, which is
 * what bounds the list to the number of areas rather than needing a length cap.
 *
 * <b>Written on leaving, not on arriving</b> — again matching the observed behaviour: the entry for
 * the screen you are on appears once you go somewhere else, so the popover never offers you the page
 * you are looking at.
 */
@Injectable({ providedIn: 'root' })
export class HistoryService {
  private readonly router = inject(Router);
  private readonly stack = signal<readonly HistoryEntry[]>(read());

  /** Most recent first, at most one entry per area. */
  readonly entries = this.stack.asReadonly();

  private pending: HistoryEntry | null = null;

  /**
   * Starts recording. Called once from the shell rather than in the constructor, so a test or a
   * non-shell entry point can construct the service without it subscribing to the router.
   */
  start(): void {
    this.router.events
      .pipe(filter((e): e is NavigationEnd => e instanceof NavigationEnd))
      .subscribe((e) => this.visit(e.urlAfterRedirects));
  }

  /** Records that the previous screen has been left, and remembers the new one for next time. */
  visit(url: string): void {
    const entry = describe(url);

    if (this.pending && this.pending.url !== entry?.url) {
      this.push(this.pending);
    }

    this.pending = entry;
  }

  clear(): void {
    this.pending = null;
    this.stack.set([]);
    write([]);
  }

  private push(entry: HistoryEntry): void {
    const next = [entry, ...this.stack().filter((x) => x.area !== entry.area)];
    this.stack.set(next);
    write(next);
  }
}

/**
 * Turns a router url into a history entry, or null for one that is not worth recording.
 *
 * Deliberately does *not* use `NavigationCatalog`: a detail url (`/sales/invoices/{guid}`) is not a
 * catalogue entry, and it is exactly the case that has to be labelled by its list screen. So the
 * label is derived from the path the same way the catalogue derives its own, which is what makes the
 * two agree.
 */
export function describe(url: string): HistoryEntry | null {
  const path = url.split('?')[0].split('#')[0];
  const segments = path.split('/').filter((s) => s.length > 0);

  // /organizations/{id}/<area>[/<screen>][/...]
  if (segments[0] !== 'organizations' || segments.length < 3) {
    return null;
  }

  const rest = segments.slice(2);
  const head = rest[0];

  if (head in EXCLUDED_PATHS) {
    return null;
  }

  const areaKey = KNOWN_AREAS.has(head) ? head : null;
  const area = areaKey ?? head;
  const nameSegment = areaKey ? (rest[1] ?? head) : head;

  // A guid or a literal `new` is a record, not a screen: keep the list screen's own label, which is
  // what the reference product's popover shows for a record url.
  const name = isRecordSegment(nameSegment) ? titleCase(areaKey ?? head) : titleCase(nameSegment);

  return { area: titleCase(area), name, url: path };
}

/** The first path segments that name an area rather than a screen. */
const KNOWN_AREAS = new Set([
  'accounting',
  'configuration',
  'inventory',
  'manufacturing',
  'purchasing',
  'reports',
  'sales',
  'workflow',
]);

function isRecordSegment(segment: string): boolean {
  return segment === 'new' || /^[0-9a-f-]{36}$/i.test(segment);
}

function titleCase(segment: string): string {
  return segment
    .split('-')
    .map((w) => w.charAt(0).toUpperCase() + w.slice(1))
    .join(' ');
}

function read(): HistoryEntry[] {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    const parsed: unknown = raw ? JSON.parse(raw) : [];

    return Array.isArray(parsed) ? (parsed as HistoryEntry[]).filter(isEntry) : [];
  } catch {
    // Private mode, disabled storage, or a value some earlier version wrote in another shape.
    // History is a convenience; never let reading it break the shell it renders in.
    return [];
  }
}

function isEntry(value: unknown): value is HistoryEntry {
  const entry = value as Partial<HistoryEntry> | null;

  return (
    typeof entry?.area === 'string' &&
    typeof entry?.name === 'string' &&
    typeof entry?.url === 'string'
  );
}

function write(entries: readonly HistoryEntry[]): void {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(entries));
  } catch {
    // As above.
  }
}
