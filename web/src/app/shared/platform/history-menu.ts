import { Component, ElementRef, HostListener, inject, signal } from '@angular/core';
import { Router } from '@angular/router';

import { HistoryService } from '../navigation/history.service';

/**
 * Phase 33 — the top bar's History popover, left of the search box exactly as the reference product
 * places it.
 *
 * Everything it renders comes from {@link HistoryService}, which is browser storage: no request is
 * made to open it and none was made to fill it. See that service for why, and for why each row shows
 * a *screen* rather than a record.
 *
 * Driven from a signal rather than `data-bs-toggle`: Bootstrap's JavaScript is not loaded anywhere in
 * this app (`angular.json` has no `scripts`), so the attribute would do nothing — phase-22's gotcha.
 */
@Component({
  selector: 'app-history-menu',
  templateUrl: './history-menu.html',
  styleUrl: './history-menu.scss',
})
export class HistoryMenu {
  private readonly history = inject(HistoryService);
  private readonly router = inject(Router);
  private readonly host = inject(ElementRef<HTMLElement>);

  protected readonly open = signal(false);
  protected readonly entries = this.history.entries;

  protected toggle(): void {
    this.open.update((x) => !x);
  }

  protected go(url: string): void {
    this.open.set(false);
    void this.router.navigateByUrl(url);
  }

  protected clear(): void {
    this.history.clear();
    this.open.set(false);
  }

  @HostListener('document:click', ['$event'])
  protected onDocumentClick(event: MouseEvent): void {
    if (this.open() && !this.host.nativeElement.contains(event.target as Node)) {
      this.open.set(false);
    }
  }
}
