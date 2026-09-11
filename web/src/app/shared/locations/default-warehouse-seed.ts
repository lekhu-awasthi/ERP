import { Signal, WritableSignal } from '@angular/core';

/** Anything with an id -- the host's own `Warehouse[]`, without this file needing to know it. */
interface HasId {
  readonly id: string;
}

export interface DefaultWarehouseSeed {
  /** Take the location's default warehouse, from the picker's one-shot output. */
  offer(warehouseId: string): void;

  /** Re-attempt a seed that arrived before the warehouse list did. */
  retry(): void;
}

/**
 * Phase 35a (Decision D) -- seed a document's Warehouse from its billing location's **default**,
 * once, without overwriting a warehouse that is already chosen.
 *
 * <p>Confirm-live 2026-09-10: the Add New Location dialog's required fourth field is `Warehouse *`
 * with the placeholder **`Select Default Warehouse`**, and switching a document's location on an
 * open form leaves its Warehouse untouched — tested against a location with no warehouse at all,
 * where the form kept the one it had. So the relationship is a prefill: not a constraint on which
 * warehouse may be picked, and not reactive to a later change of location.</p>
 *
 * <p><b>Why it needs a `retry`.</b> Two independent requests feed it — the tenant's warehouses,
 * issued by the host in its constructor, and its billing locations, issued by the picker on first
 * render. Seeding an id the Warehouse `<select>` has no `<option>` for is the phase-5 native-select
 * race, so the seed waits for the list; and a seed that simply gave up when the list had not
 * arrived would work on a fast machine and not on a slow one. The host calls {@link offer} from the
 * picker's output and {@link retry} when its warehouse list lands, and whichever is second wins.</p>
 */
export function defaultWarehouseSeed(
  warehouseId: WritableSignal<string>,
  warehouses: Signal<readonly HasId[]>,
): DefaultWarehouseSeed {
  let pending: string | null = null;

  const flush = (): void => {
    if (pending === null || warehouseId()) {
      return;
    }

    if (!warehouses().some((x) => x.id === pending)) {
      return;
    }

    warehouseId.set(pending);
    pending = null;
  };

  return {
    offer(id: string): void {
      pending = id;
      flush();
    },
    retry(): void {
      flush();
    },
  };
}
