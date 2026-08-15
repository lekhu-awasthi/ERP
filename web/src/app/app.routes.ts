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
    path: 'organizations/:id/configuration/tds-types',
    loadComponent: () =>
      import('./features/configuration/tds-type-list-page/tds-type-list-page').then((m) => m.TdsTypeListPage),
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
  {
    path: 'organizations/:id/purchasing/purchase-orders',
    loadComponent: () =>
      import('./features/purchasing/purchase-order-list-page/purchase-order-list-page').then(
        (m) => m.PurchaseOrderListPage,
      ),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/purchasing/purchase-orders/:purchaseOrderId',
    loadComponent: () =>
      import('./features/purchasing/purchase-order-detail-page/purchase-order-detail-page').then(
        (m) => m.PurchaseOrderDetailPage,
      ),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/purchasing/purchase-bills',
    loadComponent: () =>
      import('./features/purchasing/purchase-bill-list-page/purchase-bill-list-page').then(
        (m) => m.PurchaseBillListPage,
      ),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/purchasing/purchase-bills/:purchaseBillId',
    loadComponent: () =>
      import('./features/purchasing/purchase-bill-detail-page/purchase-bill-detail-page').then(
        (m) => m.PurchaseBillDetailPage,
      ),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/purchasing/expenses',
    loadComponent: () =>
      import('./features/purchasing/expense-list-page/expense-list-page').then((m) => m.ExpenseListPage),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/purchasing/expenses/:expenseId',
    loadComponent: () =>
      import('./features/purchasing/expense-detail-page/expense-detail-page').then((m) => m.ExpenseDetailPage),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/purchasing/debit-notes',
    loadComponent: () =>
      import('./features/purchasing/debit-note-list-page/debit-note-list-page').then((m) => m.DebitNoteListPage),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/purchasing/debit-notes/:debitNoteId',
    loadComponent: () =>
      import('./features/purchasing/debit-note-detail-page/debit-note-detail-page').then(
        (m) => m.DebitNoteDetailPage,
      ),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/purchasing/supplier-payments',
    loadComponent: () =>
      import('./features/purchasing/supplier-payment-list-page/supplier-payment-list-page').then(
        (m) => m.SupplierPaymentListPage,
      ),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/purchasing/supplier-payments/:supplierPaymentId',
    loadComponent: () =>
      import('./features/purchasing/supplier-payment-detail-page/supplier-payment-detail-page').then(
        (m) => m.SupplierPaymentDetailPage,
      ),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/inventory/warehouse-transfers',
    loadComponent: () =>
      import('./features/inventory/warehouse-transfer-list-page/warehouse-transfer-list-page').then(
        (m) => m.WarehouseTransferListPage,
      ),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/inventory/warehouse-transfers/:warehouseTransferId',
    loadComponent: () =>
      import('./features/inventory/warehouse-transfer-detail-page/warehouse-transfer-detail-page').then(
        (m) => m.WarehouseTransferDetailPage,
      ),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/inventory/inventory-adjustments',
    loadComponent: () =>
      import('./features/inventory/inventory-adjustment-list-page/inventory-adjustment-list-page').then(
        (m) => m.InventoryAdjustmentListPage,
      ),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/inventory/inventory-adjustments/:inventoryAdjustmentId',
    loadComponent: () =>
      import('./features/inventory/inventory-adjustment-detail-page/inventory-adjustment-detail-page').then(
        (m) => m.InventoryAdjustmentDetailPage,
      ),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/inventory/stock-position',
    loadComponent: () =>
      import('./features/inventory/stock-position-page/stock-position-page').then((m) => m.StockPositionPage),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/inventory/ledger',
    loadComponent: () =>
      import('./features/inventory/inventory-ledger-page/inventory-ledger-page').then((m) => m.InventoryLedgerPage),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/reports/trial-balance',
    loadComponent: () =>
      import('./features/reports/trial-balance-page/trial-balance-page').then((m) => m.TrialBalancePage),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/reports/balance-sheet',
    loadComponent: () =>
      import('./features/reports/balance-sheet-page/balance-sheet-page').then((m) => m.BalanceSheetPage),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/reports/income-statement',
    loadComponent: () =>
      import('./features/reports/income-statement-page/income-statement-page').then((m) => m.IncomeStatementPage),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/reports/sales-master-report',
    loadComponent: () =>
      import('./features/reports/sales-master-report-page/sales-master-report-page').then(
        (m) => m.SalesMasterReportPage,
      ),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/reports/purchase-master-report',
    loadComponent: () =>
      import('./features/reports/purchase-master-report-page/purchase-master-report-page').then(
        (m) => m.PurchaseMasterReportPage,
      ),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/reports/vat-summary',
    loadComponent: () =>
      import('./features/reports/vat-summary-report-page/vat-summary-report-page').then(
        (m) => m.VatSummaryReportPage,
      ),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/reports/tds-report',
    loadComponent: () =>
      import('./features/reports/tds-report-page/tds-report-page').then((m) => m.TdsReportPage),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/reports/annex-thirteen',
    loadComponent: () =>
      import('./features/reports/annex-thirteen-report-page/annex-thirteen-report-page').then(
        (m) => m.AnnexThirteenReportPage,
      ),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/reports/annex-five',
    loadComponent: () =>
      import('./features/reports/annex-five-report-page/annex-five-report-page').then(
        (m) => m.AnnexFiveReportPage,
      ),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/reports/customer-ageing-summary',
    loadComponent: () =>
      import('./features/reports/customer-ageing-summary-page/customer-ageing-summary-page').then(
        (m) => m.CustomerAgeingSummaryPage,
      ),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/reports/supplier-ageing-summary',
    loadComponent: () =>
      import('./features/reports/supplier-ageing-summary-page/supplier-ageing-summary-page').then(
        (m) => m.SupplierAgeingSummaryPage,
      ),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/reports/customer-statement',
    loadComponent: () =>
      import('./features/reports/customer-statement-page/customer-statement-page').then(
        (m) => m.CustomerStatementPage,
      ),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/reports/supplier-statement',
    loadComponent: () =>
      import('./features/reports/supplier-statement-page/supplier-statement-page').then(
        (m) => m.SupplierStatementPage,
      ),
    canActivate: [authGuard],
  },
];
