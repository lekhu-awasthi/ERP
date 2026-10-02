import { Component, DestroyRef, ElementRef, computed, inject, signal, viewChild } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { PosFloorArea, PosFloorPlan, PosTableShape } from '../../../core/pos/pos-restaurant.models';
import { PosRestaurantService } from '../../../core/pos/pos-restaurant.service';
import { StatusBanner } from '../../../shared/a11y/status-banner';

/** A table as the editor holds it until Save: an existing one keeps its id, a new one has none. */
interface DraftTable {
  key: string;
  id: string | null;
  name: string;
  capacity: number;
  shape: PosTableShape;
  x: number;
  y: number;
  width: number;
  height: number;
  isActive: boolean;
  isOccupied: boolean;
}

const MIN_SIZE = 40;
const NUDGE = 10;
const BIG_NUDGE = 50;

/** How far the pointer must travel before a press becomes a drag, so a click never nudges a table. */
const DRAG_THRESHOLD_PX = 4;

/**
 * Phase 64 -- Configurations > Point of Sale > Floor plan: a Restaurant location's areas and the tables
 * laid out on each (the vendor's Floorplan tab, read live 2026-10-02).
 *
 * <p><b>One save per area, of the whole layout</b>, as the vendor's <i>Save Changes</i> is: tables are
 * moved, added and edited here, and nothing reaches the server until Save. A table is never deleted --
 * the vendor offers <i>Make Inactive</i> and so does this -- because an order names its table.</p>
 *
 * <p><b>Moving a table works without a mouse</b> (phase 40): a table is a button; select it and the
 * arrow keys move it 10 units (50 with Shift), and its position and size are also plain number fields
 * in the form beside the canvas. Dragging is the mouse's shortcut to the same fields.</p>
 */
@Component({
  selector: 'app-pos-floor-plan-page',
  imports: [RouterLink, StatusBanner],
  templateUrl: './pos-floor-plan-page.html',
  styleUrl: './pos-floor-plan-page.scss',
})
export class PosFloorPlanPage {
  private readonly route = inject(ActivatedRoute);
  private readonly restaurantService = inject(PosRestaurantService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;
  protected readonly locationId = this.route.snapshot.paramMap.get('locationId')!;

  private readonly canvas = viewChild<ElementRef<HTMLElement>>('canvas');

  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly noticeMessage = signal<string | null>(null);

  protected readonly plan = signal<PosFloorPlan | null>(null);
  protected readonly areaId = signal<string | null>(null);
  protected readonly tables = signal<DraftTable[]>([]);
  protected readonly selectedKey = signal<string | null>(null);
  protected readonly dirty = signal(false);
  protected readonly showInactive = signal(false);

  protected readonly newAreaName = signal('');
  protected readonly areaFormOpen = signal(false);
  protected readonly areaName = signal('');
  protected readonly areaActive = signal(true);

  private newKey = 1;

  /**
   * The table being dragged, from press to release. The release is listened for on the document, not
   * the canvas: a release the canvas never saw (the pointer let go outside it) once left a drag running,
   * so every later movement over the canvas kept moving a table -- found in this phase's browser pass.
   */
  private drag: { key: string; startX: number; startY: number; originX: number; originY: number; moving: boolean } | null = null;
  private readonly onDocumentMove = (event: PointerEvent) => this.dragTo(event);
  private readonly onDocumentUp = () => this.endDrag();

  protected readonly area = computed<PosFloorArea | null>(() =>
    this.plan()?.areas.find((a) => a.id === this.areaId()) ?? null);

  protected readonly visibleTables = computed(() =>
    this.tables().filter((t) => t.isActive || this.showInactive()));

  protected readonly selected = computed(() => this.tables().find((t) => t.key === this.selectedKey()) ?? null);

  constructor() {
    inject(DestroyRef).onDestroy(() => this.endDrag());
    this.restaurantService.getFloorPlan(this.organizationId, this.locationId).subscribe({
      next: (plan) => {
        this.apply(plan);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the floor plan.');
      },
    });
  }

  // ---- Areas ----

  protected chooseArea(areaId: string, event?: Event): void {
    if (this.dirty() && !window.confirm('This area has unsaved changes. Leave them?')) {
      // The radio the user pressed is already checked by the browser; the binding has not changed, so
      // Angular will not put it back -- put the current area's radio back by hand.
      if (event?.target instanceof HTMLInputElement) event.target.checked = false;
      const current = document.getElementById(`floor-area-${this.areaId()}`) as HTMLInputElement | null;
      if (current) current.checked = true;
      return;
    }
    this.areaId.set(areaId);
    this.loadDrafts();
  }

  protected onNewAreaName(event: Event): void {
    this.newAreaName.set((event.target as HTMLInputElement).value);
  }

  protected addArea(): void {
    const name = this.newAreaName().trim();
    this.noticeMessage.set(null);
    if (!name) {
      this.errorMessage.set('An area needs a name.');
      return;
    }

    this.restaurantService.createArea(this.organizationId, this.locationId, name).subscribe({
      next: (plan) => {
        const created = plan.areas.find((a) => a.name === name);
        this.newAreaName.set('');
        this.apply(plan, created?.id);
        this.noticeMessage.set(`Area ${name} added.`);
      },
      error: (err: unknown) => this.errorMessage.set(extractErrorMessage(err) ?? 'Could not add the area.'),
    });
  }

  protected openAreaForm(): void {
    const area = this.area();
    if (!area) return;
    this.areaName.set(area.name);
    this.areaActive.set(area.isActive);
    this.areaFormOpen.set(true);
  }

  protected onAreaName(event: Event): void {
    this.areaName.set((event.target as HTMLInputElement).value);
  }

  protected onAreaActive(event: Event): void {
    this.areaActive.set((event.target as HTMLInputElement).checked);
  }

  protected saveArea(): void {
    const area = this.area();
    if (!area) return;

    this.restaurantService.updateArea(this.organizationId, area.id, this.areaName().trim(), this.areaActive()).subscribe({
      next: (plan) => {
        this.areaFormOpen.set(false);
        this.apply(plan, area.id);
        this.noticeMessage.set('Area saved.');
      },
      error: (err: unknown) => this.errorMessage.set(extractErrorMessage(err) ?? 'Could not save the area.'),
    });
  }

  // ---- Tables ----

  protected addTable(): void {
    // A table's name is unique at the location, not the area (a kitchen ticket says "T1"), so the
    // suggestion steps over every area's names as well as this one's unsaved ones.
    const names = new Set([
      ...(this.plan()?.areas ?? []).flatMap((a) => a.tables.map((t) => t.name.toLowerCase())),
      ...this.tables().map((t) => t.name.toLowerCase()),
    ]);
    let n = 1;
    while (names.has(`t${n}`)) n++;

    const table: DraftTable = {
      key: `new-${this.newKey++}`,
      id: null,
      name: `T${n}`,
      capacity: 4,
      shape: 'Rectangle',
      x: 20,
      y: 20,
      width: 160,
      height: 100,
      isActive: true,
      isOccupied: false,
    };
    this.tables.update((list) => [...list, table]);
    this.selectedKey.set(table.key);
    this.dirty.set(true);
  }

  protected select(key: string): void {
    this.selectedKey.set(key);
  }

  protected setField<K extends 'name' | 'capacity' | 'x' | 'y' | 'width' | 'height'>(field: K, event: Event): void {
    const raw = (event.target as HTMLInputElement).value;
    const value = field === 'name' ? raw : Math.round(Number(raw) || 0);
    this.update({ [field]: value } as Partial<DraftTable>);
  }

  protected setShape(shape: PosTableShape): void {
    this.update({ shape });
  }

  protected setActive(event: Event): void {
    this.update({ isActive: (event.target as HTMLInputElement).checked });
  }

  /** Arrow keys move the focused table; Shift moves it further. */
  protected onTableKey(event: KeyboardEvent, key: string): void {
    const step = event.shiftKey ? BIG_NUDGE : NUDGE;
    const moves: Record<string, [number, number]> = {
      ArrowLeft: [-step, 0],
      ArrowRight: [step, 0],
      ArrowUp: [0, -step],
      ArrowDown: [0, step],
    };
    const move = moves[event.key];
    if (!move) return;

    event.preventDefault();
    this.selectedKey.set(key);
    const table = this.selected()!;
    this.update({ x: table.x + move[0], y: table.y + move[1] });
  }

  protected onPointerDown(event: PointerEvent, key: string): void {
    const table = this.tables().find((t) => t.key === key);
    if (!table || event.button !== 0) return;
    this.endDrag();
    this.selectedKey.set(key);
    this.drag = { key, startX: event.clientX, startY: event.clientY, originX: table.x, originY: table.y, moving: false };
    document.addEventListener('pointermove', this.onDocumentMove);
    document.addEventListener('pointerup', this.onDocumentUp);
    document.addEventListener('pointercancel', this.onDocumentUp);
  }

  private dragTo(event: PointerEvent): void {
    const drag = this.drag;
    const plan = this.plan();
    const canvas = this.canvas()?.nativeElement;
    if (!drag || !plan || !canvas) return;

    const dx = event.clientX - drag.startX;
    const dy = event.clientY - drag.startY;
    if (!drag.moving && Math.hypot(dx, dy) < DRAG_THRESHOLD_PX) return;
    drag.moving = true;

    const scale = plan.canvasWidth / canvas.getBoundingClientRect().width;
    this.update(
      { x: Math.round(drag.originX + dx * scale), y: Math.round(drag.originY + dy * scale) },
      drag.key);
  }

  private endDrag(): void {
    this.drag = null;
    document.removeEventListener('pointermove', this.onDocumentMove);
    document.removeEventListener('pointerup', this.onDocumentUp);
    document.removeEventListener('pointercancel', this.onDocumentUp);
  }

  protected placement(table: DraftTable): Record<string, string> {
    const plan = this.plan()!;
    return {
      '--x': `${(table.x / plan.canvasWidth) * 100}%`,
      '--y': `${(table.y / plan.canvasHeight) * 100}%`,
      '--w': `${(table.width / plan.canvasWidth) * 100}%`,
      '--h': `${(table.height / plan.canvasHeight) * 100}%`,
    };
  }

  protected save(): void {
    const area = this.area();
    if (!area || this.saving()) return;

    this.saving.set(true);
    this.errorMessage.set(null);
    this.noticeMessage.set(null);
    this.restaurantService
      .saveLayout(
        this.organizationId,
        area.id,
        this.tables().map((t) => ({
          id: t.id, name: t.name.trim(), capacity: t.capacity, shape: t.shape, x: t.x, y: t.y,
          width: t.width, height: t.height, isActive: t.isActive,
        })),
      )
      .subscribe({
        next: (plan) => {
          this.saving.set(false);
          this.apply(plan, area.id);
          this.noticeMessage.set(`${area.name} saved.`);
        },
        error: (err: unknown) => {
          this.saving.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not save the layout.');
        },
      });
  }

  /** Keeps a table inside the canvas and at least the minimum size, as the server will. A drag names
   * the table it grabbed; everything else edits the selected one. */
  private update(change: Partial<DraftTable>, tableKey?: string): void {
    const key = tableKey ?? this.selectedKey();
    const plan = this.plan();
    if (!key || !plan) return;

    this.tables.update((list) =>
      list.map((t) => {
        if (t.key !== key) return t;
        const next = { ...t, ...change };
        next.width = Math.min(Math.max(next.width, MIN_SIZE), plan.canvasWidth);
        next.height = Math.min(Math.max(next.height, MIN_SIZE), plan.canvasHeight);
        next.x = Math.min(Math.max(next.x, 0), plan.canvasWidth - next.width);
        next.y = Math.min(Math.max(next.y, 0), plan.canvasHeight - next.height);
        next.capacity = Math.min(Math.max(next.capacity, 1), plan.maxCapacity);
        return next;
      }));
    this.dirty.set(true);
  }

  private apply(plan: PosFloorPlan, areaId?: string): void {
    this.plan.set(plan);
    const keep = areaId ?? this.areaId();
    this.areaId.set(plan.areas.find((a) => a.id === keep)?.id ?? plan.areas[0]?.id ?? null);
    this.loadDrafts();
  }

  private loadDrafts(): void {
    const area = this.area();
    this.tables.set(
      (area?.tables ?? []).map((t) => ({
        key: t.id, id: t.id, name: t.name, capacity: t.capacity, shape: t.shape, x: t.x, y: t.y,
        width: t.width, height: t.height, isActive: t.isActive, isOccupied: t.isOccupied,
      })));
    this.selectedKey.set(null);
    this.dirty.set(false);
  }
}
