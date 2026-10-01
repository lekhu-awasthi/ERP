import { ChangeDetectionStrategy, Component, computed, input, output, signal } from '@angular/core';

import { DenominationCount } from '../../../core/pos/pos.models';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';

/**
 * Phase 62 -- counting a drawer note by note, for a till that requires cash verification (phase 60's
 * setting): the opening float and the close's count. One number input per denomination the
 * location lists, labelled with the note it counts, and the running total beside them.
 *
 * <p>Emits only the rows with a count, which is the shape the server stores (`1000x2,500x1`).</p>
 */
@Component({
  selector: 'app-denomination-count',
  imports: [AmountPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <fieldset>
      <legend class="h6">{{ legend() }}</legend>
      <div class="row g-2">
        @for (value of denominations(); track value) {
          <div class="col-6 col-md-4">
            <label class="form-label small mb-0" for="{{ idPrefix() }}-{{ value }}">Rs {{ value }} &times;</label>
            <input
              class="form-control form-control-sm"
              type="number"
              min="0"
              step="1"
              inputmode="numeric"
              id="{{ idPrefix() }}-{{ value }}"
              [value]="countOf(value) || ''"
              (input)="onInput(value, $event)"
            />
          </div>
        }
      </div>
      <p class="mt-2 mb-0">Counted: <strong>{{ total() | amount }}</strong></p>
    </fieldset>
  `,
})
export class DenominationCountInput {
  /** The location's note values, largest first. */
  readonly denominations = input.required<readonly number[]>();

  /** Makes each input's id unique on a page that counts twice. */
  readonly idPrefix = input('pos-count');

  readonly legend = input('Count the notes');

  readonly countsChange = output<DenominationCount[]>();

  private readonly counts = signal<ReadonlyMap<number, number>>(new Map());

  protected readonly total = computed(() =>
    [...this.counts()].reduce((sum, [value, count]) => sum + value * count, 0));

  protected countOf(value: number): number {
    return this.counts().get(value) ?? 0;
  }

  protected onInput(value: number, event: Event): void {
    const raw = (event.target as HTMLInputElement).valueAsNumber;
    const count = Number.isInteger(raw) && raw > 0 ? raw : 0;

    const next = new Map(this.counts());
    if (count > 0) {
      next.set(value, count);
    } else {
      next.delete(value);
    }

    this.counts.set(next);
    this.countsChange.emit(
      this.denominations()
        .filter((x) => next.has(x))
        .map((x) => ({ value: x, count: next.get(x)! })),
    );
  }
}
