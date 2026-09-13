import { Component, inject } from '@angular/core';
import { ActivatedRoute } from '@angular/router';

import { DealList } from '../deal-list/deal-list';

/**
 * Phase 39 — `CRM > Deals` as a screen of its own, closing phase 34b's carried item #6.
 *
 * <b>Why it was missing and why it is a page wrapper.</b> `DealList` has served two hosts since
 * phase 15 — the Contact detail page's Deals tab and the dashboard's Deals card — and neither is a
 * route. Phase 34b's router-derived nav therefore could not show Deals at all, which is what made a
 * gap nobody had noticed for twenty-four phases visible in a day. The fix is a route, not a
 * component: the live standalone list is the same three-tab table with the same inline create form,
 * so a second implementation would have been a second thing to keep in step.
 *
 * <b>Deals has a detail page in the reference product</b> — left rail, Overview / Contact Personnel
 * / Tasks tabs, a Documents dropzone and an Activity composer, all read live on 2026-09-13. That is
 * phase-27a's mechanism sweep over a parent type this codebase does not have, and it is a carried
 * item rather than something this phase quietly half-built; see docs/phase-39-status.md, Decision F.
 */
@Component({
  selector: 'app-deal-list-page',
  imports: [DealList],
  templateUrl: './deal-list-page.html',
})
export class DealListPage {
  private readonly route = inject(ActivatedRoute);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;
}
