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
    path: 'organizations/:id/configuration/cost-terms',
    loadComponent: () =>
      import('./features/configuration/cost-term-list-page/cost-term-list-page').then((m) => m.CostTermListPage),
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
    path: 'organizations/:id/configuration/banks',
    loadComponent: () =>
      import('./features/configuration/bank-list-page/bank-list-page').then((m) => m.BankListPage),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/configuration/opening-balances',
    loadComponent: () =>
      import('./features/configuration/opening-balances-page/opening-balances-page').then(
        (m) => m.OpeningBalancesPage,
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
    path: 'organizations/:id/configuration/reporting-tags',
    loadComponent: () =>
      import('./features/configuration/reporting-tag-list-page/reporting-tag-list-page').then(
        (m) => m.ReportingTagListPage,
      ),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/configuration/printing-templates',
    loadComponent: () =>
      import('./features/configuration/printing-template-list-page/printing-template-list-page').then(
        (m) => m.PrintingTemplateListPage,
      ),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/configuration/custom-templates',
    loadComponent: () =>
      import('./features/configuration/custom-template-list-page/custom-template-list-page').then(
        (m) => m.CustomTemplateListPage,
      ),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/configuration/alerts',
    loadComponent: () =>
      import('./features/configuration/alert-list-page/alert-list-page').then((m) => m.AlertListPage),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/configuration/import',
    loadComponent: () =>
      import('./features/configuration/import-page/import-page').then((m) => m.ImportPage),
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
    path: 'organizations/:id/sms',
    loadComponent: () => import('./features/crm/sms-shell-page/sms-shell-page').then((m) => m.SmsShellPage),
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
    path: 'organizations/:id/accounting/bank-accounts',
    loadComponent: () =>
      import('./features/accounting/bank-account-list-page/bank-account-list-page').then(
        (m) => m.BankAccountListPage,
      ),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/accounting/cheque-register',
    loadComponent: () =>
      import('./features/accounting/cheque-register-page/cheque-register-page').then((m) => m.ChequeRegisterPage),
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
    path: 'organizations/:id/lock-date',
    loadComponent: () =>
      import('./features/organizations/lock-date-page/lock-date-page').then((m) => m.LockDatePage),
    canActivate: [authGuard],
  },
  {
    // Phase 20f (FR-2.6) -- read-only plan + Accounting Features state.
    path: 'organizations/:id/features',
    loadComponent: () =>
      import('./features/organizations/subscription-features-page/subscription-features-page').then(
        (m) => m.SubscriptionFeaturesPage,
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
    path: 'organizations/:id/sales/sales-orders',
    loadComponent: () =>
      import('./features/sales/sales-order-list-page/sales-order-list-page').then((m) => m.SalesOrderListPage),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/sales/sales-orders/:salesOrderId',
    loadComponent: () =>
      import('./features/sales/sales-order-detail-page/sales-order-detail-page').then((m) => m.SalesOrderDetailPage),
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
    path: 'organizations/:id/quick-payment',
    loadComponent: () =>
      import('./features/payments/quick-payment-page/quick-payment-page').then((m) => m.QuickPaymentPage),
    data: { direction: 'Paid' },
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/quick-receipt',
    loadComponent: () =>
      import('./features/payments/quick-payment-page/quick-payment-page').then((m) => m.QuickPaymentPage),
    data: { direction: 'Received' },
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/allocate-customer-payment',
    loadComponent: () =>
      import('./features/payments/allocate-payment-page/allocate-payment-page').then((m) => m.AllocatePaymentPage),
    data: { direction: 'Received' },
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/allocate-supplier-payment',
    loadComponent: () =>
      import('./features/payments/allocate-payment-page/allocate-payment-page').then((m) => m.AllocatePaymentPage),
    data: { direction: 'Paid' },
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
  {
    path: 'organizations/:id/reports/system-audit',
    loadComponent: () =>
      import('./features/reports/system-audit-report-page/system-audit-report-page').then(
        (m) => m.SystemAuditReportPage,
      ),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/reports/cash-flow-summary',
    loadComponent: () =>
      import('./features/reports/cash-flow-summary-page/cash-flow-summary-page').then(
        (m) => m.CashFlowSummaryPage,
      ),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/reports/sales-register',
    loadComponent: () =>
      import('./features/reports/sales-register-page/sales-register-page').then((m) => m.SalesRegisterPage),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/reports/purchase-register',
    loadComponent: () =>
      import('./features/reports/purchase-register-page/purchase-register-page').then(
        (m) => m.PurchaseRegisterPage,
      ),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/reports/stock-ageing',
    loadComponent: () =>
      import('./features/reports/stock-ageing-page/stock-ageing-page').then((m) => m.StockAgeingPage),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/reports/product-profitability',
    loadComponent: () =>
      import('./features/reports/product-profitability-page/product-profitability-page').then(
        (m) => m.ProductProfitabilityPage,
      ),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/reports/ratio-analysis',
    loadComponent: () =>
      import('./features/reports/ratio-analysis-page/ratio-analysis-page').then((m) => m.RatioAnalysisPage),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/workflow/transaction-approval-queue',
    loadComponent: () =>
      import('./features/workflow/transaction-approval-queue-page/transaction-approval-queue-page').then(
        (m) => m.TransactionApprovalQueuePage,
      ),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/roles',
    loadComponent: () => import('./features/tenancy/role-list-page/role-list-page').then((m) => m.RoleListPage),
    canActivate: [authGuard],
  },
  {
    path: 'organizations/:id/roles/:roleId/permissions',
    loadComponent: () =>
      import('./features/tenancy/role-permission-matrix-page/role-permission-matrix-page').then(
        (m) => m.RolePermissionMatrixPage,
      ),
    canActivate: [authGuard],
  },
];
