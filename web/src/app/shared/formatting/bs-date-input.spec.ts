import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { FormControl, ReactiveFormsModule } from '@angular/forms';

import { BsDateInput } from './bs-date-input';
import { DatePreferenceService } from './date-preference';

/**
 * The roadmap's Phase 23 exit criterion, written as a test: <b>"a date entered in BS persists and
 * reads back identically in both calendars."</b> That is the assertion this file exists for, and it
 * is asserted through both of the shapes this app binds dates with -- 60 signal-based sites and 6
 * Reactive Forms ones -- because the component has to serve both.
 *
 * The invariant underneath it (Decision A) is that <b>what leaves this component is always an AD ISO
 * string</b>. BS never reaches a DTO, a query window or a column; it exists at this edge only.
 */
@Component({
  imports: [BsDateInput],
  template: `<app-bs-date-input [value]="value()" (valueChange)="value.set($event)" />`,
})
class SignalHost {
  readonly value = signal<string | null>('2026-09-01');
}

@Component({
  imports: [BsDateInput, ReactiveFormsModule],
  template: `<app-bs-date-input [formControl]="control" />`,
})
class FormsHost {
  readonly control = new FormControl<string | null>('2026-09-01');
}

describe('BsDateInput', () => {
  let preference: DatePreferenceService;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({});
    preference = TestBed.inject(DatePreferenceService);
  });

  function render<T>(host: new () => T) {
    const fixture = TestBed.createComponent(host);
    fixture.detectChanges();
    return fixture;
  }

  function input(fixture: { nativeElement: HTMLElement }): HTMLInputElement {
    return fixture.nativeElement.querySelector('input')!;
  }

  describe('AD mode', () => {
    it('renders a native date input carrying the ISO value', () => {
      const fixture = render(SignalHost);
      const el = input(fixture);

      expect(el.type).toBe('date');
      expect(el.value).toBe('2026-09-01');
    });
  });

  describe('BS mode', () => {
    beforeEach(() => preference.set('BS'));

    it('renders the bound AD date as its BS equivalent, in DD-MM-YYYY', () => {
      const fixture = render(SignalHost);
      const el = input(fixture);

      // 2026-09-01 is BS 2083-05-16 -- the pairing read off the live reference product.
      expect(el.type).toBe('text');
      expect(el.value).toBe('16-05-2083');
    });

    it('emits an AD ISO string when a BS date is typed, never a BS one', () => {
      const fixture = render(SignalHost);
      const el = input(fixture);

      el.value = '17-05-2083';
      el.dispatchEvent(new Event('input'));
      fixture.detectChanges();

      // The exit criterion's first half: what is stored is AD.
      expect(fixture.componentInstance.value()).toBe('2026-09-02');
    });

    it('reads back identically in both calendars after a BS entry', () => {
      const fixture = render(SignalHost);
      const el = input(fixture);

      el.value = '17-05-2083';
      el.dispatchEvent(new Event('input'));
      fixture.detectChanges();

      // Still BS: the box shows what was typed.
      expect(input(fixture).value).toBe('17-05-2083');

      // Flip the whole app to AD: the same stored value now renders as the AD date, unchanged.
      preference.set('AD');
      fixture.detectChanges();
      expect(input(fixture).value).toBe('2026-09-02');
      expect(fixture.componentInstance.value()).toBe('2026-09-02');

      // ...and back again, with no drift.
      preference.set('BS');
      fixture.detectChanges();
      expect(input(fixture).value).toBe('17-05-2083');
    });

    it('does not commit a value while the typed BS date is invalid', () => {
      const fixture = render(SignalHost);
      const el = input(fixture);

      el.value = '32-05-2083'; // Bhadra 2083 has 31 days
      el.dispatchEvent(new Event('input'));
      fixture.detectChanges();

      // The bound value is untouched -- a half-typed or impossible date never reaches the DTO.
      expect(fixture.componentInstance.value()).toBe('2026-09-01');
      expect(fixture.nativeElement.textContent).toContain('Not a valid BS date');
    });

    it('clears the value when the box is emptied', () => {
      const fixture = render(SignalHost);
      const el = input(fixture);

      el.value = '';
      el.dispatchEvent(new Event('input'));
      fixture.detectChanges();

      expect(fixture.componentInstance.value()).toBe('');
    });

    it('shows an out-of-range date in AD rather than guessing a BS date for it', () => {
      const fixture = render(SignalHost);
      fixture.componentInstance.value.set('2040-01-01'); // past BS 2092
      fixture.detectChanges();

      expect(fixture.nativeElement.textContent).toContain('outside the supported Bikram Sambat range');
    });
  });

  describe('Reactive Forms binding', () => {
    it('writes the control value into the BS box', () => {
      preference.set('BS');
      const fixture = render(FormsHost);

      expect(input(fixture).value).toBe('16-05-2083');
    });

    it('pushes an AD ISO string back into the control when a BS date is typed', () => {
      preference.set('BS');
      const fixture = render(FormsHost);
      const el = input(fixture);

      el.value = '17-05-2083';
      el.dispatchEvent(new Event('input'));
      fixture.detectChanges();

      // Same invariant through the other binding shape: the FormControl holds AD.
      expect(fixture.componentInstance.control.value).toBe('2026-09-02');
    });

    it('honours a disabled control', () => {
      const fixture = render(FormsHost);
      fixture.componentInstance.control.disable();
      fixture.detectChanges();

      expect(input(fixture).disabled).toBe(true);
    });
  });

  describe('the picker', () => {
    beforeEach(() => preference.set('BS'));

    it('opens on the calendar button and offers the days of the bound BS month', () => {
      const fixture = render(SignalHost);
      const button = fixture.nativeElement.querySelector('button')! as HTMLButtonElement;

      button.click();
      fixture.detectChanges();

      const days = fixture.nativeElement.querySelectorAll('.bs-date-day:not(.empty)');
      // Bhadra 2083 has 31 days -- driven by the table, not by a fixed 30/31 assumption.
      expect(days.length).toBe(31);
      expect(fixture.nativeElement.textContent).toContain('Bhadra 2083');
    });

    it('commits the AD equivalent of the day that is clicked', () => {
      const fixture = render(SignalHost);
      (fixture.nativeElement.querySelector('button') as HTMLButtonElement).click();
      fixture.detectChanges();

      const days = fixture.nativeElement.querySelectorAll('.bs-date-day:not(.empty)');
      (days[16] as HTMLButtonElement).click(); // the 17th day of Bhadra
      fixture.detectChanges();

      expect(fixture.componentInstance.value()).toBe('2026-09-02');
    });
  });
});
