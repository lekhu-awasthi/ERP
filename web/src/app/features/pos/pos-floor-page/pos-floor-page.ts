import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { POS_TAB_LABELS, PosTab } from '../../../core/pos/pos.models';
import {
  POS_ORDER_TYPES,
  PosOrderType,
  PosRestaurant,
  PosRestaurantTable,
} from '../../../core/pos/pos-restaurant.models';
import { PosRestaurantService } from '../../../core/pos/pos-restaurant.service';
import { StatusBanner } from '../../../shared/a11y/status-banner';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';
import { NepaliDatePipe } from '../../../shared/formatting/nepali-date-pipe';

/**
 * Phase 64 -- the restaurant till's floor: the Dine In tab draws each area's tables where the floor
 * plan put them, a taken table carrying its order; Take Away and Delivery list their open orders.
 *
 * <p><b>Occupancy is the server's</b>: a table is taken exactly when an open order names it, read on
 * load and on Refresh. A live board is phase 65's; until then the vendor's own KOT board has a refresh
 * button too.</p>
 *
 * <p><b>Every table is a link</b>, positioned on a canvas scaled to the screen: a free table opens a
 * new order for it, a taken one opens its order. Its accessible name says which (phase 40: a click
 * target is a control, never a bare <code>div</code>). Below 576px the canvas becomes a grid in floor
 * order, so 320px still reflows (WCAG 1.4.10).</p>
 */
@Component({
  selector: 'app-pos-floor-page',
  imports: [RouterLink, StatusBanner, AmountPipe, NepaliDatePipe],
  templateUrl: './pos-floor-page.html',
  styleUrl: './pos-floor-page.scss',
})
export class PosFloorPage {
  private readonly route = inject(ActivatedRoute);
  private readonly restaurantService = inject(PosRestaurantService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;
  protected readonly locationId = this.route.snapshot.paramMap.get('locationId')!;
  protected readonly tabLabels = POS_TAB_LABELS;

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly restaurant = signal<PosRestaurant | null>(null);
  protected readonly tab = signal<PosOrderType>('DineIn');
  protected readonly areaId = signal<string | null>(null);

  protected readonly tabs = computed<PosOrderType[]>(() =>
    (this.restaurant()?.availableTabs ?? []).filter((t): t is PosOrderType => POS_ORDER_TYPES.includes(t as PosOrderType)));

  protected readonly area = computed(() => {
    const r = this.restaurant();
    return r?.areas.find((a) => a.id === this.areaId()) ?? r?.areas[0] ?? null;
  });

  /** The open orders of the chosen tab (Take Away or Delivery), oldest first: the queue the kitchen works. */
  protected readonly ordersOfTab = computed(() =>
    (this.restaurant()?.orders ?? []).filter((o) => o.orderType === this.tab()));

  constructor() {
    this.load(true);
  }

  protected refresh(): void {
    this.load(false);
  }

  protected chooseTab(tab: PosTab): void {
    this.tab.set(tab as PosOrderType);
  }

  protected chooseArea(areaId: string): void {
    this.areaId.set(areaId);
  }

  /** The table's position and size as fractions of the canvas, for the stylesheet's custom properties. */
  protected placement(table: PosRestaurantTable): Record<string, string> {
    const r = this.restaurant()!;
    return {
      '--x': `${(table.x / r.canvasWidth) * 100}%`,
      '--y': `${(table.y / r.canvasHeight) * 100}%`,
      '--w': `${(table.width / r.canvasWidth) * 100}%`,
      '--h': `${(table.height / r.canvasHeight) * 100}%`,
    };
  }

  protected newOrderQuery(tableId: string | null): Record<string, string> {
    const query: Record<string, string> = { locationId: this.locationId, type: this.tab() };
    if (tableId) query['tableId'] = tableId;
    return query;
  }

  private load(first: boolean): void {
    this.loading.set(first);
    this.restaurantService.getRestaurant(this.organizationId, this.locationId).subscribe({
      next: (restaurant) => {
        this.restaurant.set(restaurant);
        this.errorMessage.set(null);
        if (first) {
          const tab = restaurant.defaultTab;
          this.tab.set(tab && POS_ORDER_TYPES.includes(tab as PosOrderType) ? (tab as PosOrderType) : 'DineIn');
        }
        if (!restaurant.areas.some((a) => a.id === this.areaId())) {
          this.areaId.set(restaurant.areas[0]?.id ?? null);
        }
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the floor.');
      },
    });
  }
}
