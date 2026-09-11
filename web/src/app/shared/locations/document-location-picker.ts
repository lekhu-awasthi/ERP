import { Component, computed, effect, inject, input, model, output, untracked } from '@angular/core';

import { BillingLocationStore } from './billing-location-store';

/**
 * Phase 35a -- the billing location picker that sits in a document form's **header**, immediately
 * left of Save, rendering `Name (Code)`.
 *
 * <p>Phase 32 built this inline on the Invoice form after reading it live on a location-enabled
 * tenant; the 2026-09-10 pass confirmed the same control on the Journal Voucher form, i.e. it is
 * not a sales-module control but the header of every document type the tenant's scope covers. This
 * is that markup extracted, so the remaining fifteen forms get it without a sixteenth copy of the
 * two service calls behind it -- and so a later phase adding a document type gets the control by
 * adding one tag.</p>
 *
 * <p><b>It renders nothing at all</b> when the tenant's `LocationScopeMode` excludes this document
 * type, or when the tenant has no locations, which is exactly what a tenant without the entitlement
 * sees. So every form can carry the tag unconditionally.</p>
 *
 * <p><b>`[selected]` per option, never `[value]` on the select</b> -- the phase-5/6/7 native-select
 * race, which persisted wrong ids rather than merely displaying them.</p>
 */
@Component({
  selector: 'app-document-location-picker',
  imports: [],
  template: `
    @if (visible()) {
      <select
        class="form-select form-select-sm w-auto"
        aria-label="Billing location"
        [disabled]="disabled()"
        (change)="locationId.set($any($event.target).value)"
      >
        @for (location of locations(); track location.id) {
          <option [value]="location.id" [selected]="locationId() === location.id">
            {{ location.name }} ({{ location.code }})
          </option>
        }
      </select>
    }
  `,
})
export class DocumentLocationPicker {
  private readonly store = inject(BillingLocationStore);

  readonly organizationId = input.required<string>();

  /** The `DocumentType` member name this form writes -- `'Invoice'`, `'JournalVoucher'`, … The
   * server's `locationBearingDocumentTypes` is matched against it by name, never by position. */
  readonly documentType = input.required<string>();

  readonly disabled = input(false);

  /** Empty means "the caller has not chosen one", which the server's `LocationResolver` turns into
   * the tenant's HeadOffice. The picker fills it in as soon as the list loads so the select never
   * shows a location it is not actually sending. */
  readonly locationId = model<string>('');

  /**
   * The default warehouse of the location this picker first resolved, emitted once.
   *
   * <p><b>Once, and only on the way in.</b> `BillingLocation.WarehouseId` is a *default*: the live
   * Add New Location dialog labels that field `Select Default Warehouse` and requires it, and
   * switching a document's location on an open form leaves its Warehouse untouched -- observed
   * 2026-09-10 against a location whose warehouse is not set at all, where the form kept the
   * warehouse it already had. So this is a prefill, never a constraint and never reactive; a host
   * form uses it only when it is creating a document and has no warehouse yet.</p>
   */
  readonly defaultWarehouseId = output<string>();

  protected readonly locations = computed(() =>
    this.store.locations(this.organizationId())().filter((x) => x.isActive));

  protected readonly visible = computed(() =>
    this.store.applies(this.organizationId(), this.documentType()) && this.locations().length > 0);

  private emitted = false;

  constructor() {
    effect(() => {
      const locations = this.locations();
      if (locations.length === 0) {
        return;
      }

      // untracked: this effect reacts to the list arriving, not to the user picking. Reading the
      // model tracked would re-run it on every change and re-emit the prefill (phase-34b's rule --
      // an effect cannot tell "already being acted on" from "needs action").
      const chosen = untracked(() => this.locationId())
        || locations.find((x) => x.isHeadOffice)?.id
        || locations[0].id;

      untracked(() => this.locationId.set(chosen));

      if (this.emitted) {
        return;
      }

      this.emitted = true;
      const warehouseId = locations.find((x) => x.id === chosen)?.warehouseId;
      if (warehouseId) {
        this.defaultWarehouseId.emit(warehouseId);
      }
    });
  }
}
