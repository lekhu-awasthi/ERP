import { Component, computed, input, output } from '@angular/core';

/**
 * First shared UI component in this codebase (roadmap Phase 16c) -- every retrofitted list/report
 * page wires this in against its own PagedResult<T>. Deliberately dumb: it only knows
 * page/pageSize/totalCount and emits change events, the host page owns reloading.
 */
@Component({
  selector: 'app-pagination-control',
  imports: [],
  templateUrl: './pagination-control.html',
})
export class PaginationControl {
  readonly page = input.required<number>();
  readonly pageSize = input.required<number>();
  readonly totalCount = input.required<number>();
  readonly pageSizeOptions = input<number[]>([25, 50, 100, 200]);

  readonly pageChange = output<number>();
  readonly pageSizeChange = output<number>();

  protected readonly totalPages = computed(() => Math.max(1, Math.ceil(this.totalCount() / this.pageSize())));
  protected readonly hasPrevious = computed(() => this.page() > 1);
  protected readonly hasNext = computed(() => this.page() < this.totalPages());
  protected readonly rangeStart = computed(() => (this.totalCount() === 0 ? 0 : (this.page() - 1) * this.pageSize() + 1));
  protected readonly rangeEnd = computed(() => Math.min(this.page() * this.pageSize(), this.totalCount()));

  protected previous(): void {
    if (this.hasPrevious()) {
      this.pageChange.emit(this.page() - 1);
    }
  }

  protected next(): void {
    if (this.hasNext()) {
      this.pageChange.emit(this.page() + 1);
    }
  }

  protected onPageSizeChange(event: Event): void {
    const value = Number((event.target as HTMLSelectElement).value);
    this.pageSizeChange.emit(value);
  }
}
