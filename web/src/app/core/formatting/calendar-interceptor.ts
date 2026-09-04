import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';

import { environment } from '../../../environments/environment';
import { DatePreferenceService } from '../../shared/formatting/date-preference';

/**
 * Phase 27b -- sends the user's calendar preference to the API so that server-rendered output can
 * honour it.
 *
 * <p>Phase 23 deliberately kept the AD/BS choice on the client (`DatePreferenceService`, browser
 * storage) and converted at the render edge, and recorded the cost in its Decision A: the print/PDF
 * pipeline and every `.xlsx` export format their dates on the server, "so they remain AD regardless
 * of the user's setting". This interceptor is the whole fix. One header on every API request means
 * every existing download route -- and every one a later phase adds -- gets the preference for
 * free, where a query parameter would have had to be threaded through some forty call sites.</p>
 *
 * <p>Only requests to this app's own API carry the header: an absolute URL to somewhere else must
 * not learn anything about the user, and nothing else would know what to do with it anyway.</p>
 */
export const calendarInterceptor: HttpInterceptorFn = (req, next) => {
  if (!req.url.startsWith(environment.apiBaseUrl)) {
    return next(req);
  }

  return next(req.clone({ setHeaders: { 'X-Calendar': inject(DatePreferenceService).format() } }));
};
