import { Component, DestroyRef, Injector, afterNextRender, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { POS_TAB_LABELS } from '../../../core/pos/pos.models';
import {
  POS_ORDER_TYPES,
  PosKitchenBoard,
  PosKitchenBoardLine,
  PosKitchenBoardTicket,
  PosKitchenBoardView,
  PosOrderType,
} from '../../../core/pos/pos-restaurant.models';
import { PosRestaurantService } from '../../../core/pos/pos-restaurant.service';
import { StatusBanner } from '../../../shared/a11y/status-banner';
import { NepaliDatePipe } from '../../../shared/formatting/nepali-date-pipe';

/** How often the board asks again while it is on screen. */
export const KITCHEN_POLL_MS = 10_000;

/**
 * Phase 65 -- the kitchen board: every kitchen ticket at this location, one card each, with what is
 * still to cook; the cook marks a line, or the whole ticket, served. Under Pos.Kitchen.Operate.
 *
 * <p><b>Live by polling</b> (Decision F): the board asks every ten seconds while the tab is visible,
 * at once when it becomes visible again, and on Refresh. The vendor's board has only the button. New
 * tickets are announced politely ("2 new kitchen tickets"), never on the first load.</p>
 *
 * <p><b>Nothing is archived by age.</b> A ticket stays pending until it is served or cancelled, oldest
 * first, with how long it has waited -- the vendor hides one unserved for five hours.</p>
 *
 * <p>A cancellation (a discard) shows on the card of the food it cancels, struck through with its
 * count, and as its own card under All with the reason.</p>
 */
@Component({
  selector: 'app-pos-kitchen-page',
  imports: [RouterLink, StatusBanner, NepaliDatePipe],
  templateUrl: './pos-kitchen-page.html',
  styleUrl: './pos-kitchen-page.scss',
})
export class PosKitchenPage {
  private readonly route = inject(ActivatedRoute);
  private readonly restaurantService = inject(PosRestaurantService);
  private readonly injector = inject(Injector);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;
  protected readonly locationId = this.route.snapshot.paramMap.get('locationId')!;
  protected readonly tabLabels = POS_TAB_LABELS;
  protected readonly orderTypes = POS_ORDER_TYPES;
  protected readonly views: readonly PosKitchenBoardView[] = ['Pending', 'Served', 'All'];

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly announcement = signal('');
  protected readonly board = signal<PosKitchenBoard | null>(null);
  protected readonly busyTicketId = signal<string | null>(null);

  protected readonly view = signal<PosKitchenBoardView>('Pending');
  /** A station's id, 'default', or '' for every station. */
  protected readonly station = signal('');
  protected readonly orderType = signal<PosOrderType | ''>('');

  /** Ticks every poll, so the waiting times move. */
  protected readonly now = signal(Date.now());

  private knownPending: Set<string> | null = null;
  private timer: ReturnType<typeof setInterval> | null = null;

  protected readonly tickets = computed(() => this.board()?.tickets ?? []);

  constructor() {
    this.refresh();
    this.timer = setInterval(() => this.poll(), KITCHEN_POLL_MS);
    const onVisible = () => {
      if (document.visibilityState === 'visible') this.refresh();
    };
    document.addEventListener('visibilitychange', onVisible);
    this.destroyRef.onDestroy(() => {
      if (this.timer) clearInterval(this.timer);
      document.removeEventListener('visibilitychange', onVisible);
    });
  }

  private poll(): void {
    if (document.visibilityState !== 'visible') return;
    this.refresh();
  }

  protected refresh(focusId?: string): void {
    this.restaurantService
      .getKitchenBoard(this.organizationId, this.locationId, {
        view: this.view(),
        station: this.station() || null,
        orderType: this.orderType() || null,
      })
      .subscribe({
        next: (board) => {
          this.loading.set(false);
          this.errorMessage.set(null);
          this.now.set(Date.now());
          this.noteNewTickets(board);
          this.board.set(board);
          if (focusId) {
            afterNextRender(() => this.focus(focusId), { injector: this.injector });
          }
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not read the kitchen board.');
        },
      });
  }

  /** Announces tickets that were not pending on the last read; the first read only learns them. */
  private noteNewTickets(board: PosKitchenBoard): void {
    if (this.view() !== 'Pending') return;
    const pending = new Set(board.tickets.filter((t) => t.state === 'Pending').map((t) => t.id));
    if (this.knownPending) {
      const fresh = [...pending].filter((id) => !this.knownPending!.has(id)).length;
      if (fresh > 0) this.announcement.set(`${fresh} new kitchen ticket${fresh === 1 ? '' : 's'}.`);
    }
    this.knownPending = pending;
  }

  protected chooseView(view: PosKitchenBoardView): void {
    this.view.set(view);
    this.knownPending = null;
    this.refresh();
  }

  protected onStation(event: Event): void {
    this.station.set((event.target as HTMLSelectElement).value);
    this.knownPending = null;
    this.refresh();
  }

  protected onOrderType(event: Event): void {
    this.orderType.set((event.target as HTMLSelectElement).value as PosOrderType | '');
    this.knownPending = null;
    this.refresh();
  }

  /** Serves one line of the ticket, or every line when `line` is omitted. */
  protected serve(ticket: PosKitchenBoardTicket, line?: PosKitchenBoardLine): void {
    this.busyTicketId.set(ticket.id);
    const items = line ? [{ lineId: line.orderLineId, quantity: line.pending }] : [];
    this.restaurantService.serveTicket(this.organizationId, ticket.orderId, ticket.id, items).subscribe({
      next: (result) => {
        this.busyTicketId.set(null);
        this.announcement.set(
          result.state === 'Pending'
            ? `${line?.productName ?? 'Items'} served on ${ticket.number}.`
            : `${ticket.number} served.`,
        );
        // Focus stays on the card while it is on this view, and goes to the board's heading when it leaves.
        this.refresh(result.state === 'Pending' || this.view() !== 'Pending' ? `pos-kot-${ticket.id}` : 'pos-kitchen-heading');
      },
      error: (err: unknown) => {
        this.busyTicketId.set(null);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not mark it served.');
      },
    });
  }

  private focus(id: string): void {
    const element = document.getElementById(id) ?? document.getElementById('pos-kitchen-heading');
    element?.focus();
  }

  /** How long a ticket has waited, in minutes, from the last read. */
  protected waited(ticket: PosKitchenBoardTicket): string {
    const minutes = Math.max(0, Math.floor((this.now() - Date.parse(ticket.createdAt)) / 60_000));
    if (minutes < 60) return `${minutes} min`;
    return `${Math.floor(minutes / 60)} h ${minutes % 60} min`;
  }

  protected viewLabel(view: PosKitchenBoardView): string {
    const pending = this.board()?.pendingCount;
    return view === 'Pending' && pending !== undefined ? `Pending (${pending})` : view;
  }
}
