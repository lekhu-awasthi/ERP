import { Component, DestroyRef, Injector, afterNextRender, inject, input, output, signal } from '@angular/core';

import { extractErrorMessage } from '../../core/auth/api-error';
import { Contact } from '../../core/contacts/contacts.models';
import { ContactsService } from '../../core/contacts/contacts.service';

/**
 * Phase 72 -- finding a customer at a till: a search box and the first eight matches by name, code or PAN, the
 * walk-in left out (choosing it is each screen's own "Use Walk-in"). Lifted out of the Retail till and the order
 * screen when the bill screen became the third to need it (CLAUDE.md: retire copies 1..N before writing copy
 * N+1). The button that shows it stays each screen's own, because what choosing means differs: a sale's
 * customer, a delivery's address, one bill's buyer.
 *
 * <p><b>Focus goes back to that button</b> (`returnFocusTo`) once a customer is chosen: the chosen result and
 * the panel holding it both disappear, which dropped a keyboard user onto the page itself (found in phase 72's
 * keyboard pass, and true of the two screens this was lifted from since phases 62 and 64).</p>
 */
@Component({
  selector: 'app-pos-customer-search',
  template: `
    <label class="form-label small mb-1" [for]="inputId()">Find a customer by name, code or PAN</label>
    <input class="form-control form-control-sm" type="search" autocomplete="off" [id]="inputId()"
      [value]="term()" (input)="onInput($event)" />
    <ul class="list-unstyled mb-0 mt-1">
      @for (contact of results(); track contact.id) {
        <li>
          <button type="button" class="btn btn-link btn-sm p-0" (click)="choose(contact)">
            {{ contact.name }} ({{ contact.code }})
          </button>
        </li>
      }
    </ul>
  `,
})
export class PosCustomerSearch {
  private readonly contactsService = inject(ContactsService);
  private readonly injector = inject(Injector);

  readonly organizationId = input.required<string>();

  /** The id of the control that opened the search, which takes focus once a customer is chosen. */
  readonly returnFocusTo = input<string | null>(null);

  /** Distinct per screen, so the label names its own input (phase 34a). */
  readonly inputId = input.required<string>();

  /** The walk-in, which a search never offers. */
  readonly excludeId = input<string | null>(null);

  readonly chosen = output<Contact>();

  /** A search that failed, for the screen's own message line. */
  readonly failed = output<string>();

  protected readonly term = signal('');
  protected readonly results = signal<Contact[]>([]);
  private timer: ReturnType<typeof setTimeout> | null = null;

  constructor() {
    inject(DestroyRef).onDestroy(() => {
      if (this.timer) clearTimeout(this.timer);
    });
  }

  protected onInput(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.term.set(value);
    const search = value.trim();
    if (this.timer) clearTimeout(this.timer);
    if (!search) {
      this.results.set([]);
      return;
    }

    this.timer = setTimeout(() => {
      this.contactsService.listContacts(this.organizationId(), 'Customer', 1, 8, { search }).subscribe({
        next: (result) => this.results.set(result.items.filter((x) => x.id !== this.excludeId())),
        error: (err: unknown) => this.failed.emit(extractErrorMessage(err) ?? 'Could not search customers.'),
      });
    }, 250);
  }

  protected choose(contact: Contact): void {
    this.results.set([]);
    this.term.set('');
    this.chosen.emit(contact);

    const opener = this.returnFocusTo();
    if (opener) {
      afterNextRender(() => document.getElementById(opener)?.focus(), { injector: this.injector });
    }
  }
}
