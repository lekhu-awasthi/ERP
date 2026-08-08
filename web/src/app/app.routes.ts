import { Routes } from '@angular/router';

import { authGuard } from './core/auth/auth.guard';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'login' },
  {
    path: 'register',
    loadComponent: () => import('./features/auth/register-page/register-page').then((m) => m.RegisterPage),
  },
  {
    path: 'verify-email',
    loadComponent: () =>
      import('./features/auth/verify-email-page/verify-email-page').then((m) => m.VerifyEmailPage),
  },
  {
    path: 'login',
    loadComponent: () => import('./features/auth/login-page/login-page').then((m) => m.LoginPage),
  },
  {
    path: 'forgot-password',
    loadComponent: () =>
      import('./features/auth/forgot-password-page/forgot-password-page').then((m) => m.ForgotPasswordPage),
  },
  {
    path: 'reset-password',
    loadComponent: () =>
      import('./features/auth/reset-password-page/reset-password-page').then((m) => m.ResetPasswordPage),
  },
  {
    path: 'organizations',
    loadComponent: () =>
      import('./features/organizations/organization-list-page/organization-list-page').then(
        (m) => m.OrganizationListPage,
      ),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/new',
    loadComponent: () =>
      import('./features/organizations/new-organization-wizard/new-organization-wizard').then(
        (m) => m.NewOrganizationWizard,
      ),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/welcome',
    loadComponent: () =>
      import('./features/organizations/welcome-page/welcome-page').then((m) => m.WelcomePage),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id',
    loadComponent: () =>
      import('./features/organizations/organization-dashboard-page/organization-dashboard-page').then(
        (m) => m.OrganizationDashboardPage,
      ),
    canActivate: [authGuard],
  },
];
