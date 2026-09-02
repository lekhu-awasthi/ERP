import { Component, computed, inject } from '@angular/core';

import { DatePreferenceService } from './date-preference';

/**
 * NFR-1.1's "switchable per user preference", as the live reference product shapes it (Phase 23
 * Step 2): a <b>global</b> AD/BS pair in the user's own menu -- not a per-field toggle, and never
 * both calendars at once. Flipping it re-renders every date in the app immediately, because
 * `DatePreferenceService` holds a signal and `NepaliDatePipe` is impure.
 *
 * This app has no global navigation chrome (each page carries its own header), so this sits in the
 * user block of the two screens that are always reachable: the organization launcher and the Home
 * dashboard. See `DatePreferenceService` for where the choice is stored and what that does not
 * cover.
 */
@Component({
  selector: 'app-calendar-toggle',
  imports: [],
  template: `
    <div class="btn-group btn-group-sm" role="group" aria-label="Calendar format">
      <button
        type="button"
        class="btn"
        [class.btn-success]="!isBs()"
        [class.btn-outline-secondary]="isBs()"
        [attr.aria-pressed]="!isBs()"
        (click)="choose('AD')"
      >
        AD
      </button>
      <button
        type="button"
        class="btn"
        [class.btn-success]="isBs()"
        [class.btn-outline-secondary]="!isBs()"
        [attr.aria-pressed]="isBs()"
        (click)="choose('BS')"
      >
        BS
      </button>
    </div>
  `,
})
export class CalendarToggle {
  private readonly preference = inject(DatePreferenceService);

  protected readonly isBs = computed(() => this.preference.isBs());

  protected choose(format: 'AD' | 'BS'): void {
    this.preference.set(format);
  }
}
