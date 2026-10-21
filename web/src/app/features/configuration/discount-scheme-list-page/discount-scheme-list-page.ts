import { Component, ElementRef, computed, inject, signal, viewChild } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { Product, ProductCategory } from '../../../core/catalog/catalog.models';
import { CatalogService } from '../../../core/catalog/catalog.service';
import { BillingLocation } from '../../../core/organizations/organizations.models';
import { OrganizationsService } from '../../../core/organizations/organizations.service';
import {
  DISCOUNT_SCHEME_KINDS,
  DiscountScheme,
  DiscountSchemeKind,
  DiscountSchemeRequest,
} from '../../../core/sales/sales.models';
import { SalesService } from '../../../core/sales/sales.service';
import { StatusBanner } from '../../../shared/a11y/status-banner';
import { BsDateInput } from '../../../shared/formatting/bs-date-input';
import { NepaliDatePipe } from '../../../shared/formatting/nepali-date-pipe';
import { nepalToday, shiftDay } from '../../../shared/formatting/nepal-time';
import { LookupFilter, matchesLookup } from '../../../shared/pagination/lookup-filter';

/** One editable row: the field its kind names, and the percent. */
interface SchemeRow {
  key: number;
  productId: string;
  categoryId: string;
  minimumAmount: number | null;
  discountPct: number | null;
}

let nextRowKey = 1;

/**
 * Phase 97 -- Configurations > Discount Schemes: dated percent-off rules a sale picks up by itself, on the ERP's
 * Quotation, Sales Order and Invoice and at the till. Item wise names products, category wise names categories (and
 * covers their sub-categories), and a slab gives a bill of at least an amount a percent off. A product in two schemes
 * gets the larger. The reference product's screen with these three kinds is a mock-up that saves nothing, so its rules
 * are the user's (2026-10-21); see `DiscountScheme` on the server.
 */
@Component({
  selector: 'app-discount-scheme-list-page',
  imports: [RouterLink, StatusBanner, BsDateInput, NepaliDatePipe, LookupFilter],
  templateUrl: './discount-scheme-list-page.html',
})
export class DiscountSchemeListPage {
  private readonly route = inject(ActivatedRoute);
  private readonly salesService = inject(SalesService);
  private readonly catalogService = inject(CatalogService);
  private readonly organizationsService = inject(OrganizationsService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;
  protected readonly kinds = DISCOUNT_SCHEME_KINDS;

  private readonly formHeading = viewChild<ElementRef<HTMLElement>>('formHeading');
  private readonly listHeading = viewChild<ElementRef<HTMLElement>>('listHeading');

  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly items = signal<DiscountScheme[]>([]);
  protected readonly term = signal('');
  protected readonly filtered = computed(() =>
    this.items().filter((item) => matchesLookup(this.term(), `${item.code} ${item.name}`)));
  protected readonly confirmingDeleteId = signal<string | null>(null);

  protected readonly products = signal<Product[]>([]);
  protected readonly categories = signal<ProductCategory[]>([]);
  protected readonly locations = signal<BillingLocation[]>([]);
  protected readonly productNames = computed(() => new Map(this.products().map((p) => [p.id, `${p.code} — ${p.name}`] as const)));
  protected readonly categoryNames = computed(() => new Map(this.categories().map((c) => [c.id, c.name] as const)));
  protected readonly locationNames = computed(() => new Map(this.locations().map((l) => [l.id, l.name] as const)));

  // ---- the form ----
  protected readonly editingId = signal<string | null>(null);
  protected readonly code = signal('');
  protected readonly name = signal('');
  protected readonly kind = signal<DiscountSchemeKind>('ItemWise');
  protected readonly startDate = signal(nepalToday());
  protected readonly endDate = signal(shiftDay(nepalToday(), 30));
  protected readonly allLocations = signal(true);
  protected readonly locationIds = signal<ReadonlySet<string>>(new Set());
  protected readonly isActive = signal(true);
  protected readonly rows = signal<SchemeRow[]>([this.newRow()]);

  constructor() {
    this.load();
    this.catalogService.listAllProducts(this.organizationId, undefined, 'All').subscribe({
      next: (products) => this.products.set([...products].sort((a, b) => a.code.localeCompare(b.code))),
    });
    this.catalogService.listProductCategories(this.organizationId).subscribe({
      next: (categories) => this.categories.set([...categories].sort((a, b) => a.name.localeCompare(b.name))),
    });
    this.organizationsService.listBillingLocations(this.organizationId).subscribe({
      next: (locations) => this.locations.set(locations),
    });
  }

  protected kindLabel(kind: DiscountSchemeKind): string {
    return this.kinds.find((k) => k.value === kind)?.label ?? kind;
  }

  /** What a scheme's rows say, in a line: the names it covers, or its slabs. */
  protected describe(scheme: DiscountScheme): string {
    return scheme.lines
      .map((line) =>
        scheme.kind === 'ItemWise'
          ? `${this.productNames().get(line.productId ?? '') ?? 'A product'} ${line.discountPct}%`
          : scheme.kind === 'CategoryWise'
            ? `${this.categoryNames().get(line.categoryId ?? '') ?? 'A category'} ${line.discountPct}%`
            : `from Rs ${line.minimumAmount} ${line.discountPct}%`)
      .join(', ');
  }

  protected where(scheme: DiscountScheme): string {
    return scheme.allLocations
      ? 'All locations'
      : scheme.locationIds.map((id) => this.locationNames().get(id) ?? 'A location').join(', ');
  }

  // ---- form events ----

  protected onCode(event: Event): void {
    this.code.set((event.target as HTMLInputElement).value);
  }

  protected onName(event: Event): void {
    this.name.set((event.target as HTMLInputElement).value);
  }

  /** A change of kind starts the rows again: a product row means nothing to a slab scheme. */
  protected onKind(event: Event): void {
    this.kind.set((event.target as HTMLSelectElement).value as DiscountSchemeKind);
    this.rows.set([this.newRow()]);
  }

  protected onAllLocations(event: Event): void {
    this.allLocations.set((event.target as HTMLInputElement).checked);
  }

  protected onLocation(id: string, event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    this.locationIds.update((ids) => {
      const next = new Set(ids);
      if (checked) next.add(id);
      else next.delete(id);
      return next;
    });
  }

  protected onActive(event: Event): void {
    this.isActive.set((event.target as HTMLInputElement).checked);
  }

  protected onRowProduct(key: number, event: Event): void {
    this.updateRow(key, { productId: (event.target as HTMLSelectElement).value });
  }

  protected onRowCategory(key: number, event: Event): void {
    this.updateRow(key, { categoryId: (event.target as HTMLSelectElement).value });
  }

  protected onRowMinimum(key: number, event: Event): void {
    const value = (event.target as HTMLInputElement).valueAsNumber;
    this.updateRow(key, { minimumAmount: Number.isFinite(value) ? value : null });
  }

  protected onRowPct(key: number, event: Event): void {
    const value = (event.target as HTMLInputElement).valueAsNumber;
    this.updateRow(key, { discountPct: Number.isFinite(value) ? value : null });
  }

  protected addRow(): void {
    this.rows.update((rows) => [...rows, this.newRow()]);
  }

  protected removeRow(key: number): void {
    this.rows.update((rows) => (rows.length > 1 ? rows.filter((r) => r.key !== key) : rows));
  }

  // ---- create, edit, delete ----

  protected startCreate(): void {
    this.editingId.set(null);
    this.code.set('');
    this.name.set('');
    this.kind.set('ItemWise');
    this.startDate.set(nepalToday());
    this.endDate.set(shiftDay(nepalToday(), 30));
    this.allLocations.set(true);
    this.locationIds.set(new Set());
    this.isActive.set(true);
    this.rows.set([this.newRow()]);
  }

  /** The form is above the list, so it takes focus when it opens for an edit (phase 73's rule). */
  protected startEdit(scheme: DiscountScheme): void {
    this.editingId.set(scheme.id);
    this.code.set(scheme.code);
    this.name.set(scheme.name);
    this.kind.set(scheme.kind);
    this.startDate.set(scheme.startDate);
    this.endDate.set(scheme.endDate);
    this.allLocations.set(scheme.allLocations);
    this.locationIds.set(new Set(scheme.locationIds));
    this.isActive.set(scheme.isActive);
    this.rows.set(scheme.lines.map((line) => ({
      key: nextRowKey++,
      productId: line.productId ?? '',
      categoryId: line.categoryId ?? '',
      minimumAmount: line.minimumAmount,
      discountPct: line.discountPct,
    })));
    this.errorMessage.set(null);
    this.formHeading()?.nativeElement.focus();
  }

  protected cancelEdit(): void {
    this.startCreate();
    this.formHeading()?.nativeElement.focus();
  }

  protected save(): void {
    this.saving.set(true);
    this.errorMessage.set(null);

    const kind = this.kind();
    const request: DiscountSchemeRequest = {
      code: this.code().trim(),
      name: this.name().trim(),
      kind,
      startDate: this.startDate(),
      endDate: this.endDate(),
      allLocations: this.allLocations(),
      locationIds: this.allLocations() ? [] : [...this.locationIds()],
      lines: this.rows().map((row) => ({
        productId: kind === 'ItemWise' ? row.productId || null : null,
        categoryId: kind === 'CategoryWise' ? row.categoryId || null : null,
        minimumAmount: kind === 'Slab' ? row.minimumAmount : null,
        discountPct: row.discountPct ?? 0,
      })),
      isActive: this.isActive(),
    };

    const editingId = this.editingId();
    const request$ = editingId
      ? this.salesService.updateDiscountScheme(this.organizationId, editingId, request)
      : this.salesService.createDiscountScheme(this.organizationId, request);

    request$.subscribe({
      next: () => {
        this.saving.set(false);
        this.startCreate();
        this.load();
        this.formHeading()?.nativeElement.focus();
      },
      error: (err: unknown) => {
        this.saving.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not save the discount scheme. Please try again.');
      },
    });
  }

  protected requestDelete(scheme: DiscountScheme): void {
    this.confirmingDeleteId.set(scheme.id);
  }

  protected cancelDelete(): void {
    this.confirmingDeleteId.set(null);
  }

  /** The row and its buttons go, so focus moves to the list's heading rather than the page (phase 72). */
  protected confirmDelete(scheme: DiscountScheme): void {
    this.salesService.deleteDiscountScheme(this.organizationId, scheme.id).subscribe({
      next: () => {
        this.confirmingDeleteId.set(null);
        if (this.editingId() === scheme.id) this.startCreate();
        this.load();
        this.listHeading()?.nativeElement.focus();
      },
      error: (err: unknown) => {
        this.confirmingDeleteId.set(null);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not delete the discount scheme. Please try again.');
      },
    });
  }

  private updateRow(key: number, patch: Partial<SchemeRow>): void {
    this.rows.update((rows) => rows.map((row) => (row.key === key ? { ...row, ...patch } : row)));
  }

  private newRow(): SchemeRow {
    return { key: nextRowKey++, productId: '', categoryId: '', minimumAmount: null, discountPct: null };
  }

  private load(): void {
    this.loading.set(true);
    this.salesService.listDiscountSchemes(this.organizationId).subscribe({
      next: (items) => {
        this.items.set(items);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load discount schemes.');
      },
    });
  }
}
