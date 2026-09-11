import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';

import { BillingLocation, BillingLocationSettings } from '../../core/organizations/organizations.models';
import { OrganizationsService } from '../../core/organizations/organizations.service';
import { BillingLocationStore } from './billing-location-store';
import { defaultWarehouseSeed } from './default-warehouse-seed';
import { DocumentLocationPicker } from './document-location-picker';

const organizationId = '11111111-1111-1111-1111-111111111111';

const headOffice: BillingLocation = {
  id: 'loc-ho',
  code: 'HO',
  name: 'HeadOffice',
  address: null,
  warehouseId: 'wh-main',
  warehouseName: 'Main Warehouse',
  locationType: 'HeadOffice',
  isHeadOffice: true,
  isActive: true,
};

const branch: BillingLocation = {
  id: 'loc-br1',
  code: 'BR1',
  name: 'Branch One',
  address: 'Pokhara',
  warehouseId: null,
  warehouseName: null,
  locationType: 'Standard',
  isHeadOffice: false,
  isActive: true,
};

function settings(types: string[]): BillingLocationSettings {
  return {
    locationScopeMode: 'AllTransactions',
    locationWiseReportPermission: false,
    multipleLocationsEnabled: true,
    locationBearingDocumentTypes: types,
  };
}

@Component({
  selector: 'app-picker-host',
  imports: [DocumentLocationPicker],
  template: `
    <app-document-location-picker
      [organizationId]="organizationId"
      [documentType]="documentType"
      [(locationId)]="locationId"
      (defaultWarehouseId)="seen.push($event)"
    />
  `,
})
class PickerHost {
  readonly organizationId = organizationId;
  documentType = 'Invoice';
  readonly locationId = signal('');
  readonly seen: string[] = [];
}

function host(scopedTypes: string[], locations: BillingLocation[] = [headOffice, branch]) {
  TestBed.configureTestingModule({
    imports: [PickerHost],
    providers: [
      {
        provide: OrganizationsService,
        useValue: {
          listBillingLocations: () => of(locations),
          getBillingLocationSettings: () => of(settings(scopedTypes)),
        },
      },
    ],
  });

  const fixture = TestBed.createComponent(PickerHost);
  fixture.detectChanges();
  return fixture;
}

describe('DocumentLocationPicker', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('renders nothing when this document type is outside the tenant scope', () => {
    const fixture = host(['Quotation']);

    expect((fixture.nativeElement as HTMLElement).querySelector('select')).toBeNull();
  });

  /**
   * The default the live header shows. Chosen explicitly rather than left to the first `<option>`:
   * a select displaying one location while the request carries none is the phase-5 select race in
   * spirit, so the displayed value and the stored one are made to agree.
   */
  it('defaults to HeadOffice and writes that back to the host', () => {
    const fixture = host(['Invoice']);
    const select = (fixture.nativeElement as HTMLElement).querySelector('select');

    expect(select).not.toBeNull();
    expect(fixture.componentInstance.locationId()).toBe('loc-ho');
    expect(select!.options.length).toBe(2);
    expect(select!.options[0].selected).toBe(true);
  });

  /** An existing document's stored branch must survive the picker's own default. */
  it('leaves a location the host already set alone', () => {
    TestBed.configureTestingModule({
      imports: [PickerHost],
      providers: [
        {
          provide: OrganizationsService,
          useValue: {
            listBillingLocations: () => of([headOffice, branch]),
            getBillingLocationSettings: () => of(settings(['Invoice'])),
          },
        },
      ],
    });

    const fixture = TestBed.createComponent(PickerHost);
    fixture.componentInstance.locationId.set('loc-br1');
    fixture.detectChanges();

    expect(fixture.componentInstance.locationId()).toBe('loc-br1');
  });

  /**
   * Decision D: the location's warehouse is a *default*, offered once on the way in. Emitting on
   * every change would make it reactive, which the reference product is not — switching to a
   * location with no warehouse there leaves the document's warehouse untouched.
   */
  it('offers the resolved location default warehouse exactly once', () => {
    const fixture = host(['Invoice']);

    expect(fixture.componentInstance.seen).toEqual(['wh-main']);

    fixture.componentInstance.locationId.set('loc-br1');
    fixture.detectChanges();

    expect(fixture.componentInstance.seen).toEqual(['wh-main']);
  });

  it('offers nothing when the resolved location has no default warehouse', () => {
    const fixture = host(['Invoice'], [branch]);

    expect(fixture.componentInstance.seen).toEqual([]);
  });
});

describe('BillingLocationStore', () => {
  afterEach(() => TestBed.resetTestingModule());

  function store(calls: { locations: number }) {
    TestBed.configureTestingModule({
      providers: [
        {
          provide: OrganizationsService,
          useValue: {
            listBillingLocations: () => {
              calls.locations += 1;
              return of([headOffice, branch]);
            },
            getBillingLocationSettings: () => of(settings(['Invoice'])),
          },
        },
      ],
    });

    return TestBed.inject(BillingLocationStore);
  }

  /** The reason this is a store and not a per-page subscription: a grid of rows must not be a grid
   * of requests. */
  it('reads one organization once however many callers ask', () => {
    const calls = { locations: 0 };
    const subject = store(calls);

    subject.locations(organizationId)();
    subject.name(organizationId, 'loc-br1');
    subject.name(organizationId, 'loc-ho');

    expect(calls.locations).toBe(1);
    expect(subject.name(organizationId, 'loc-br1')).toBe('Branch One');
    expect(subject.name(organizationId, null)).toBeNull();
    expect(subject.name(organizationId, 'loc-missing')).toBeNull();
  });

  it('re-reads after an invalidate, which is what the Billing Location screen calls on save', () => {
    const calls = { locations: 0 };
    const subject = store(calls);

    subject.locations(organizationId)();
    subject.invalidate(organizationId);
    subject.locations(organizationId)();

    expect(calls.locations).toBe(2);
  });
});

describe('defaultWarehouseSeed', () => {
  /**
   * Two independent requests feed the seed and either can land first, so both orders are asserted.
   * Only asserting the fast one is how a prefill ships working on the developer's machine.
   */
  it('seeds whichever way round the two lists arrive', () => {
    for (const warehousesFirst of [true, false]) {
      const warehouseId = signal('');
      const warehouses = signal<{ id: string }[]>([]);
      const seed = defaultWarehouseSeed(warehouseId, warehouses);

      if (warehousesFirst) {
        warehouses.set([{ id: 'wh-main' }]);
        seed.retry();
        seed.offer('wh-main');
      } else {
        seed.offer('wh-main');
        warehouses.set([{ id: 'wh-main' }]);
        seed.retry();
      }

      expect(warehouseId()).toBe('wh-main');
    }
  });

  it('never overwrites a warehouse that is already chosen', () => {
    const warehouseId = signal('wh-other');
    const warehouses = signal<{ id: string }[]>([{ id: 'wh-main' }, { id: 'wh-other' }]);
    const seed = defaultWarehouseSeed(warehouseId, warehouses);

    seed.offer('wh-main');

    expect(warehouseId()).toBe('wh-other');
  });

  /** Seeding an id the `<select>` has no `<option>` for is the phase-5 native-select race. */
  it('ignores a default the warehouse list does not contain', () => {
    const warehouseId = signal('');
    const warehouses = signal<{ id: string }[]>([{ id: 'wh-main' }]);
    const seed = defaultWarehouseSeed(warehouseId, warehouses);

    seed.offer('wh-retired');

    expect(warehouseId()).toBe('');
  });
});
