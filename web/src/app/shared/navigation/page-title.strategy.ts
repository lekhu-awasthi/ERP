import { Injectable, inject } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { RouterStateSnapshot, TitleStrategy } from '@angular/router';

import { areaOf, titleOf } from './navigation-catalog';

/** Suffixed onto every page title, so a tab is identifiable as this app among a row of tabs. */
export const APP_NAME = 'ErpApp';

/**
 * Phase 34a — WCAG 2.1 **2.4.2 Page Titled (Level A)**: every page needs a title that describes its
 * topic or purpose.
 *
 * Before this the whole app was `<title>Web</title>`, the Angular CLI's scaffold default, on all
 * hundred-and-forty-one routes. For a sighted user that is a cosmetic annoyance; for a screen-reader
 * user the title is the *first thing announced on every navigation*, and in a single-page app it is
 * often the only announcement a route change makes at all — so a hundred and forty-one screens were
 * indistinguishable from each other and from any other tab.
 *
 * <b>Derived from the router, not written down.</b> Adding `title:` to every route definition would
 * work, and would be a hundred and forty-one places to keep in step with the eleven-entry override
 * table `NavigationCatalog` already maintains for exactly the same strings. Phase 33's lesson (d)
 * applies unchanged: the router *is* the rule, so this reuses `titleOf`/`areaOf` and a route added
 * by a later phase gets a correct title with no edit here. A route may still set an explicit `title`
 * when the derivation reads badly; that wins.
 *
 * <b>Detail routes.</b> `sales/invoices/:invoiceId` titles as its list — "Invoices" — because the id
 * segment carries no words. That is the same compromise the reference product makes in its own
 * History list (phase 33 confirmed it live: a contact detail page records as "CRM · Contacts"), and
 * the record's own identity is in the `<h1>` a screen reader reaches immediately after.
 */
@Injectable({ providedIn: 'root' })
export class PageTitleStrategy extends TitleStrategy {
  private readonly title = inject(Title);

  override updateTitle(snapshot: RouterStateSnapshot): void {
    this.title.setTitle(this.buildTitle(snapshot) ?? titleForUrl(snapshot.url));
  }
}

/** The org prefix, matched on the *resolved* url where `:id` has become a real guid. */
const ORG_URL = /^\/organizations\/[^/]+\//;

/** Titles for the screens outside any organization, whose paths do not name an area. */
const OUTSIDE_ORGANIZATION: Readonly<Record<string, string>> = {
  login: 'Sign In',
  register: 'Create an Account',
  'verify-email': 'Verify Your Email',
  'forgot-password': 'Forgot Password',
  'reset-password': 'Reset Password',
  organizations: 'Your Organizations',
  'organizations/new': 'New Organization',
};

/**
 * The document title for a resolved url. Exported so the guard spec can assert every route in the
 * config produces a distinct, non-default title without booting a router.
 */
export function titleForUrl(url: string): string {
  const path = url.split('?')[0].split('#')[0].replace(/^\/+|\/+$/g, '');

  if (!ORG_URL.test(`/${path}/`)) {
    const named = OUTSIDE_ORGANIZATION[path];
    return named ? `${named} · ${APP_NAME}` : APP_NAME;
  }

  const inside = path.replace(/^organizations\/[^/]+\//, '');

  // Drop trailing id segments (a guid, or the literal `new` this codebase uses for a create form),
  // so a record page titles as its screen.
  const segments = inside.split('/').filter((s) => s.length > 0);
  while (segments.length > 1 && isIdSegment(segments.at(-1)!)) {
    segments.pop();
  }

  const screen = segments.join('/');
  const creating = inside.endsWith('/new');
  const name = creating ? `New ${singular(titleOf(screen))}` : titleOf(screen);

  return `${name} · ${areaOf(screen)} · ${APP_NAME}`;
}

/** A guid, the literal `new`, or a sub-tab like `overview` that follows an id. */
function isIdSegment(segment: string): boolean {
  return segment === 'new' || /^[0-9a-f]{8}-[0-9a-f]{4}-/i.test(segment);
}

/** "Invoices" → "Invoice", so a create form reads "New Invoice" rather than "New Invoices". */
function singular(name: string): string {
  if (name.endsWith('ies')) {
    return `${name.slice(0, -3)}y`;
  }
  return name.endsWith('s') && !name.endsWith('ss') ? name.slice(0, -1) : name;
}
