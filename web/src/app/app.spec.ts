import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { DeferBlockBehavior, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { App, isTillUrl } from './app';

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
  });

  it('should create the app', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;
    expect(app).toBeTruthy();
  });

  it('should render a router outlet', () => {
    const fixture = TestBed.createComponent(App);
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('router-outlet')).toBeTruthy();
  });
});

@Component({ selector: 'app-blank', template: '' })
class Blank {}

/**
 * Phase 34c (NFR-5.2), Decision D. The platform chrome is inside a `@defer (when organizationId()
 * !== null)` block, which took 74.7 kB out of the initial bundle — every byte of it previously
 * downloaded by visitors to the login screen, who can never see any of it.
 *
 * <b>Both halves are asserted here because a defer block can fail in two opposite ways</b>, and
 * neither shows up in a build: the chunk can stay unloaded so the chrome never appears at all, or
 * the trigger can be wrong in the other direction so it renders where it should not. The first would
 * have been a silent regression of everything phase 34b shipped.
 *
 * `DeferBlockBehavior.Playthrough` is what makes this a real test rather than a template read: it
 * evaluates the `when` trigger exactly as the browser does, instead of the default test behaviour of
 * rendering the placeholder and stopping.
 */
describe('App — the deferred platform chrome (phase 34c Decision D)', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      deferBlockBehavior: DeferBlockBehavior.Playthrough,
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([
          { path: 'login', component: Blank },
          { path: 'organizations/:organizationId/dashboard', component: Blank },
          { path: 'organizations/:organizationId/pos/till/:locationId', component: Blank },
        ]),
      ],
    }).compileComponents();
  });

  it('does not render outside an organization', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    await TestBed.inject(Router).navigate(['/login']);
    await fixture.whenStable();
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.platform-bar')).toBeNull();
    expect(compiled.querySelector('app-left-nav')).toBeNull();
  });

  it('renders once a navigation puts an organization in scope', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    await TestBed.inject(Router).navigate([
      '/organizations',
      '11111111-1111-1111-1111-111111111111',
      'dashboard',
    ]);
    await fixture.whenStable();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.platform-bar')).not.toBeNull();
    expect(compiled.querySelector('app-left-nav')).not.toBeNull();
    expect(compiled.querySelector('app-global-search')).not.toBeNull();
    expect(compiled.querySelector('app-date-range-picker')).not.toBeNull();
  });

  it('steps aside at the till, which is a full-screen layout of its own (phase 62)', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    await TestBed.inject(Router).navigate([
      '/organizations',
      '11111111-1111-1111-1111-111111111111',
      'pos',
      'till',
      '22222222-2222-2222-2222-222222222222',
    ]);
    await fixture.whenStable();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.platform-bar')).toBeNull();
    expect(compiled.querySelector('app-left-nav')).toBeNull();
    expect(compiled.querySelector('main')!.classList.contains('shell-with-nav')).toBe(false);
  });
});

describe('isTillUrl', () => {
  it('is the pos route tree inside an organization, and nothing else', () => {
    expect(isTillUrl('/organizations/abc/pos')).toBe(true);
    expect(isTillUrl('/organizations/abc/pos/till/loc?x=1')).toBe(true);
    expect(isTillUrl('/organizations/abc/configuration/pos')).toBe(false);
    expect(isTillUrl('/organizations/abc/home')).toBe(false);
    expect(isTillUrl('/pos')).toBe(false);
  });
});
