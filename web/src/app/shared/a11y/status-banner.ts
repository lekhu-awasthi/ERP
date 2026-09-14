import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

/** The four tones the app's banners come in, and what each one means to assistive technology. */
export type StatusBannerTone = 'danger' | 'success' | 'warning' | 'info';

/**
 * Phase 40 — <b>the app's one status message</b>, and the phase's `aria-live` policy made into
 * code rather than a paragraph (phase 34a's carried item #5).
 *
 * <h4>The defect this exists to fix</h4>
 *
 * The app had 163 status banners in 145 templates, and 157 of them were spelled exactly like this:
 *
 * <pre>
 *   &#64;if (errorMessage()) {
 *     &lt;div class="alert alert-danger …" role="alert"&gt;…{{ errorMessage() }}…&lt;/div&gt;
 *   }
 * </pre>
 *
 * which announces nothing. A live region is announced when its <i>contents change</i>; ARIA
 * requires the region to already be in the accessibility tree when that happens, because what the
 * screen reader is watching is the region, not the document. An element inserted into the DOM with
 * its text already inside it is one mutation, not two, and there was no region to watch beforehand.
 * Confirmed against the running app on 2026-09-14: a MutationObserver on the New Invoice form
 * recorded exactly one event when Save failed — <i>live region ADDED to DOM</i>, already carrying
 * "Select a Customer." — and never a change inside an existing region.
 *
 * So the region moves outside the `&#64;if`. This component's host is always rendered; only the
 * visible alert box inside it comes and goes, which is a change to a region that was already being
 * watched. That is the entire fix, and it is why call sites must render `&lt;app-status-banner&gt;`
 * unconditionally rather than wrapping it in the `&#64;if` they used to have.
 *
 * <h4>The policy</h4>
 *
 * One decision applied everywhere, not a judgement per screen:
 *
 * <ul>
 *   <li><b>A failure interrupts</b> — `role="alert"` / `aria-live="assertive"`. The user's action
 *       did not happen; they must not type another field first.</li>
 *   <li><b>A success or a notice waits</b> — `role="status"` / `aria-live="polite"`. It is
 *       confirmation, not a correction, and cutting off whatever is being read to say "Saved" is
 *       worse than saying it a moment later.</li>
 *   <li><b>`aria-atomic="true"`</b> on both, so a changed message is read whole. Without it a
 *       screen reader may read only the words that differ from the previous message, which for two
 *       validation errors that share a prefix is gibberish.</li>
 *   <li><b>Progress is not a status message.</b> A spinner announces nothing: 4.1.3 is about the
 *       <i>result</i> of an action, and "loading" repeated on every keystroke of a search box is
 *       noise that drowns the result when it arrives. The `aria-live` regions that pre-date this
 *       component are the two that follow the same rule — a result <i>count</i> (`list-chrome`,
 *       `global-search`), never a request in flight.</li>
 * </ul>
 *
 * An empty host is an empty inline box with no styling, and every one of the 163 call sites sits
 * directly inside a plain block container (verified before the sweep, precisely because an empty
 * child of a `d-flex … gap-*` parent would have added a phantom gap on 163 screens).
 */
@Component({
  selector: 'app-status-banner',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div [attr.role]="role()" [attr.aria-live]="politeness()" aria-atomic="true">
      @if (shown()) {
        <div
          class="alert alert-{{ tone() }} d-flex {{ align() }} gap-2 {{ spacing() }}"
          [class.small]="small()"
          [class.justify-content-between]="spread()"
        >
          <div class="d-flex {{ align() }} gap-2">
            <i aria-hidden="true" class="bi {{ icon() }} flex-shrink-0"></i>
            @if (message()) {
              <div>{{ message() }}</div>
            }
            <ng-content />
          </div>
          <ng-content select="[bannerAction]" />
        </div>
      }
    </div>
  `,
})
export class StatusBanner {
  /** The message to show, or null/'' for "nothing has happened yet". */
  readonly message = input<string | null | undefined>(null);

  readonly tone = input<StatusBannerTone>('danger');

  /** The bottom margin utility the call site used, so the sweep does not move anything on screen. */
  readonly spacing = input('mb-4');

  /** A handful of call sites render a denser banner inside a panel. */
  readonly small = input(false);

  /** `align-items-center` for one line, `align-items-start` for a paragraph. */
  readonly align = input<'align-items-center' | 'align-items-start'>('align-items-center');

  /**
   * Show the box when the call site's content is richer than a string.
   *
   * <p>Two banners in the app are not "a message": the SMS send result is a sentence assembled from
   * three counts, and the Production Journal's stock warning carries an <i>Approve anyway</i> button
   * beside it. Both project their own content and say when it exists. Everything else leaves this
   * alone, and the presence of `message` is the answer.</p>
   */
  readonly visible = input<boolean | null>(null);

  /** Push a projected `[bannerAction]` control to the far end of the box. */
  readonly spread = input(false);

  protected readonly shown = computed(() => this.visible() ?? !!this.message());

  protected readonly role = computed(() => (this.interrupts() ? 'alert' : 'status'));
  protected readonly politeness = computed(() => (this.interrupts() ? 'assertive' : 'polite'));

  private readonly interrupts = computed(() => this.tone() === 'danger' || this.tone() === 'warning');

  protected readonly icon = computed(() => {
    switch (this.tone()) {
      case 'success':
        return 'bi-check-circle-fill';
      case 'warning':
        return 'bi-exclamation-circle-fill';
      case 'info':
        return 'bi-info-circle-fill';
      default:
        return 'bi-exclamation-triangle-fill';
    }
  });
}
