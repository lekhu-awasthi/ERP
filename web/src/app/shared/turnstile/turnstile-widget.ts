import { AfterViewInit, Component, ElementRef, OnDestroy, output, signal, viewChild } from '@angular/core';

import { environment } from '../../../environments/environment';

interface TurnstileRenderOptions {
  sitekey: string;
  callback: (token: string) => void;
  'expired-callback'?: () => void;
  'error-callback'?: () => void;
}

declare global {
  interface Window {
    turnstile?: {
      render(container: HTMLElement, options: TurnstileRenderOptions): string;
      reset(widgetId?: string): void;
      remove(widgetId?: string): void;
    };
  }
}

/**
 * Cloudflare Turnstile bot-check widget (roadmap Phase 20g / FR-1.1, the Phase 1 registration-
 * hardening deferral). The api.js script tag lives in index.html as async/defer, so
 * window.turnstile may not exist yet when this component mounts -- rendering is deferred until it
 * appears (short poll, capped) rather than assumed ready.
 */
@Component({
  selector: 'app-turnstile-widget',
  imports: [],
  templateUrl: './turnstile-widget.html',
})
export class TurnstileWidget implements AfterViewInit, OnDestroy {
  private readonly container = viewChild.required<ElementRef<HTMLDivElement>>('container');

  readonly verified = output<string>();
  readonly expired = output<void>();
  readonly failed = output<void>();

  protected readonly loadFailed = signal(false);

  private widgetId: string | undefined;
  private pollHandle: ReturnType<typeof setTimeout> | undefined;
  private pollAttempts = 0;
  private static readonly maxPollAttempts = 100; // ~10s at 100ms

  ngAfterViewInit(): void {
    this.waitForTurnstile();
  }

  ngOnDestroy(): void {
    clearTimeout(this.pollHandle);
    if (this.widgetId !== undefined) {
      window.turnstile?.remove(this.widgetId);
    }
  }

  /** Cloudflare tokens are single-use -- call this after a failed submit to get a fresh one. */
  reset(): void {
    if (this.widgetId !== undefined) {
      window.turnstile?.reset(this.widgetId);
    }
  }

  private waitForTurnstile(): void {
    if (window.turnstile) {
      this.render();
      return;
    }

    if (this.pollAttempts++ >= TurnstileWidget.maxPollAttempts) {
      this.loadFailed.set(true);
      return;
    }

    this.pollHandle = setTimeout(() => this.waitForTurnstile(), 100);
  }

  private render(): void {
    this.widgetId = window.turnstile!.render(this.container().nativeElement, {
      sitekey: environment.turnstileSiteKey,
      callback: (token) => this.verified.emit(token),
      'expired-callback': () => this.expired.emit(),
      'error-callback': () => this.failed.emit(),
    });
  }
}
