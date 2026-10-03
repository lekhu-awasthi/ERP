import { convertToParamMap } from '@angular/router';
import { ActivatedRoute } from '@angular/router';

import { nepalToday } from '../formatting/nepal-time';
import { initialSalesChannel } from '../sales/sales-channel-filter';
import { lastDays, shiftDate } from './pos-period-filter';

/** Phase 66 -- the two small helpers every POS report reads its period and channel through. */
describe('POS report helpers', () => {
  it('moves a date by whole calendar days, across a month and a year', () => {
    expect(shiftDate('2026-10-02', -6)).toBe('2026-09-26');
    expect(shiftDate('2026-12-31', 1)).toBe('2027-01-01');
    expect(shiftDate('2028-03-01', -1)).toBe('2028-02-29');
  });

  it('takes "the last N days" to end today on the Nepal clock, today included', () => {
    const today = nepalToday();
    expect(lastDays(1)).toEqual({ fromDate: today, toDate: today });
    expect(lastDays(7)).toEqual({ fromDate: shiftDate(today, -6), toDate: today });
  });

  it('opens a report on the channel a POS screen linked with, and on every channel otherwise', () => {
    const route = (params: Record<string, string>) =>
      ({ snapshot: { queryParamMap: convertToParamMap(params) } }) as unknown as ActivatedRoute;

    expect(initialSalesChannel(route({ channel: 'Pos' }))).toBe('Pos');
    expect(initialSalesChannel(route({ channel: 'Erp' }))).toBe('Erp');
    expect(initialSalesChannel(route({ channel: 'Anything' }))).toBeNull();
    expect(initialSalesChannel(route({}))).toBeNull();
  });
});
