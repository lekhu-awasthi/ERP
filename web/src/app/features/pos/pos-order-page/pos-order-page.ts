import { Component, ElementRef, Injector, afterNextRender, computed, inject, signal, viewChild } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { concatMap, from, toArray } from 'rxjs';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { Contact } from '../../../core/contacts/contacts.models';
import { ContactsService } from '../../../core/contacts/contacts.service';
import { POS_TAB_LABELS, PosProduct } from '../../../core/pos/pos.models';
import {
  KitchenTicket,
  POS_ORDER_TYPES,
  PosOrder,
  PosOrderBillPreview,
  PosOrderLine,
  PosOrderType,
  PosRestaurant,
  PosRestaurantTable,
} from '../../../core/pos/pos-restaurant.models';
import { PosRestaurantService } from '../../../core/pos/pos-restaurant.service';
import { StatusBanner } from '../../../shared/a11y/status-banner';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';
import { NepaliDatePipe } from '../../../shared/formatting/nepali-date-pipe';
import { PosEstimateView } from '../pos-estimate/pos-estimate';
import { PosKotView, PrintedTicket } from '../pos-kot/pos-kot';

/** Something the waiter has added and not yet sent: a new item, or more of a line already sent. */
interface PendingItem {
  key: number;
  kind: 'new' | 'more';
  productId: string;
  productName: string;
  lineId: string | null;
  quantity: number;
  note: string;
}

/** The inline action panel open under a line or the order. */
type Panel =
  | { kind: 'serve'; lineId: string }
  | { kind: 'discard'; lineId: string }
  | { kind: 'details' }
  | { kind: 'void' };

const PAGE_SIZE = 48;

/**
 * Phase 64 -- one restaurant order: a table's tab, a parcel or a delivery. The product grid adds to a
 * list of things <b>not yet sent</b>, kept in this browser; <i>Send to Kitchen</i> sends them, which is
 * the only way anything reaches the server (the vendor's Save Orders, which saves and tickets at once).
 * The order's lines then show every quantity the server derives from its tickets: ordered, discarded,
 * on the order, served and still to serve.
 *
 * <p><b>Nothing here prices anything that is billed.</b> A sent line shows the server's figures; an
 * unsent one shows its quantity only, because the order is an estimate until phase 65 bills it, and a
 * second copy of the arithmetic is how two would drift (phase 63's lesson).</p>
 *
 * <p><b>Discarding needs Pos.Order.Void</b> (Admin by default). The page reads whether the user holds
 * it and says so beside the line rather than offering a button that 403s.</p>
 *
 * <p><b>Kitchen tickets print themselves</b> after a send when the location prints KOTs, one page per
 * station; every print is counted by the server, and a second one prints as a REPRINT.</p>
 *
 * <p><b>Phase 65 -- billing.</b> <i>Bill</i> opens the split-and-pay screen; <i>Print Estimate</i> prints
 * what the order still comes to, priced by the server's bill planner (the estimate bill, which says it
 * is not a tax invoice). A line shows how much of it is billed, and the order lists its bills.</p>
 *
 * <p>Serves <code>pos/orders/new</code> and <code>pos/orders/:orderId</code> from one component, so the
 * id is read from the route's <code>paramMap</code> on every emission (phase 3 bug #1): a new order's
 * first send navigates to its own URL and the component stays.</p>
 */
@Component({
  selector: 'app-pos-order-page',
  imports: [RouterLink, StatusBanner, AmountPipe, NepaliDatePipe, PosKotView, PosEstimateView],
  templateUrl: './pos-order-page.html',
  styleUrl: './pos-order-page.scss',
})
export class PosOrderPage {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly restaurantService = inject(PosRestaurantService);
  private readonly contactsService = inject(ContactsService);
  private readonly injector = inject(Injector);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;
  protected readonly tabLabels = POS_TAB_LABELS;

  private readonly searchBox = viewChild<ElementRef<HTMLInputElement>>('searchBox');

  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly noticeMessage = signal<string | null>(null);

  protected readonly restaurant = signal<PosRestaurant | null>(null);
  protected readonly order = signal<PosOrder | null>(null);

  /** For a new order: what it will be, from the floor's link. */
  protected readonly newType = signal<PosOrderType>('DineIn');
  protected readonly newTableId = signal<string | null>(null);
  protected readonly covers = signal(1);
  protected readonly customer = signal<{ id: string; name: string } | null>(null);
  protected readonly customerOpen = signal(false);
  protected readonly customerResults = signal<Contact[]>([]);
  private customerTimer: ReturnType<typeof setTimeout> | null = null;

  // ---- The grid ----
  protected readonly products = signal<PosProduct[]>([]);
  protected readonly categoryId = signal<string | null>(null);
  protected readonly search = signal('');
  protected readonly page = signal(1);
  protected readonly pageCount = signal(1);
  private searchTimer: ReturnType<typeof setTimeout> | null = null;

  // ---- Not yet sent ----
  protected readonly pending = signal<PendingItem[]>([]);
  private nextKey = 1;

  // ---- Inline actions ----
  protected readonly panel = signal<Panel | null>(null);
  protected readonly panelQuantity = signal(1);
  protected readonly panelReason = signal('');
  protected readonly panelTableId = signal<string | null>(null);
  protected readonly panelError = signal<string | null>(null);

  /** The control that opened the panel, where focus goes back when it closes (phase 40). */
  private panelOpener: HTMLElement | null = null;

  // ---- Printing ----
  protected readonly printed = signal<PrintedTicket[]>([]);
  /** Phase 65 -- the estimate bill being printed, and when; the print root shows it or the tickets. */
  protected readonly estimate = signal<PosOrderBillPreview | null>(null);
  protected readonly estimateAt = signal('');

  protected readonly isNew = computed(() => this.order() === null);
  protected readonly orderType = computed<PosOrderType>(() => this.order()?.orderType ?? this.newType());
  protected readonly isOpen = computed(() => this.order() === null || this.order()!.status === 'Open');

  /** Every table on the floor, for the heading and the move list. */
  private readonly tables = computed(() =>
    (this.restaurant()?.areas ?? []).flatMap((a) => a.tables.map((t) => ({ table: t, areaName: a.name }))));

  protected readonly seatedAt = computed(() => {
    const id = this.order()?.tableId ?? this.newTableId();
    return this.tables().find((x) => x.table.id === id) ?? null;
  });

  /** Tables an order could move to: free ones, on any area of this floor. */
  protected readonly freeTables = computed(() => this.tables().filter((x) => x.table.order === null));

  protected readonly heading = computed(() => {
    const seat = this.seatedAt();
    const where = seat ? ` · ${seat.table.name} (${seat.areaName})` : '';
    const order = this.order();
    return order
      ? `${order.code} · ${this.tabLabels[order.orderType]}${where}`
      : `New ${this.tabLabels[this.newType()]} order${where}`;
  });

  protected readonly pendingCount = computed(() => this.pending().reduce((sum, p) => sum + p.quantity, 0));

  /** A Delivery goes to a named customer, so it cannot be sent without one. */
  protected readonly needsCustomer = computed(() => this.isNew() && this.newType() === 'Delivery' && !this.customer());

  constructor() {
    this.route.paramMap.subscribe((params) => this.loadFor(params.get('orderId')!));
  }

  // ---- Loading ----

  private loadFor(orderId: string): void {
    // A new order's first send navigates here with its id; the order is already in hand.
    if (orderId !== 'new' && this.order()?.id === orderId) {
      return;
    }

    this.loading.set(true);
    this.errorMessage.set(null);

    if (orderId === 'new') {
      const query = this.route.snapshot.queryParamMap;
      const type = query.get('type') as PosOrderType | null;
      this.newType.set(type && POS_ORDER_TYPES.includes(type) ? type : 'DineIn');
      this.newTableId.set(query.get('tableId'));
      this.order.set(null);
      this.loadRestaurant(query.get('locationId') ?? '');
      return;
    }

    this.restaurantService.getOrder(this.organizationId, orderId).subscribe({
      next: (order) => {
        this.order.set(order);
        this.loadRestaurant(order.locationId);
      },
      error: (err: unknown) => this.fail(err, 'Could not load the order.'),
    });
  }

  private loadRestaurant(locationId: string): void {
    this.restaurantService.getRestaurant(this.organizationId, locationId).subscribe({
      next: (restaurant) => {
        this.restaurant.set(restaurant);
        const seat = this.seatedAt();
        if (this.isNew() && seat) {
          this.covers.set(Math.min(2, seat.table.capacity));
        }
        this.loading.set(false);
        this.loadProducts();
        afterNextRender(() => this.searchBox()?.nativeElement.focus(), { injector: this.injector });
      },
      error: (err: unknown) => this.fail(err, 'Could not load the restaurant.'),
    });
  }

  private loadProducts(): void {
    const restaurant = this.restaurant();
    if (!restaurant) return;

    this.restaurantService
      .listProducts(this.organizationId, restaurant.locationId, {
        search: this.search() || undefined,
        categoryId: this.categoryId() ?? undefined,
        page: this.page(),
        pageSize: PAGE_SIZE,
      })
      .subscribe({
        next: (result) => {
          this.products.set(result.items);
          this.pageCount.set(Math.max(1, Math.ceil(result.totalCount / result.pageSize)));
        },
        error: (err: unknown) => this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the products.'),
      });
  }

  private fail(err: unknown, fallback: string): void {
    this.loading.set(false);
    this.errorMessage.set(extractErrorMessage(err) ?? fallback);
  }

  // ---- The grid ----

  protected onSearchInput(event: Event): void {
    const term = (event.target as HTMLInputElement).value.trim();
    if (this.searchTimer) clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => {
      this.search.set(term);
      this.page.set(1);
      this.loadProducts();
    }, 250);
  }

  protected chooseCategory(categoryId: string | null): void {
    this.categoryId.set(categoryId);
    this.page.set(1);
    this.loadProducts();
  }

  protected goToPage(page: number): void {
    this.page.set(page);
    this.loadProducts();
  }

  // ---- Not yet sent ----

  protected addProduct(product: PosProduct): void {
    if (!this.isOpen()) return;
    this.noticeMessage.set(null);
    this.pending.update((items) => {
      const same = items.find((x) => x.kind === 'new' && x.productId === product.id && x.note === '');
      return same
        ? items.map((x) => (x === same ? { ...x, quantity: x.quantity + 1 } : x))
        : [...items, { key: this.nextKey++, kind: 'new', productId: product.id, productName: product.name, lineId: null, quantity: 1, note: '' }];
    });
  }

  /** One more of a line already sent: the same line, so it keeps its note and its kitchen. */
  protected addMore(line: PosOrderLine): void {
    if (!this.isOpen()) return;
    this.noticeMessage.set(null);
    this.pending.update((items) => {
      const same = items.find((x) => x.kind === 'more' && x.lineId === line.id);
      return same
        ? items.map((x) => (x === same ? { ...x, quantity: x.quantity + 1 } : x))
        : [...items, { key: this.nextKey++, kind: 'more', productId: line.productId, productName: line.productName, lineId: line.id, quantity: 1, note: line.note ?? '' }];
    });
  }

  protected changeQuantity(key: number, delta: number): void {
    this.pending.update((items) =>
      items.map((x) => (x.key === key ? { ...x, quantity: x.quantity + delta } : x)).filter((x) => x.quantity > 0));
  }

  protected setNote(key: number, event: Event): void {
    const note = (event.target as HTMLInputElement).value;
    this.pending.update((items) => items.map((x) => (x.key === key ? { ...x, note } : x)));
  }

  protected removePending(key: number): void {
    this.pending.update((items) => items.filter((x) => x.key !== key));
  }

  protected onCoversInput(event: Event): void {
    this.covers.set(Number((event.target as HTMLInputElement).value) || 0);
  }

  // ---- Customer (Take Away and Delivery) ----

  protected onCustomerInput(event: Event): void {
    const term = (event.target as HTMLInputElement).value.trim();
    if (this.customerTimer) clearTimeout(this.customerTimer);
    if (!term) {
      this.customerResults.set([]);
      return;
    }

    this.customerTimer = setTimeout(() => {
      this.contactsService.listContacts(this.organizationId, 'Customer', 1, 8, { search: term }).subscribe({
        next: (result) =>
          this.customerResults.set(result.items.filter((x) => x.id !== this.restaurant()?.walkInCustomer?.id)),
        error: (err: unknown) => this.errorMessage.set(extractErrorMessage(err) ?? 'Could not search customers.'),
      });
    }, 250);
  }

  protected chooseCustomer(contact: Contact | null): void {
    this.customer.set(contact ? { id: contact.id, name: contact.name } : null);
    this.customerOpen.set(false);
    this.customerResults.set([]);
  }

  // ---- Send ----

  protected send(): void {
    const restaurant = this.restaurant();
    const items = this.pending();
    if (!restaurant || items.length === 0 || this.busy() || this.needsCustomer()) return;

    this.busy.set(true);
    this.errorMessage.set(null);

    const newItems = items
      .filter((x) => x.kind === 'new')
      .map((x) => ({ productId: x.productId, quantity: x.quantity, unitId: null, note: x.note.trim() || null }));
    const moreOf = items.filter((x) => x.kind === 'more').map((x) => ({ lineId: x.lineId!, quantity: x.quantity }));

    const order = this.order();
    const request$ = order
      ? this.restaurantService.addItems(this.organizationId, order.id, newItems, moreOf)
      : this.restaurantService.createOrder(this.organizationId, {
          locationId: restaurant.locationId,
          orderType: this.newType(),
          tableId: this.newType() === 'DineIn' ? this.newTableId() : null,
          covers: this.newType() === 'DineIn' ? this.covers() : 0,
          contactId: this.customer()?.id ?? null,
          items: newItems,
        });

    request$.subscribe({
      next: (saved) => {
        const sendNumber = Math.max(...saved.tickets.map((t) => t.sendNumber));
        const sent = saved.tickets.filter((t) => t.sendNumber === sendNumber);
        this.order.set(saved);
        this.pending.set([]);
        this.busy.set(false);
        this.noticeMessage.set(
          `Sent to the kitchen: ${sent.map((t) => t.kitchenStationName).join(', ')} (ticket ${saved.code}-${sendNumber}).`);
        if (!order) {
          void this.router.navigate(['/organizations', this.organizationId, 'pos', 'orders', saved.id], { replaceUrl: true });
        }
        if (restaurant.printKot) {
          this.printTickets(sent);
        }
      },
      error: (err: unknown) => {
        this.busy.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not send the order.');
      },
    });
  }

  /** Records each printing on the server, then prints them all, one page per station. */
  protected printTickets(tickets: KitchenTicket[]): void {
    const order = this.order();
    if (!order || tickets.length === 0) return;

    from(tickets)
      .pipe(
        concatMap((t) => this.restaurantService.printTicket(this.organizationId, order.id, t.id)),
        toArray(),
      )
      .subscribe({
        next: (results) => {
          const latest = results[results.length - 1].order;
          this.order.set(latest);
          this.estimate.set(null);
          this.printed.set(results.flatMap((r) => {
            const ticket = latest.tickets.find((t) => t.id === r.ticketId);
            return ticket ? [{ ticket, printNumber: r.printNumber }] : [];
          }));
          afterNextRender(() => window.print(), { injector: this.injector });
        },
        error: (err: unknown) => this.errorMessage.set(extractErrorMessage(err) ?? 'Could not print the kitchen ticket.'),
      });
  }

  /**
   * Phase 65 -- prints the estimate bill: everything still to be billed, priced by the server's bill
   * planner. Not a tax invoice and not a print of one, so nothing is counted.
   */
  protected printEstimate(): void {
    const order = this.order();
    if (!order) return;

    this.restaurantService.previewBill(this.organizationId, order.id, { split: 'Whole', items: [], parts: null }).subscribe({
      next: (preview) => {
        this.printed.set([]);
        this.estimateAt.set(new Date().toISOString());
        this.estimate.set(preview);
        afterNextRender(() => window.print(), { injector: this.injector });
      },
      error: (err: unknown) => this.errorMessage.set(extractErrorMessage(err) ?? 'Could not price the estimate.'),
    });
  }

  // ---- Inline actions ----

  protected openPanel(panel: Panel, line?: PosOrderLine): void {
    this.panelOpener = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    this.panel.set(panel);
    this.panelError.set(null);
    this.panelReason.set('');
    this.panelQuantity.set(panel.kind === 'serve' ? (line?.outstanding ?? 1) : 1);
    this.panelTableId.set(this.order()?.tableId ?? null);
    if (panel.kind === 'details') {
      this.covers.set(this.order()?.covers ?? 1);
    }
    afterNextRender(() => document.getElementById('pos-order-panel-first')?.focus(), { injector: this.injector });
  }

  protected closePanel(): void {
    this.panel.set(null);
    this.restoreFocus();
  }

  /** Back to the button that opened the panel, or to the order's heading when that button is gone
   * (a voided order has no Void button to return to). */
  private restoreFocus(): void {
    const opener = this.panelOpener;
    this.panelOpener = null;
    afterNextRender(() => {
      const target = opener?.isConnected ? opener : document.getElementById('pos-order-heading');
      target?.focus();
    }, { injector: this.injector });
  }

  protected isPanel(kind: Panel['kind'], lineId?: string): boolean {
    const panel = this.panel();
    return !!panel && panel.kind === kind && (lineId === undefined || ('lineId' in panel && panel.lineId === lineId));
  }

  protected onPanelQuantity(event: Event): void {
    this.panelQuantity.set(Number((event.target as HTMLInputElement).value) || 0);
  }

  protected onPanelReason(event: Event): void {
    this.panelReason.set((event.target as HTMLInputElement).value);
  }

  protected onPanelTable(event: Event): void {
    this.panelTableId.set((event.target as HTMLSelectElement).value || null);
  }

  protected confirmPanel(): void {
    const panel = this.panel();
    const order = this.order();
    if (!panel || !order || this.busy()) return;

    if ((panel.kind === 'discard' || panel.kind === 'void') && !this.panelReason().trim()) {
      this.panelError.set('A discard needs a reason.');
      document.getElementById(panel.kind === 'discard' ? 'pos-order-discard-reason' : 'pos-order-panel-first')?.focus();
      return;
    }

    const request$ =
      panel.kind === 'serve'
        ? this.restaurantService.serve(this.organizationId, order.id, [{ lineId: panel.lineId, quantity: this.panelQuantity() }])
        : panel.kind === 'discard'
          ? this.restaurantService.discard(
              this.organizationId, order.id, [{ lineId: panel.lineId, quantity: this.panelQuantity() }], this.panelReason().trim())
          : panel.kind === 'void'
            ? this.restaurantService.voidOrder(this.organizationId, order.id, this.panelReason().trim())
            : this.restaurantService.updateOrder(this.organizationId, order.id, {
                tableId: order.orderType === 'DineIn' ? this.panelTableId() : null,
                covers: this.covers(),
                contactId: order.contactId,
              });

    this.busy.set(true);
    request$.subscribe({
      next: (saved) => {
        const before = order.tickets.length;
        this.order.set(saved);
        this.busy.set(false);
        this.panel.set(null);
        this.restoreFocus();
        this.noticeMessage.set(this.panelNotice(panel.kind, saved));
        if (panel.kind === 'details') {
          // The move freed one table and took another; the floor's own read says which.
          this.loadRestaurant(saved.locationId);
        }
        const cancellations = saved.tickets.slice(before);
        if (cancellations.length > 0 && this.restaurant()?.printKot) {
          this.printTickets(cancellations);
        }
      },
      error: (err: unknown) => {
        this.busy.set(false);
        this.panelError.set(extractErrorMessage(err) ?? 'Could not save.');
      },
    });
  }

  private panelNotice(kind: Panel['kind'], saved: PosOrder): string {
    switch (kind) {
      case 'serve':
        return 'Marked as served.';
      case 'discard':
        return 'Discarded; the kitchen has a cancellation ticket.';
      case 'void':
        return `Order ${saved.code} is voided and its table is free.`;
      default:
        return 'Order updated.';
    }
  }

  protected lineSummary(line: PosOrderLine): string {
    const parts = [`${line.quantity} on the order`, `${line.served} served`];
    if (line.discarded > 0) parts.push(`${line.discarded} discarded`);
    if (line.invoiced > 0) parts.push(`${line.invoiced} billed`);
    return parts.join(' · ');
  }

  protected tableOption(entry: { table: PosRestaurantTable; areaName: string }): string {
    return `${entry.table.name} (${entry.areaName}, seats ${entry.table.capacity})`;
  }
}
