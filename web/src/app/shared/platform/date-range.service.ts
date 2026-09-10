import { Injectable, computed, inject, signal } from '@angular/core';

import { PlatformService } from '../../core/platform/platform.service';
import { UserPreferenceKeys } from '../../core/platform/platform.models';
import { bsToAd } from '../formatting/bs-date';
import { FISCAL_YEAR_START_MONTH, currentFiscalYear } from '../formatting/bs-fiscal-year';

/**
 * Phase 34b — the global date range the top bar sets and list screens read.
 *
 * <b>This control is bigger than the module scan says it is.</b> `erp-module-scan.md` records a
 * "date-range filter (global period filter that scopes dashboard figures)". Confirmed live on
 * 2026-09-10 by instrumenting `fetch`/`XMLHttpRequest` on the reference tenant, it does **not** scope
 * dashboard figures: it scopes *list queries*. Opening Customers issued
 * `contacts?date_$gte=17-07-2026&date_$lte=10-09-2026`, and switching the preset to "Last 7 days"
 * re-issued the same list with the new bounds. Invoices, Chart of Accounts and Journal Vouchers do
 * the same. That is phase-32b's lesson for the fourth time — a recorded description of a control
 * nobody operated is not settled — and it is the reason this is a service consumed across the app
 * rather than two inputs on the dashboard.
 *
 * <b>It is not uniform even there, and that is not a bug to copy blindly.</b> On the same tenant
 * `products` and `contact-groups` receive no range at all. So the rule adopted here is the one the
 * observation actually supports: **the range scopes screens that show dated activity**, and master
 * data and configuration lists ignore it. Which lists opt in is stated by each list's own query, not
 * decided here.
 *
 * <b>Where it is stored.</b> The reference product keeps this in `localStorage`
 * (`TOP_DATE_FROM`/`TOP_DATE_TO`/`TOP_DATE_NAME`), so it does not follow a user between machines.
 * Phase 33 built `UserPreference` — a server-side per-`(organization, user, key)` store — precisely
 * for settings of this shape, so this uses that instead. That is a deliberate improvement on the
 * reference, recorded rather than silently made.
 */
@Injectable({ providedIn: 'root' })
export class DateRangeService {
  private readonly platform = inject(PlatformService);

  /** The preference key. One row per (organization, user) in `UserPreference`. */
  static readonly PREFERENCE_KEY: string = UserPreferenceKeys.dateRange;

  private readonly state = signal<DateRange>(rangeFor('fiscal-year-to-date'));
  private organizationId: string | null = null;

  /** The range in force. Consumers read `from`/`to` as ISO `yyyy-MM-dd`, the wire format everywhere. */
  readonly range = computed(() => this.state());
  readonly from = computed(() => this.state().from);
  readonly to = computed(() => this.state().to);
  readonly label = computed(() => this.state().label);
  readonly preset = computed(() => this.state().preset);

  /**
   * Adopts an organization's stored range. Called by the shell on entering an organization, the same
   * seam `DatePreferenceService.activate` uses for the AD/BS choice.
   */
  activate(organizationId: string): void {
    this.organizationId = organizationId;

    this.platform.getPreferences(organizationId).subscribe({
      next: (preferences) => {
        const stored = preferences.find((p) => p.key === DateRangeService.PREFERENCE_KEY);

        if (!stored) {
          return;
        }

        const parsed = parseStored(stored.value);

        if (parsed) {
          this.state.set(parsed);
        }
      },
      // No stored preference, or the store is unreachable: the default range is still correct.
      error: () => undefined,
    });
  }

  /** Switches to a named preset and re-derives its bounds from today. */
  setPreset(preset: DateRangePreset): void {
    this.apply(rangeFor(preset));
  }

  /** Sets an explicit range. `from`/`to` are ISO `yyyy-MM-dd`. */
  setCustom(from: string, to: string): void {
    this.apply({ preset: 'custom', label: 'Custom Range', from, to });
  }

  private apply(range: DateRange): void {
    this.state.set(range);

    if (!this.organizationId) {
      return;
    }

    // Fire and forget, exactly as the Quick Links tray does: the range is already in force locally,
    // and a failed write costs the next session its default, not this one its filter.
    this.platform
      .setPreference(this.organizationId, DateRangeService.PREFERENCE_KEY, JSON.stringify(range))
      .subscribe({ error: () => undefined });
  }
}

export type DateRangePreset =
  | 'today'
  | 'last-7'
  | 'last-15'
  | 'last-30'
  | 'last-45'
  | 'last-60'
  | 'fiscal-year-to-date'
  | 'fiscal-year'
  | 'custom';

export interface DateRange {
  readonly preset: DateRangePreset;
  readonly label: string;
  readonly from: string;
  readonly to: string;
}

/**
 * The presets, in the reference product's own order and wording — read off the live popover:
 * Today, Last 7 days, Last 15 days, Last 30 days, Last 45 days, Last 60 days, This Fiscal Year to
 * Date, Fiscal Year, Custom Range.
 */
export const DATE_RANGE_PRESETS: readonly { readonly preset: DateRangePreset; readonly label: string }[] = [
  { preset: 'today', label: 'Today' },
  { preset: 'last-7', label: 'Last 7 days' },
  { preset: 'last-15', label: 'Last 15 days' },
  { preset: 'last-30', label: 'Last 30 days' },
  { preset: 'last-45', label: 'Last 45 days' },
  { preset: 'last-60', label: 'Last 60 days' },
  { preset: 'fiscal-year-to-date', label: 'This Fiscal Year to Date' },
  { preset: 'fiscal-year', label: 'Fiscal Year' },
];

/** Bounds for a preset, as of `today`. Exported so its arithmetic can be tested against fixed dates. */
export function rangeFor(preset: DateRangePreset, today: Date = new Date()): DateRange {
  const label = DATE_RANGE_PRESETS.find((p) => p.preset === preset)?.label ?? 'Custom Range';
  const to = iso(today);

  const daysBack = (days: number): DateRange => ({
    preset,
    label,
    from: iso(new Date(today.getTime() - days * 86_400_000)),
    to,
  });

  switch (preset) {
    case 'today':
      return { preset, label, from: to, to };
    case 'last-7':
      return daysBack(7);
    case 'last-15':
      return daysBack(15);
    case 'last-30':
      return daysBack(30);
    case 'last-45':
      return daysBack(45);
    case 'last-60':
      return daysBack(60);
    case 'fiscal-year':
      return { preset, label, ...fiscalYearBounds(currentFiscalYear(today)) };
    case 'fiscal-year-to-date':
    default:
      return { preset: 'fiscal-year-to-date', label, from: fiscalYearBounds(currentFiscalYear(today)).from, to };
  }
}

/**
 * A fiscal year's AD bounds: Shrawan 1 of the named BS year to the day before Shrawan 1 of the next.
 *
 * Derived through `bsToAd` rather than by adding 365 days, because BS month lengths vary — this is
 * the same rule `BsCalendar` applies on the server, and phase-26b's gotcha is that both boundaries
 * have to stay pinned.
 */
export function fiscalYearBounds(fiscalYear: number): { from: string; to: string } {
  const from = bsToAd({ year: fiscalYear, month: FISCAL_YEAR_START_MONTH, day: 1 });
  const nextYearStart = bsToAd({ year: fiscalYear + 1, month: FISCAL_YEAR_START_MONTH, day: 1 });

  if (!from || !nextYearStart) {
    // Outside the supported BS range (2000–2092). Fall back to a calendar year rather than throwing
    // in a top-bar control: a wrong-but-sane range is recoverable, a broken shell is not.
    const year = new Date().getUTCFullYear();
    return { from: `${year}-01-01`, to: `${year}-12-31` };
  }

  const lastDay = new Date(`${nextYearStart}T00:00:00Z`);
  lastDay.setUTCDate(lastDay.getUTCDate() - 1);

  return { from, to: iso(lastDay) };
}

function iso(date: Date): string {
  return date.toISOString().slice(0, 10);
}

function parseStored(value: string): DateRange | null {
  try {
    const parsed = JSON.parse(value) as Partial<DateRange>;

    if (typeof parsed?.from !== 'string' || typeof parsed?.to !== 'string' || typeof parsed?.preset !== 'string') {
      return null;
    }

    // A relative preset is re-derived from today rather than restored: "Last 7 days" stored a week
    // ago must not come back meaning that week.
    return parsed.preset === 'custom'
      ? { preset: 'custom', label: 'Custom Range', from: parsed.from, to: parsed.to }
      : rangeFor(parsed.preset as DateRangePreset);
  } catch {
    return null;
  }
}
