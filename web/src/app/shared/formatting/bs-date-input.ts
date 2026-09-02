import { Component, ElementRef, computed, forwardRef, inject, input, output, signal } from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';

import { BS_MONTH_NAMES, BsDate, FIRST_BS_YEAR, LAST_BS_YEAR, adToBs, bsDaysInMonth, bsToAd, formatBs, parseBs } from './bs-date';
import { DatePreferenceService } from './date-preference';

interface DayCell {
  readonly bsDay: number;
  readonly adDay: number;
  readonly iso: string;
  readonly isToday: boolean;
  readonly isSelected: boolean;
}

/**
 * NFR-1.1's entry half, and the single replacement for all 66 native `<input type="date">` this app
 * had before Phase 23. A BS date cannot be entered through a native date input -- the browser
 * renders its own AD picker and there is no way to reach it -- so every one of those was a
 * replacement rather than a decoration.
 *
 * <b>What is stored is always AD (Phase 23, Decision A).</b> `value` in and `valueChange` out are
 * always an ISO `yyyy-MM-dd` Gregorian string, exactly what the native input produced and exactly
 * what every DTO already carries. BS is a presentation and entry format converted at this edge and
 * nowhere else -- no column, no DTO field and no report window changes meaning.
 *
 * <b>It works in both shapes this app uses.</b> 60 of the 66 sites are signal-based
 * (`[value]` + `(change)`) and 6 are Reactive Forms (`formControlName`), so this is both a
 * `[value]`/`(valueChange)` component and a `ControlValueAccessor`. Which one is driving is decided
 * by whether Angular registered a CVA callback, so the two never fight over the displayed value.
 *
 * <b>In AD mode it renders a real native date input</b> rather than reimplementing one -- the
 * browser's own picker is better than anything here, and keyboard and mobile behaviour come free.
 * This is the one template the sweep guard allow-lists for that reason.
 *
 * Two gotchas from Phase 22 are load-bearing in the BS popup: Bootstrap's JavaScript is not loaded
 * anywhere in this app, so the popup is driven from a signal rather than `data-bs-toggle`; and a
 * popup inside a `.table-responsive` is clipped by that wrapper's computed `overflow-y`, so it is
 * positioned `fixed` at coordinates captured from the trigger on open.
 */
@Component({
  selector: 'app-bs-date-input',
  imports: [],
  templateUrl: './bs-date-input.html',
  styleUrl: './bs-date-input.scss',
  providers: [
    {
      provide: NG_VALUE_ACCESSOR,
      useExisting: forwardRef(() => BsDateInput),
      multi: true,
    },
  ],
})
export class BsDateInput implements ControlValueAccessor {
  private readonly preference = inject(DatePreferenceService);
  private readonly host = inject(ElementRef<HTMLElement>);

  readonly value = input<string | null>(null);
  readonly disabled = input(false);
  readonly inputClass = input('form-control');
  readonly inputId = input<string | null>(null);

  readonly valueChange = output<string>();

  /** Set only by writeValue; `undefined` means "no CVA is driving this instance". */
  private readonly written = signal<string | null | undefined>(undefined);
  private readonly cvaDisabled = signal(false);
  private cvaMode = false;
  private onChange: (value: string) => void = () => undefined;
  private onTouched: () => void = () => undefined;

  protected readonly open = signal(false);
  protected readonly popupTop = signal(0);
  protected readonly popupLeft = signal(0);
  /** Set while the user is mid-edit in the BS text box, so their keystrokes are not overwritten. */
  protected readonly draft = signal<string | null>(null);
  protected readonly draftInvalid = signal(false);

  protected readonly isBs = computed(() => this.preference.isBs());
  protected readonly isDisabled = computed(() => this.disabled() || this.cvaDisabled());

  /** The bound AD value, whichever of the two APIs is supplying it. */
  protected readonly current = computed(() => {
    const written = this.written();
    return written === undefined ? this.value() : written;
  });

  protected readonly bsText = computed(() => {
    const draft = this.draft();
    if (draft !== null) {
      return draft;
    }
    const iso = this.current();
    if (!iso) {
      return '';
    }
    const bs = adToBs(iso);
    return bs === null ? '' : reorder(formatBs(bs));
  });

  /** Shown when the bound AD date exists but falls outside the conversion table. */
  protected readonly outOfRange = computed(() => {
    const iso = this.current();
    return !!iso && adToBs(iso) === null;
  });

  /** The BS year/month the popup grid is showing. */
  private readonly viewYear = signal(0);
  private readonly viewMonth = signal(0);

  protected readonly viewLabel = computed(
    () => `${BS_MONTH_NAMES[this.viewMonth() - 1]} ${this.viewYear()}`,
  );

  /** `Aug/Sep 2026` -- the AD span this BS month covers, as the live reference product shows it. */
  protected readonly viewAdLabel = computed(() => {
    const cells = this.cells();
    const first = cells.find((c) => c !== null);
    const last = [...cells].reverse().find((c) => c !== null);
    if (!first || !last) {
      return '';
    }
    const start = new Date(`${first.iso}T00:00:00Z`);
    const end = new Date(`${last.iso}T00:00:00Z`);
    const month = (d: Date) => d.toLocaleString('en-US', { month: 'short', timeZone: 'UTC' });
    const left = month(start);
    const right = month(end);
    const year = end.getUTCFullYear();
    return left === right ? `${left} ${year}` : `${left}/${right} ${year}`;
  });

  protected readonly canGoBack = computed(
    () => this.viewYear() > FIRST_BS_YEAR || this.viewMonth() > 1,
  );
  protected readonly canGoForward = computed(
    () => this.viewYear() < LAST_BS_YEAR || this.viewMonth() < 12,
  );

  /** Leading nulls pad the grid so day 1 lands under its real weekday (Sunday-first). */
  protected readonly cells = computed<(DayCell | null)[]>(() => {
    const year = this.viewYear();
    const month = this.viewMonth();
    const length = bsDaysInMonth(year, month);
    if (!length) {
      return [];
    }
    const firstIso = bsToAd({ year, month, day: 1 });
    if (!firstIso) {
      return [];
    }
    const selected = this.current();
    const todayIso = isoToday();
    const leading = new Date(`${firstIso}T00:00:00Z`).getUTCDay();

    const out: (DayCell | null)[] = Array.from({ length: leading }, () => null);
    for (let day = 1; day <= length; day++) {
      const iso = bsToAd({ year, month, day })!;
      out.push({
        bsDay: day,
        adDay: Number(iso.slice(8, 10)),
        iso,
        isToday: iso === todayIso,
        isSelected: iso === selected,
      });
    }
    return out;
  });

  protected readonly weekdays = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];

  // --- ControlValueAccessor -------------------------------------------------

  writeValue(value: string | null): void {
    this.written.set(value ? value.slice(0, 10) : null);
    this.draft.set(null);
    this.draftInvalid.set(false);
  }

  registerOnChange(fn: (value: string) => void): void {
    this.cvaMode = true;
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.cvaDisabled.set(isDisabled);
  }

  // --- Interaction ----------------------------------------------------------

  /** The native AD input's own change event. */
  protected onAdChange(event: Event): void {
    this.commit((event.target as HTMLInputElement).value);
  }

  /** Typing `DD-MM-YYYY` straight into the BS box, without opening the picker. */
  protected onBsInput(event: Event): void {
    const text = (event.target as HTMLInputElement).value;
    this.draft.set(text);

    if (text.trim() === '') {
      this.draftInvalid.set(false);
      this.commit('');
      return;
    }
    const bs = parseBs(toIsoOrder(text));
    const iso = bs === null ? null : bsToAd(bs);
    this.draftInvalid.set(iso === null);
    if (iso !== null) {
      this.commit(iso);
    }
  }

  protected onBsBlur(): void {
    // Snap the box back to the committed value so a half-typed date never lingers on screen
    // looking saved.
    this.draft.set(null);
    this.draftInvalid.set(false);
    this.onTouched();
  }

  protected togglePicker(event: MouseEvent): void {
    if (this.isDisabled()) {
      return;
    }
    if (this.open()) {
      this.open.set(false);
      return;
    }

    const iso = this.current();
    const bs = (iso && adToBs(iso)) || adToBs(isoToday()) || { year: FIRST_BS_YEAR, month: 1, day: 1 };
    this.viewYear.set(bs.year);
    this.viewMonth.set(bs.month);

    // Fixed positioning from the trigger's own rect -- a popup rendered inside a .table-responsive
    // is otherwise clipped by that wrapper's overflow (Phase 22's gotcha).
    const rect = (event.currentTarget as HTMLElement).getBoundingClientRect();
    const anchor = this.host.nativeElement.getBoundingClientRect();
    this.popupTop.set(rect.bottom + 4);
    this.popupLeft.set(Math.min(anchor.left, window.innerWidth - 300));
    this.open.set(true);
  }

  protected close(): void {
    this.open.set(false);
  }

  protected pick(cell: DayCell): void {
    this.draft.set(null);
    this.draftInvalid.set(false);
    this.commit(cell.iso);
    this.open.set(false);
  }

  protected pickToday(): void {
    const today = isoToday();
    if (adToBs(today)) {
      this.draft.set(null);
      this.commit(today);
      this.open.set(false);
    }
  }

  protected step(months: number): void {
    let year = this.viewYear();
    let month = this.viewMonth() + months;
    while (month > 12) {
      month -= 12;
      year++;
    }
    while (month < 1) {
      month += 12;
      year--;
    }
    if (year < FIRST_BS_YEAR || year > LAST_BS_YEAR) {
      return;
    }
    this.viewYear.set(year);
    this.viewMonth.set(month);
  }

  private commit(iso: string): void {
    if (this.cvaMode) {
      this.written.set(iso || null);
      this.onChange(iso);
    }
    this.valueChange.emit(iso);
  }
}

/** `dd-MM-yyyy` <-> `yyyy-MM-dd`, the two orders this component moves between. */
function reorder(ymd: string): string {
  const [y, m, d] = ymd.split('-');
  return `${d}-${m}-${y}`;
}

function toIsoOrder(dmy: string): string {
  const parts = dmy.trim().split(/[-/.]/);
  if (parts.length !== 3) {
    return dmy;
  }
  const [d, m, y] = parts;
  return `${y}-${m}-${d}`;
}

/**
 * Today on the <b>Nepal wall clock</b>, never UTC -- this is a Nepal-only product, and between
 * 18:15 and 24:00 UTC the Nepal calendar date is already tomorrow, so a UTC-derived "today" would
 * silently highlight the wrong day in the picker. Mirrors `Domain/Common/NepalTime`'s fixed
 * UTC+05:45 offset (Nepal has had no DST since 1986). Note this is a time-zone concern and is
 * separate from the BS calendar conversion itself.
 */
function isoToday(): string {
  const nowMs = Date.now() + (5 * 60 + 45) * 60_000;
  return new Date(nowMs).toISOString().slice(0, 10);
}
