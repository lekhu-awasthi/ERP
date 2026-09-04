import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, UrlTree, convertToParamMap, provideRouter } from '@angular/router';
import { Observable, firstValueFrom } from 'rxjs';

import { environment } from '../../../environments/environment';
import { clearFeatureGuardCache, featureGuard } from './feature.guard';

const ORGANIZATION_ID = '11111111-1111-1111-1111-111111111111';

/** A snapshot shaped like the real one: the id lives on the parent, which is what the guard walks up
 * to find. */
function routeSnapshot(): ActivatedRouteSnapshot {
  const parent = { paramMap: convertToParamMap({ id: ORGANIZATION_ID }), parent: null };
  return { paramMap: convertToParamMap({}), parent } as unknown as ActivatedRouteSnapshot;
}

function subscriptionWith(feature: string, isEnabled: boolean) {
  return {
    organizationId: ORGANIZATION_ID,
    planName: 'Trial',
    trialStartsAt: '2026-01-01',
    trialEndsAt: '2026-02-01',
    isTrialActive: true,
    daysRemaining: 10,
    irdSyncEnabled: false,
    features: [{ feature, displayName: feature, description: '', isEnabled }],
  };
}

describe('featureGuard', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    clearFeatureGuardCache();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  function run(feature: string): Promise<boolean | UrlTree> {
    const result = TestBed.runInInjectionContext(() =>
      featureGuard(feature as never)(routeSnapshot(), {} as never),
    );
    return firstValueFrom(result as Observable<boolean | UrlTree>);
  }

  function expectSubscriptionRequest() {
    return httpMock.expectOne(`${environment.apiBaseUrl}/api/organizations/${ORGANIZATION_ID}/subscription`);
  }

  it('allows the route when the tenant has the feature', async () => {
    const outcome = run('TrackInventory');
    expectSubscriptionRequest().flush(subscriptionWith('TrackInventory', true));

    expect(await outcome).toBe(true);
  });

  it('redirects to the organization dashboard when the tenant does not', async () => {
    const outcome = run('Manufacturing');
    expectSubscriptionRequest().flush(subscriptionWith('Manufacturing', false));

    const result = await outcome;
    expect(result).toBeInstanceOf(UrlTree);
    expect((result as UrlTree).toString()).toBe(`/organizations/${ORGANIZATION_ID}/home`);
  });

  it('fails closed when the subscription cannot be read', async () => {
    // A guard that opened the route on an error would make the gate advisory rather than real.
    const outcome = run('TrackInventory');
    expectSubscriptionRequest().flush(null, { status: 500, statusText: 'Server Error' });

    expect(await outcome).toBeInstanceOf(UrlTree);
  });

  it('reads the subscription once per organization, not once per gated route', async () => {
    const first = run('TrackInventory');
    expectSubscriptionRequest().flush(subscriptionWith('TrackInventory', true));
    expect(await first).toBe(true);

    // No second request is issued -- httpMock.verify() in afterEach would fail on an unexpected one,
    // and expectNone asserts it directly.
    const second = run('TrackInventory');
    httpMock.expectNone(`${environment.apiBaseUrl}/api/organizations/${ORGANIZATION_ID}/subscription`);
    expect(await second).toBe(true);
  });
});
