import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { PlatformService } from '../../core/platform/platform.service';
import { QuickLinkDto } from '../../core/platform/platform.models';
import { QuickLinks } from './quick-links';

/**
 * Phase 39 — drag to reorder, closing phase 33's carried item #2.
 *
 * <p>The assertions are about the <b>order that gets saved</b>, not about the drag gesture's visual
 * feedback: the gesture is browser machinery, and what a test can meaningfully pin is that a drop
 * produces the sequence the user aimed at and that Done persists exactly that. The
 * {@link A_drag_across_several_tiles_moves_rather_than_swaps} case is the one a swap-based
 * implementation passes every adjacent test and still fails.</p>
 */
describe('QuickLinks reorder', () => {
  let fixture: ComponentFixture<QuickLinks>;
  let component: QuickLinks;
  let saved: QuickLinkDto[][];

  const stored: QuickLinkDto[] = [
    { name: 'Invoices', area: 'Sales', kind: 'List', url: 'sales/invoices' },
    { name: 'Contacts', area: 'CRM', kind: 'List', url: 'contacts' },
    { name: 'Products', area: 'Inventory', kind: 'List', url: 'products' },
    { name: 'Accounts', area: 'Accounting', kind: 'List', url: 'accounting/accounts' },
  ];

  beforeEach(async () => {
    saved = [];

    // The tray is stored as one JSON value in the per-user preference store (phase 33), not as its
    // own resource -- which is what makes "one whole-list write on Done" assertable at all.
    const platform = {
      getPreferences: () => of([{ key: 'quick-links', value: JSON.stringify(stored) }]),
      setPreference: (_organizationId: string, _key: string, value: string) => {
        saved.push(JSON.parse(value) as QuickLinkDto[]);
        return of({ key: 'quick-links', value });
      },
    };

    await TestBed.configureTestingModule({
      imports: [QuickLinks],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        { provide: PlatformService, useValue: platform },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(QuickLinks);
    fixture.componentRef.setInput('organizationId', 'org-1');
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  function urls(): string[] {
    return (component['links']() as QuickLinkDto[]).map((x) => x.url);
  }

  function drag(from: number, to: number): void {
    const event = { dataTransfer: null, preventDefault: () => {} } as unknown as DragEvent;
    component['onDragStart'](stored[from], event);
    component['onDrop'](stored[to], event);
  }

  it('loads the stored tray in its stored order', () => {
    expect(urls()).toEqual(['sales/invoices', 'contacts', 'products', 'accounting/accounts']);
  });

  it('a drag across several tiles moves rather than swaps', () => {
    drag(3, 0);

    // Moved: Accounts to the front, everything else shifted down one. A swap would have produced
    // ['accounting/accounts', 'contacts', 'products', 'sales/invoices'] — Invoices flung to the end.
    expect(urls()).toEqual(['accounting/accounts', 'sales/invoices', 'contacts', 'products']);
  });

  it('a drop onto the tile being dragged changes nothing', () => {
    drag(1, 1);

    expect(urls()).toEqual(['sales/invoices', 'contacts', 'products', 'accounting/accounts']);
  });

  it('the arrow buttons remain, and agree with the drag for an adjacent move', () => {
    component['move'](stored[0], 1);

    expect(urls()).toEqual(['contacts', 'sales/invoices', 'products', 'accounting/accounts']);
  });

  it('clears the dragging marker even when the drag ends outside a tile', () => {
    component['onDragStart'](stored[0], { dataTransfer: null } as unknown as DragEvent);
    expect(component['draggingUrl']()).toBe('sales/invoices');

    component['onDragEnd']();
    expect(component['draggingUrl']()).toBeNull();
  });

  /**
   * The reference product persists the whole tray on Done and has no per-link endpoint; the drag has
   * to land in that same single write rather than acquiring one of its own.
   */
  it('persists the dragged order as one whole-list write on Done', () => {
    component['startEditing']();
    drag(3, 0);
    component['done']();

    expect(saved).toHaveLength(1);
    expect(saved[0].map((x) => x.url)).toEqual([
      'accounting/accounts',
      'sales/invoices',
      'contacts',
      'products',
    ]);
  });

  it('writes nothing until Done', () => {
    component['startEditing']();
    drag(3, 0);

    expect(saved).toHaveLength(0);
  });
});
