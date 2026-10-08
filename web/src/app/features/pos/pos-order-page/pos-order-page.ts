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
  | { kind: 'takeAway'; lineId: string }
  | { kind: 'details' }
  | { kind: 'transfer' }
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
 * <p><b>Phase 68 -- Mark as Take Away and Transfer Items.</b> A dine-in line's unserved, unbilled food can
 * be parcelled: it moves to a take-away line whose service charge the location's setting decides, once,
 * when it is marked. <i>Transfer Items</i> moves food to another table's open order, or opens one there,
 * at the same rates. Both reach the kitchen as tickets of their own.</p>
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

  /** Phase 68 -- the transfer panel: the quantity chosen per line, and which lines are ticked. */
  protected readonly transferQuantities = signal<Record<string, number>>({});
  protected readonly transferPicked = signal<Record<string, boolean>>({});

  /** The control that opened the panel, where focus goes back when it closes (phase 40). */
  private panelOpener: HTMLElement | null = null;

  // ---- Printing ----
  protected readonly printed = signal<PrintedTicket[]>([]);
  /** Phase 65 -- the estimate bill being printed, and when; the print root shows it or the tickets. */
  protected readonly estimate = signal<PosOrderBillPreview | null>(null);
  protected readonly estimateAt = signal('');
  /** Phase 68 -- a transfer's ticket belongs to the other order, so the print root shows that one. */
  protected readonly printOrder = signal<PosOrder | null>(null);

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

  /** Phase 68 -- tables items can be transferred to: every other table, free or with an open order. */
  protected readonly transferTables = computed(() => {
    const here = this.order()?.tableId;
    return this.tables().filter((x) => x.table.id !== here);
  });

  /** Phase 68 -- the lines with something still to bill, which is what a transfer can move. */
  protected readonly transferableLines = computed(() => (this.order()?.lines ?? []).filter((l) => l.toBill > 0));

  protected readonly transferCount = computed(() =>
    this.transferableLines().filter((l) => this.transferPicked()[l.id]).length);

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

  /** <code>focusSearch</code> is for opening the page; a reload after an action leaves focus where the
   * action put it (back on the control that opened the panel, phase 40). */
  private loadRestaurant(locationId: string, focusSearch = true): void {
    this.restaurantService.getRestaurant(this.organizationId, locationId).subscribe({
      next: (restaurant) => {
        this.restaurant.set(restaurant);
        const seat = this.seatedAt();
        if (this.isNew() && seat) {
          this.covers.set(Math.min(2, seat.table.capacity));
        }
        this.loading.set(false);
        this.loadProducts();
        if (focusSearch) {
          afterNextRender(() => this.searchBox()?.nativeElement.focus(), { injector: this.injector });
        }
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
  protected printTickets(tickets: KitchenTicket[], forOrder?: PosOrder): void {
    const order = forOrder ?? this.order();
    if (!order || tickets.length === 0) return;

    from(tickets)
      .pipe(
        concatMap((t) => this.restaurantService.printTicket(this.organizationId, order.id, t.id)),
        toArray(),
      )
      .subscribe({
        next: (results) => {
          const latest = results[results.length - 1].order;
          if (forOrder && forOrder.id !== this.order()?.id) {
            this.printOrder.set(latest);
          } else {
            this.printOrder.set(null);
            this.order.set(latest);
          }
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
    // Serve and take-away default to the whole of what they can act on, as the vendor's dialogs do.
    this.panelQuantity.set(
      panel.kind === 'serve' ? (line?.outstanding ?? 1) : panel.kind === 'takeAway' && line ? this.markable(line) : 1);
    this.panelTableId.set(panel.kind === 'transfer' ? null : (this.order()?.tableId ?? null));
    if (panel.kind === 'transfer') {
      this.transferPicked.set({});
      this.transferQuantities.set(Object.fromEntries(this.transferableLines().map((l) => [l.id, l.toBill])));
    }
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

    if (panel.kind === 'transfer') {
      this.confirmTransfer(order);
      return;
    }

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
          : panel.kind === 'takeAway'
            ? this.restaurantService.markTakeAway(this.organizationId, order.id, panel.lineId, this.panelQuantity())
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
        this.order.set(saved);
        this.busy.set(false);
        this.panel.set(null);
        this.restoreFocus();
        this.noticeMessage.set(this.panelNotice(panel.kind, saved));
        if (panel.kind === 'details') {
          // The move freed one table and took another; the floor's own read says which.
          this.loadRestaurant(saved.locationId, false);
        }
        // A cancellation or a take-away ticket goes to the kitchen that was cooking it.
        const newTickets = saved.tickets.filter((t) => !order.tickets.some((o) => o.id === t.id));
        if (newTickets.length > 0 && this.restaurant()?.printKot) {
          this.printTickets(newTickets);
        }
      },
      error: (err: unknown) => {
        this.busy.set(false);
        this.panelError.set(extractErrorMessage(err) ?? 'Could not save.');
      },
    });
  }

  /**
   * Phase 68 -- moves the ticked lines to the chosen table. Its own request, because the answer is both
   * orders rather than one (phase 4: never share a request variable across result types). When the
   * transfer emptied this order it is voided, so the till follows the food to the other table.
   */
  private confirmTransfer(order: PosOrder): void {
    const tableId = this.panelTableId();
    const items = this.transferableLines()
      .filter((l) => this.transferPicked()[l.id])
      .map((l) => ({ lineId: l.id, quantity: this.transferQuantities()[l.id] ?? 0 }));

    if (!tableId) {
      this.panelError.set('Choose the table to transfer to.');
      document.getElementById('pos-order-panel-first')?.focus();
      return;
    }

    if (items.length === 0) {
      this.panelError.set('Tick at least one item to transfer.');
      document.getElementById(`pos-order-transfer-pick-${this.transferableLines()[0]?.id}`)?.focus();
      return;
    }

    this.busy.set(true);
    this.restaurantService.transferItems(this.organizationId, order.id, tableId, items).subscribe({
      next: (result) => {
        this.busy.set(false);
        this.panel.set(null);
        this.restoreFocus();
        const where = this.tables().find((x) => x.table.id === tableId)?.table.name ?? 'the other table';
        this.loadRestaurant(result.source.locationId, false);

        if (result.source.status === 'Voided') {
          this.noticeMessage.set(`Everything moved to ${where} (${result.target.code}); ${order.code} is closed.`);
          this.order.set(result.target);
          void this.router.navigate(['/organizations', this.organizationId, 'pos', 'orders', result.target.id], { replaceUrl: true });
        } else {
          this.order.set(result.source);
          this.noticeMessage.set(
            `Moved ${items.length} item(s) to ${where} (${result.target.code}${result.targetCreated ? ', a new order' : ''}).`);
        }

        // The kitchen is told where the food now goes: the incoming ticket, on the other order.
        const incoming = result.target.tickets.filter((t) => t.kind === 'Transfer' && t.counterpartOrderCode === order.code);
        const latest = incoming.length > 0 ? Math.max(...incoming.map((t) => t.sendNumber)) : 0;
        if (latest > 0 && this.restaurant()?.printKot) {
          this.printTickets(incoming.filter((t) => t.sendNumber === latest), result.target);
        }
      },
      error: (err: unknown) => {
        this.busy.set(false);
        this.panelError.set(extractErrorMessage(err) ?? 'Could not transfer the items.');
      },
    });
  }

  protected onTransferPick(lineId: string, event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    this.transferPicked.update((picked) => ({ ...picked, [lineId]: checked }));
  }

  protected onTransferQuantity(lineId: string, event: Event): void {
    const value = Number((event.target as HTMLInputElement).value) || 0;
    this.transferQuantities.update((q) => ({ ...q, [lineId]: value }));
  }

  /** Phase 68 -- what of a dine-in line can be parcelled: not yet served and not yet billed. */
  protected markable(line: PosOrderLine): number {
    return Math.max(0, Math.min(line.outstanding, line.toBill));
  }

  /** Phase 68 -- what marking this line will do to its service charge, said before it is done. */
  protected takeAwayChargeNote(line: PosOrderLine): string {
    if (line.serviceChargeRate === 0) {
      return 'This item carries no service charge, so the bill is unchanged.';
    }
    return this.restaurant()?.serviceChargeOnTakeAway
      ? `The parcel keeps its ${line.serviceChargeRate}% service charge (this location's setting).`
      : 'The parcel carries no service charge (this location\'s setting), so the bill comes down.';
  }

  private panelNotice(kind: Panel['kind'], saved: PosOrder): string {
    switch (kind) {
      case 'takeAway':
        return 'Marked as take away; the kitchen has a ticket to pack it.';
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
    if (line.movedOut > 0) parts.push(`${line.movedOut} moved off`);
    if (line.movedIn > 0) parts.push(`${line.movedIn} moved in`);
    if (line.invoiced > 0) parts.push(`${line.invoiced} billed`);
    return parts.join(' · ');
  }

  protected tableOption(entry: { table: PosRestaurantTable; areaName: string }): string {
    return `${entry.table.name} (${entry.areaName}, seats ${entry.table.capacity})`;
  }

  /** Phase 68 -- a transfer target says whether its items join an open order or start one. */
  protected transferOption(entry: { table: PosRestaurantTable; areaName: string }): string {
    const order = entry.table.order;
    return `${entry.table.name} (${entry.areaName}) · ${order ? `joins ${order.code}` : 'free: opens a new order'}`;
  }

  /** Phase 68 -- what a kitchen ticket is, in words, for the order's ticket list. */
  protected ticketKindLabel(ticket: KitchenTicket): string | null {
    switch (ticket.kind) {
      case 'Cancellation':
        return 'Cancellation';
      case 'TakeAway':
        return 'Take Away';
      case 'Transfer':
        return ticket.lines.every((l) => l.quantity < 0)
          ? `Moved to ${ticket.counterpartOrderCode ?? 'another order'}`
          : `From ${ticket.counterpartOrderCode ?? 'another order'}`;
      default:
        return null;
    }
  }
}
