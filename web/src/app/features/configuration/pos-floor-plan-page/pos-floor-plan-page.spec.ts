import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { Observable, of } from 'rxjs';

import { PosFloorPlan, PosTableLayoutInput } from '../../../core/pos/pos-restaurant.models';
import { PosRestaurantService } from '../../../core/pos/pos-restaurant.service';
import { PosFloorPlanPage } from './pos-floor-plan-page';

/** Phase 64 -- the floor-plan editor: moved by keyboard as well as mouse, saved whole, nothing deleted. */
describe('PosFloorPlanPage', () => {
  const plan: PosFloorPlan = {
    locationId: 'loc-1', locationName: 'POS Restaurant', canvasWidth: 1100, canvasHeight: 800, maxCapacity: 100,
    areas: [{ id: 'gf', name: 'Ground Floor', isActive: true, tables: [
      { id: 't1', name: 'T1', capacity: 4, shape: 'Rectangle', x: 100, y: 100, width: 250, height: 100, isActive: true, isOccupied: false },
      { id: 't2', name: 'T2', capacity: 2, shape: 'Circle', x: 450, y: 100, width: 100, height: 100, isActive: true, isOccupied: true },
    ] }],
  };

  function page() {
    const saved: PosTableLayoutInput[][] = [];
    TestBed.configureTestingModule({
      imports: [PosFloorPlanPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: PosRestaurantService,
          useValue: {
            getFloorPlan: (): Observable<PosFloorPlan> => of(plan),
            saveLayout: (_o: string, _a: string, tables: PosTableLayoutInput[]): Observable<PosFloorPlan> => {
              saved.push(tables);
              return of(plan);
            },
          },
        },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: 'org-1', locationId: 'loc-1' }) } } },
      ],
    });

    const fixture = TestBed.createComponent(PosFloorPlanPage);
    const element = fixture.nativeElement as HTMLElement;
    const render = () => {
      fixture.detectChanges();
      TestBed.tick();
    };
    render();

    const button = (text: string) =>
      [...element.querySelectorAll<HTMLButtonElement>('button')].find((b) => b.textContent?.trim().startsWith(text))!;

    return { element, saved, render, button };
  }

  it('moves a selected table with the arrow keys and saves every table, existing and new', () => {
    const p = page();

    const t1 = p.button('T1');
    t1.click();
    p.render();
    expect(t1.getAttribute('aria-pressed')).toBe('true');
    t1.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight' }));
    t1.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', shiftKey: true }));
    p.render();
    expect(p.element.querySelector<HTMLInputElement>('#floor-table-x')!.value).toBe('110');
    expect(p.element.querySelector<HTMLInputElement>('#floor-table-y')!.value).toBe('150');

    p.button('Add Table').click();
    p.render();
    p.button('Save Layout').click();
    p.render();

    const layout = p.saved[0];
    expect(layout.map((t) => [t.id, t.name, t.x, t.y])).toEqual([
      ['t1', 'T1', 110, 150],
      ['t2', 'T2', 450, 100],
      [null, 'T3', 20, 20],
    ]);
  });

  // Phase 64's browser pass: a release the canvas never saw left the drag running, and every later
  // movement over the canvas kept moving whichever table was selected.
  it('drags only the table it grabbed, and nothing moves after the release', () => {
    vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockReturnValue({ width: 550 } as DOMRect);
    const p = page();
    const pointer = (type: string, x: number, y: number, target: EventTarget = document) =>
      target.dispatchEvent(new PointerEvent(type, { clientX: x, clientY: y, button: 0, bubbles: true }));

    // A click is not a drag.
    const t1 = p.button('T1');
    pointer('pointerdown', 100, 100, t1);
    pointer('pointermove', 102, 101);
    pointer('pointerup', 102, 101);
    p.render();
    expect(p.element.querySelector<HTMLInputElement>('#floor-table-x')!.value).toBe('100');

    // A drag of 10px on a 550px-wide canvas is 20 units of the 1100-unit floor.
    pointer('pointerdown', 100, 100, p.button('T2'));
    pointer('pointermove', 110, 105);
    pointer('pointerup', 110, 105);
    pointer('pointermove', 300, 300);
    p.render();
    p.button('Save Layout').click();
    p.render();

    expect(p.saved[0].map((t) => [t.name, t.x, t.y])).toEqual([['T1', 100, 100], ['T2', 470, 110]]);
  });

  it('keeps an occupied table active and says why', () => {
    const p = page();

    p.button('T2').click();
    p.render();

    expect(p.element.querySelector<HTMLInputElement>('#floor-table-active')!.disabled).toBe(true);
    expect(p.element.textContent).toContain('An order is seated here');
  });
});
