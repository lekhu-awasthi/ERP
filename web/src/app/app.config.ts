import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { TitleStrategy, provideRouter } from '@angular/router';

import { routes } from './app.routes';
import { calendarInterceptor } from './core/formatting/calendar-interceptor';
import { PageTitleStrategy } from './shared/navigation/page-title.strategy';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    provideHttpClient(withInterceptors([calendarInterceptor])),
    // Phase 34a -- WCAG 2.4.2. Derives each page's title from the route rather than from a `title:`
    // on all 141 route definitions; see PageTitleStrategy for why.
    { provide: TitleStrategy, useClass: PageTitleStrategy },
  ],
};
