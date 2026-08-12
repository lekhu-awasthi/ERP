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
  {
    path: 'organizations/:id/configuration',
    loadComponent: () =>
      import('./features/configuration/configuration-shell/configuration-shell').then((m) => m.ConfigurationShell),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/configuration/credit-terms',
    loadComponent: () =>
      import('./features/configuration/credit-term-list-page/credit-term-list-page').then(
        (m) => m.CreditTermListPage,
      ),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/configuration/payment-modes',
    loadComponent: () =>
      import('./features/configuration/payment-mode-list-page/payment-mode-list-page').then(
        (m) => m.PaymentModeListPage,
      ),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/contacts',
    loadComponent: () =>
      import('./features/contacts/contact-list-page/contact-list-page').then((m) => m.ContactListPage),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/contacts/groups',
    loadComponent: () =>
      import('./features/contacts/contact-group-list-page/contact-group-list-page').then(
        (m) => m.ContactGroupListPage,
      ),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/contacts/:contactId',
    loadComponent: () =>
      import('./features/contacts/contact-detail-page/contact-detail-page').then((m) => m.ContactDetailPage),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/products',
    loadComponent: () =>
      import('./features/catalog/product-list-page/product-list-page').then((m) => m.ProductListPage),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/products/categories',
    loadComponent: () =>
      import('./features/catalog/product-category-list-page/product-category-list-page').then(
        (m) => m.ProductCategoryListPage,
      ),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/products/units',
    loadComponent: () =>
      import('./features/catalog/unit-of-measurement-list-page/unit-of-measurement-list-page').then(
        (m) => m.UnitOfMeasurementListPage,
      ),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/products/:productId',
    loadComponent: () =>
      import('./features/catalog/product-detail-page/product-detail-page').then((m) => m.ProductDetailPage),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/accounting/account-groups',
    loadComponent: () =>
      import('./features/accounting/account-group-list-page/account-group-list-page').then(
        (m) => m.AccountGroupListPage,
      ),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/accounting/accounts',
    loadComponent: () =>
      import('./features/accounting/account-list-page/account-list-page').then((m) => m.AccountListPage),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/accounting/journal-vouchers',
    loadComponent: () =>
      import('./features/accounting/journal-voucher-list-page/journal-voucher-list-page').then(
        (m) => m.JournalVoucherListPage,
      ),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/accounting/journal-vouchers/:journalVoucherId',
    loadComponent: () =>
      import('./features/accounting/journal-voucher-detail-page/journal-voucher-detail-page').then(
        (m) => m.JournalVoucherDetailPage,
      ),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/accounting/cash-transfers',
    loadComponent: () =>
      import('./features/accounting/cash-transfer-list-page/cash-transfer-list-page').then(
        (m) => m.CashTransferListPage,
      ),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/accounting/cash-transfers/:cashTransferId',
    loadComponent: () =>
      import('./features/accounting/cash-transfer-detail-page/cash-transfer-detail-page').then(
        (m) => m.CashTransferDetailPage,
      ),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/accounting/defaults',
    loadComponent: () =>
      import('./features/accounting/accounting-defaults-page/accounting-defaults-page').then(
        (m) => m.AccountingDefaultsPage,
      ),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/warehouses',
    loadComponent: () =>
      import('./features/organizations/warehouse-list-page/warehouse-list-page').then((m) => m.WarehouseListPage),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/sales/quotations',
    loadComponent: () =>
      import('./features/sales/quotation-list-page/quotation-list-page').then((m) => m.QuotationListPage),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/sales/quotations/:quotationId',
    loadComponent: () =>
      import('./features/sales/quotation-detail-page/quotation-detail-page').then((m) => m.QuotationDetailPage),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/sales/invoices',
    loadComponent: () =>
      import('./features/sales/invoice-list-page/invoice-list-page').then((m) => m.InvoiceListPage),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/sales/invoices/:invoiceId',
    loadComponent: () =>
      import('./features/sales/invoice-detail-page/invoice-detail-page').then((m) => m.InvoiceDetailPage),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/sales/credit-notes',
    loadComponent: () =>
      import('./features/sales/credit-note-list-page/credit-note-list-page').then((m) => m.CreditNoteListPage),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/sales/credit-notes/:creditNoteId',
    loadComponent: () =>
      import('./features/sales/credit-note-detail-page/credit-note-detail-page').then((m) => m.CreditNoteDetailPage),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/payments',
    loadComponent: () =>
      import('./features/sales/payment-list-page/payment-list-page').then((m) => m.PaymentListPage),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/payments/:paymentId',
    loadComponent: () =>
      import('./features/sales/payment-detail-page/payment-detail-page').then((m) => m.PaymentDetailPage),
    canActivate: [authGuard],
  },
];
