import { ChangeDetectionStrategy, Component, Signal, WritableSignal, computed, input } from '@angular/core';

/**
 * Phase 47 (phase 40 carried item #2) — <b>a validation failure that names its field, at the field.</b>
 *
 * <h4>What was there before</h4>
 *
 * Every document form in this app validated the same way: `if (!this.contactId()) { errorMessage.set('Select a
 * Customer.'); return; }`. Phase 40 made that message *announce* — `app-status-banner` is a live region
 * that already exists when the text arrives — so a screen-reader user hears "Select a Customer" the
 * moment Save fails. What they then have to do is find the Customer field, by walking the form, with
 * nothing on the control saying it is the one at fault. `aria-invalid` and `aria-describedby`
 * appeared zero times outside the five auth forms.
 *
 * <h4>Why this is a class the page owns and not a service</h4>
 *
 * The same reason `ListFilter` is: two detail pages can be alive at once while a route is torn down,
 * and a root-provided service would share one field state between them. It also takes the page's own
 * `errorMessage` signal rather than owning a second one, which is what keeps the banner and the field
 * from ever disagreeing — see {@link active}.
 *
 * <h4>Keyed by the control's DOM id</h4>
 *
 * Every control on every document form already carries a stable id (`invoice-detail-page-customer`),
 * because phase 34a's sweep made each one nameable by its `<label for>`. Keying on that id rather
 * than on a field name invented here means the message element's own id is derivable
 * (`…-customer-error`), the `aria-describedby` that points at it is derivable, and the guard in
 * `a11y-sweep-guard.spec.ts` can check both ends against the template — which is the mirror question
 * phase 34a asked of labels (*which labels name no control?*), asked of messages.
 */
export class FieldError {
  /**
   * The control the *current* message belongs to, or null.
   *
   * <p>Derived rather than stored, and that is the whole trick: the page clears and re-sets
   * `errorMessage` freely — on every save attempt, and for server errors this class knows nothing
   * about — so a stored flag would leave a field painted red under a message about something else.
   * Comparing the text this class last set against what the banner is currently showing means the
   * field marking can only be on while the message that put it there is.</p>
   */
  private readonly active: Signal<string | null>;

  private owner: { control: string; message: string } | null = null;

  constructor(private readonly errorMessage: WritableSignal<string | null>) {
    this.active = computed(() => {
      // The signal is read FIRST and unconditionally, and that is load-bearing. A `computed()`
      // records as its dependencies only the signals it actually read on the evaluation it is
      // memoizing — so `owner !== null && this.errorMessage() === …` records *nothing* on a form
      // that has not failed yet, because `&&` short-circuits before the signal is reached. The
      // computed is then frozen at null for the life of the page, and no later `fail()` can wake
      // it. Every unit test passed, because each one called `fail()` before reading; only opening
      // the form and pressing Save with an empty field shows it.
      const message = this.errorMessage();
      const owner = this.owner;

      return owner !== null && message === owner.message ? owner.control : null;
    });
  }

  /**
   * Fails the form at one control: the banner announces (unchanged behaviour), the control is marked
   * invalid and described by the message, and focus moves there.
   *
   * <p>Moving focus is the part a banner alone cannot do. WCAG 3.3.1 is satisfied by the
   * announcement; what makes the announcement *actionable* is landing on the field it names, which
   * for a sighted keyboard user is the difference between one keystroke and twenty.</p>
   */
  fail(controlId: string, message: string): void {
    this.owner = { control: controlId, message };
    this.errorMessage.set(message);

    // After this change-detection pass, so the control is rendered and marked before it is focused.
    queueMicrotask(() => document.getElementById(controlId)?.focus());
  }

  /** True when this control is the one the current message is about. */
  is(controlId: string): boolean {
    return this.active() === controlId;
  }

  /** `'true'` or null — never `'false'`, which is a claim that the field has been checked and is fine. */
  invalid(controlId: string): 'true' | null {
    return this.is(controlId) ? 'true' : null;
  }

  /**
   * The id of this control's message element, or null when there is no message.
   *
   * <p>Null rather than always-the-id, because an `aria-describedby` pointing at an element that is
   * not rendered describes nothing and some screen readers announce the dangling reference. This is
   * the same rule `bs-date-input` already follows for its own notes.</p>
   */
  describedBy(controlId: string): string | null {
    return this.is(controlId) ? errorIdFor(controlId) : null;
  }

  /** The message itself, for the element the id above points at. */
  messageFor(controlId: string): string | null {
    return this.is(controlId) ? this.errorMessage() : null;
  }
}

export function errorIdFor(controlId: string): string {
  return `${controlId}-error`;
}

/**
 * Phase 47 — the message element `aria-describedby` points at.
 *
 * <p>A component rather than four lines of markup per field, so the id it renders and the id
 * {@link FieldError.describedBy} returns are derived from the same function and cannot drift. It
 * renders nothing at all when the field is not the one at fault, which is why `describedBy` returns
 * null in that case.</p>
 *
 * <p>`d-block` because Bootstrap's `.invalid-feedback` is `display: none` until a sibling carries
 * `.is-invalid`, and the control is not always this element's sibling in these layouts.</p>
 */
@Component({
  selector: 'app-field-error',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (state().messageFor(control()); as message) {
      <div class="invalid-feedback d-block" [id]="errorId()">{{ message }}</div>
    }
  `,
})
export class FieldErrorMessage {
  readonly state = input.required<FieldError>();
  /** The DOM id of the control this message belongs to. */
  readonly control = input.required<string>();

  protected readonly errorId = computed(() => errorIdFor(this.control()));
}
