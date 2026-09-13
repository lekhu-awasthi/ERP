import { Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

import { QuickLinkDto, UserPreferenceKeys } from '../../core/platform/platform.models';
import { PlatformService } from '../../core/platform/platform.service';
import { NavigationCatalog } from '../navigation/navigation-catalog';

/**
 * Phase 33 — the Home dashboard's personalisable Quick Links tray, the feature phase 23 recorded as
 * "still not built" because per-user server storage did not exist.
 *
 * <b>Server-stored, and that is the observation that decided the whole phase.</b> The reference
 * product serves this tray from `GET/POST /quick-links` — a shortcut tray that lived in one browser
 * would be a worse feature than none — which is what makes the per-user store worth building and, in
 * turn, what lets phase-23's declined calendar boolean move behind the same endpoint for free.
 *
 * <b>Whole-list replace on Done, nothing before it.</b> Adding and removing are staged locally and a
 * single write persists the tray in display order, matching the reference product's own POST exactly.
 * There is no per-link endpoint there and none here.
 *
 * <b>A Quick Link is a screen, never a record</b> — confirmed live: its picker offers list and add
 * screens only. Ours is fed by {@link NavigationCatalog}, the same catalogue the global search's
 * navigation half renders from, which is the same single vocabulary the reference product shares
 * between its two features.
 */
@Component({
  selector: 'app-quick-links',
  imports: [RouterLink],
  templateUrl: './quick-links.html',
  styleUrl: './quick-links.scss',
})
export class QuickLinks implements OnInit {
  private readonly platform = inject(PlatformService);
  private readonly catalog = inject(NavigationCatalog);

  readonly organizationId = input.required<string>();

  protected readonly links = signal<readonly QuickLinkDto[]>([]);
  protected readonly editing = signal(false);
  protected readonly picking = signal(false);
  protected readonly saving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  /**
   * The picker's filter. Tracked in its own signal written by the input handler rather than read off
   * the control inside a `computed()` — the app is zoneless, so a `computed()` over a plain
   * `FormControl.value` caches forever (phase-17's gotcha).
   */
  protected readonly pickerTerm = signal('');

  protected readonly available = computed(() => {
    const taken = new Set(this.links().map((x) => x.url));
    const term = this.pickerTerm().trim().toLowerCase();

    return this.catalog
      .entries()
      .filter((x) => !taken.has(x.url))
      .filter(
        (x) =>
          term.length === 0 ||
          x.name.toLowerCase().includes(term) ||
          x.area.toLowerCase().includes(term),
      );
  });

  /**
   * Loaded in `ngOnInit`, not in the constructor: `organizationId` is a required signal input, and
   * reading one before Angular has set it throws NG0950 -- which surfaces as every test of the host
   * page failing rather than as anything about this component.
   */
  ngOnInit(): void {
    this.load();
  }

  protected routerLink(link: QuickLinkDto): unknown[] {
    return this.catalog.routerLink(this.organizationId(), link);
  }

  protected startEditing(): void {
    this.editing.set(true);
    this.errorMessage.set(null);
  }

  protected add(link: QuickLinkDto): void {
    this.links.update((current) => [...current, link]);
    this.picking.set(false);
    this.pickerTerm.set('');
  }

  protected remove(link: QuickLinkDto): void {
    this.links.update((current) => current.filter((x) => x.url !== link.url));
  }

  protected move(link: QuickLinkDto, delta: number): void {
    const index = this.links().findIndex((x) => x.url === link.url);
    this.moveTo(index, index + delta);
  }

  // --- drag to reorder (phase 39, closing phase 33's carried item #2) -------------------------

  /**
   * The link currently being dragged. Tracked rather than read off the DataTransfer, because
   * `dragover` cannot read the payload it is hovering over -- browsers deliberately hide it until
   * `drop` -- and the tile under the cursor needs to know it is a target now, not afterwards.
   */
  protected readonly draggingUrl = signal<string | null>(null);

  protected onDragStart(link: QuickLinkDto, event: DragEvent): void {
    this.draggingUrl.set(link.url);

    // Firefox ignores a drag that sets no data at all, so something has to go in even though the
    // component reads its own signal rather than this payload.
    event.dataTransfer?.setData('text/plain', link.url);

    if (event.dataTransfer) {
      event.dataTransfer.effectAllowed = 'move';
    }
  }

  /** Without preventDefault on dragover the element is not a drop target and `drop` never fires. */
  protected onDragOver(event: DragEvent): void {
    if (this.draggingUrl()) {
      event.preventDefault();

      if (event.dataTransfer) {
        event.dataTransfer.dropEffect = 'move';
      }
    }
  }

  protected onDrop(target: QuickLinkDto, event: DragEvent): void {
    event.preventDefault();
    const source = this.draggingUrl();
    this.draggingUrl.set(null);

    if (!source || source === target.url) {
      return;
    }

    const current = this.links();
    this.moveTo(
      current.findIndex((x) => x.url === source),
      current.findIndex((x) => x.url === target.url),
    );
  }

  /** Fires even when the drag ended outside a drop target, so the highlight always clears. */
  protected onDragEnd(): void {
    this.draggingUrl.set(null);
  }

  /**
   * Moves an item to a position, rather than swapping two.
   *
   * <p>The distinction only shows up over a distance: dragging the sixth tile onto the first should
   * put it first and push the rest down, where a swap would also fling the old first tile to
   * position six. For the adjacent case the arrow buttons use, the two are identical -- which is why
   * both gestures can share this one method.</p>
   */
  private moveTo(from: number, to: number): void {
    this.links.update((current) => {
      if (from < 0 || to < 0 || from >= current.length || to >= current.length || from === to) {
        return current;
      }

      const next = [...current];
      const [moved] = next.splice(from, 1);
      next.splice(to, 0, moved);

      return next;
    });
  }

  protected onPickerInput(event: Event): void {
    this.pickerTerm.set((event.target as HTMLInputElement).value);
  }

  /** Done: one whole-list write, then leave edit mode only if it actually persisted. */
  protected done(): void {
    this.saving.set(true);
    this.errorMessage.set(null);

    this.platform
      .setPreference(this.organizationId(), UserPreferenceKeys.quickLinks, JSON.stringify(this.links()))
      .subscribe({
        next: () => {
          this.saving.set(false);
          this.editing.set(false);
          this.picking.set(false);
        },
        error: () => {
          this.saving.set(false);
          this.errorMessage.set('Could not save your Quick Links. Your changes are still here — try again.');
        },
      });
  }

  private load(): void {
    this.platform.getPreferences(this.organizationId()).subscribe({
      next: (preferences) => {
        const stored = preferences.find((x) => x.key === UserPreferenceKeys.quickLinks);
        this.links.set(stored ? parse(stored.value) : defaultTray(this.catalog));
      },
      // A tray that cannot be read is an empty tray, not an error banner over the dashboard.
      error: () => this.links.set([]),
    });
  }
}

function parse(value: string): QuickLinkDto[] {
  try {
    const parsed: unknown = JSON.parse(value);
    return Array.isArray(parsed) ? (parsed as QuickLinkDto[]) : [];
  } catch {
    return [];
  }
}

/**
 * What a user who has never edited their tray sees. The reference product seeds six on a fresh tenant
 * (Customers, Products, Charts Of Account, Invoice, Purchase Bills, Journal Voucher — observed on a
 * tenant that had never been customised), so these are those six, resolved through the catalogue so
 * a route rename cannot leave a default pointing at nothing.
 *
 * It is a *default*, not a seed: nothing is written until the user presses Done, so a user who never
 * touches the tray has no row at all.
 */
function defaultTray(catalog: NavigationCatalog): QuickLinkDto[] {
  const wanted = [
    '/contacts',
    '/products',
    '/accounting/accounts',
    '/sales/invoices',
    '/purchasing/purchase-bills',
    '/accounting/journal-vouchers',
  ];

  const byUrl = new Map(catalog.entries().map((x) => [x.url, x]));

  return wanted.map((url) => byUrl.get(url)).filter((x): x is QuickLinkDto => x !== undefined);
}
