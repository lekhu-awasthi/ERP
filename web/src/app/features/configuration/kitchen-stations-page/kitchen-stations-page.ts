import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { Product } from '../../../core/catalog/catalog.models';
import { CatalogService } from '../../../core/catalog/catalog.service';
import { DEFAULT_KITCHEN_STATION, KitchenStation } from '../../../core/pos/pos-restaurant.models';
import { PosRestaurantService } from '../../../core/pos/pos-restaurant.service';
import { StatusBanner } from '../../../shared/a11y/status-banner';

/**
 * Phase 64 -- Configurations > Kitchen Stations: the places a restaurant's kitchen tickets go (the
 * vendor's Print Profiles, a name and nothing else) and which products each one cooks. A product on no
 * station goes to Default.
 *
 * <p><b>Products are chosen from the station</b>, not on the product form (docs/phase-64-status.md
 * Decision G): one list per station, saved whole. The list comes from the catalogue's shared
 * <code>listAllProducts</code> seam, so a variant parent, which is never ordered, never appears.</p>
 */
@Component({
  selector: 'app-kitchen-stations-page',
  imports: [RouterLink, StatusBanner],
  templateUrl: './kitchen-stations-page.html',
})
export class KitchenStationsPage {
  private readonly route = inject(ActivatedRoute);
  private readonly restaurantService = inject(PosRestaurantService);
  private readonly catalogService = inject(CatalogService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;
  protected readonly defaultName = DEFAULT_KITCHEN_STATION;

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly noticeMessage = signal<string | null>(null);
  protected readonly stations = signal<KitchenStation[]>([]);
  protected readonly products = signal<Product[]>([]);

  protected readonly newName = signal('');

  /** The station whose name/active form is open. */
  protected readonly editingId = signal<string | null>(null);
  protected readonly editName = signal('');
  protected readonly editActive = signal(true);

  /** The station whose product list is open, with what is ticked. */
  protected readonly choosingId = signal<string | null>(null);
  protected readonly chosen = signal<ReadonlySet<string>>(new Set());
  protected readonly productSearch = signal('');

  /** Where each product goes today, by product id. */
  private readonly stationOf = computed(() => {
    const map = new Map<string, string>();
    for (const station of this.stations()) {
      for (const product of station.products) map.set(product.id, station.name);
    }
    return map;
  });

  protected readonly onDefault = computed(() => {
    const assigned = this.stationOf();
    return this.products().filter((p) => p.availableForSale && !assigned.has(p.id)).length;
  });

  protected readonly matchingProducts = computed(() => {
    const term = this.productSearch().trim().toLowerCase();
    return this.products()
      .filter((p) => p.availableForSale && p.isActive)
      .filter((p) => !term || p.name.toLowerCase().includes(term) || p.code.toLowerCase().includes(term));
  });

  constructor() {
    this.restaurantService.listKitchenStations(this.organizationId).subscribe({
      next: (stations) => {
        this.stations.set(stations);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the kitchen stations.');
      },
    });
    this.catalogService.listAllProducts(this.organizationId).subscribe({
      next: (products) => this.products.set(products),
      error: (err: unknown) => this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the products.'),
    });
  }

  protected onNewName(event: Event): void {
    this.newName.set((event.target as HTMLInputElement).value);
  }

  protected add(): void {
    const name = this.newName().trim();
    if (!name) {
      this.errorMessage.set('A kitchen station needs a name.');
      return;
    }

    this.restaurantService.createKitchenStation(this.organizationId, name).subscribe({
      next: (stations) => this.done(stations, `${name} added.`, () => this.newName.set('')),
      error: (err: unknown) => this.errorMessage.set(extractErrorMessage(err) ?? 'Could not add the station.'),
    });
  }

  protected edit(station: KitchenStation): void {
    this.choosingId.set(null);
    this.editingId.set(station.id);
    this.editName.set(station.name);
    this.editActive.set(station.isActive);
  }

  protected onEditName(event: Event): void {
    this.editName.set((event.target as HTMLInputElement).value);
  }

  protected onEditActive(event: Event): void {
    this.editActive.set((event.target as HTMLInputElement).checked);
  }

  protected saveEdit(station: KitchenStation): void {
    this.restaurantService
      .updateKitchenStation(this.organizationId, station.id, this.editName().trim(), this.editActive())
      .subscribe({
        next: (stations) => this.done(stations, 'Station saved.', () => this.editingId.set(null)),
        error: (err: unknown) => this.errorMessage.set(extractErrorMessage(err) ?? 'Could not save the station.'),
      });
  }

  protected choose(station: KitchenStation): void {
    this.editingId.set(null);
    this.choosingId.set(station.id);
    this.chosen.set(new Set(station.products.map((p) => p.id)));
    this.productSearch.set('');
  }

  protected onProductSearch(event: Event): void {
    this.productSearch.set((event.target as HTMLInputElement).value);
  }

  protected toggle(productId: string, event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    const next = new Set(this.chosen());
    if (checked) next.add(productId);
    else next.delete(productId);
    this.chosen.set(next);
  }

  /** Where a product goes now, said beside its checkbox when it is not this station. */
  protected currentlyAt(productId: string, stationName: string): string | null {
    const at = this.stationOf().get(productId);
    return at && at !== stationName ? at : null;
  }

  protected saveProducts(station: KitchenStation): void {
    this.restaurantService
      .setKitchenStationProducts(this.organizationId, station.id, [...this.chosen()])
      .subscribe({
        next: (stations) => this.done(stations, `${station.name}'s products saved.`, () => this.choosingId.set(null)),
        error: (err: unknown) => this.errorMessage.set(extractErrorMessage(err) ?? 'Could not save the products.'),
      });
  }

  private done(stations: KitchenStation[], notice: string, then: () => void): void {
    this.stations.set(stations);
    this.errorMessage.set(null);
    this.noticeMessage.set(notice);
    then();
  }
}
