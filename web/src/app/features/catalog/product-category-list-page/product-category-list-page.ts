import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { buildTreeRows, TreeRow } from '../../../core/common/tree';
import { CatalogService } from '../../../core/catalog/catalog.service';
import { ProductCategory } from '../../../core/catalog/catalog.models';

type ProductCategoryRow = TreeRow<ProductCategory>;

/** Same tree list-page pattern as ContactGroups (contact-group-list-page) -- see that
 * component's doc comment for the indentation approach. */
@Component({
  selector: 'app-product-category-list-page',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './product-category-list-page.html',
})
export class ProductCategoryListPage {
  private readonly route = inject(ActivatedRoute);
  private readonly catalogService = inject(CatalogService);
  private readonly fb = inject(FormBuilder);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly items = signal<ProductCategory[]>([]);
  protected readonly editingId = signal<string | null>(null);
  protected readonly confirmingDeleteId = signal<string | null>(null);

  protected readonly rows = computed<ProductCategoryRow[]>(() =>
    buildTreeRows(
      this.items(),
      (category) => category.id,
      (category) => category.parentCategoryId,
      (category) => category.name,
    ),
  );

  protected readonly parentOptions = computed<ProductCategoryRow[]>(() => {
    const editingId = this.editingId();
    return this.rows().filter((row) => row.item.id !== editingId);
  });

  protected readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(100)]],
    parentCategoryId: [''],
    isActive: [true],
  });

  constructor() {
    this.load();
  }

  protected startCreate(): void {
    this.editingId.set(null);
    this.form.reset({ name: '', parentCategoryId: '', isActive: true });
  }

  protected startEdit(category: ProductCategory): void {
    this.editingId.set(category.id);
    this.form.reset({
      name: category.name,
      parentCategoryId: category.parentCategoryId ?? '',
      isActive: category.isActive,
    });
  }

  protected save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.saving.set(true);
    this.errorMessage.set(null);

    const { name, parentCategoryId, isActive } = this.form.getRawValue();
    const editingId = this.editingId();

    const request$ = editingId
      ? this.catalogService.updateProductCategory(this.organizationId, editingId, {
          name,
          parentCategoryId: parentCategoryId || null,
          isActive,
        })
      : this.catalogService.createProductCategory(this.organizationId, {
          name,
          parentCategoryId: parentCategoryId || null,
        });

    request$.subscribe({
      next: () => {
        this.saving.set(false);
        this.startCreate();
        this.load();
      },
      error: (err: unknown) => {
        this.saving.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not save product category. Please try again.');
      },
    });
  }

  protected requestDelete(category: ProductCategory): void {
    this.confirmingDeleteId.set(category.id);
  }

  protected cancelDelete(): void {
    this.confirmingDeleteId.set(null);
  }

  protected confirmDelete(category: ProductCategory): void {
    this.catalogService.deleteProductCategory(this.organizationId, category.id).subscribe({
      next: () => {
        this.confirmingDeleteId.set(null);
        this.load();
      },
      error: (err: unknown) => {
        this.confirmingDeleteId.set(null);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not delete product category. Please try again.');
      },
    });
  }

  private load(): void {
    this.loading.set(true);
    this.catalogService.listProductCategories(this.organizationId).subscribe({
      next: (items) => {
        this.items.set(items);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load product categories.');
      },
    });
  }
}
