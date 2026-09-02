using ErpApp.Application.Common.Security;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Tenancy;

/// <summary>
/// Seed grants for Phase 1c's two system roles against every PermissionKey currently checked by
/// AuthorizationBehavior (PermissionKeys.OrganizationInviteUser/OrganizationAcceptRequest) --
/// Admin=granted, Member=explicitly denied (an explicit IsGranted=false row rather than the row
/// simply being absent, matching architecture-spec.md §3.7's RolePermission shape and making the
/// "Member can't do this" intent visible in the seed data rather than implicit). Every later
/// phase's PermissionKeys constants need their own Admin/Member seed rows added here (or, once
/// the roadmap's later Role Reference editor exists, real per-tenant data instead of a seed).
/// </summary>
public sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    private static readonly Guid AdminInviteUserId = Guid.Parse("00000000-0000-0000-0002-000000000001");
    private static readonly Guid AdminAcceptRequestId = Guid.Parse("00000000-0000-0000-0002-000000000002");
    private static readonly Guid MemberInviteUserId = Guid.Parse("00000000-0000-0000-0002-000000000003");
    private static readonly Guid MemberAcceptRequestId = Guid.Parse("00000000-0000-0000-0002-000000000004");

    // Phase 2 (Configuration foundation) -- Admin granted on View+Manage for every lookup type;
    // Member granted View only, Manage explicitly denied (an IsGranted=false row, matching the
    // existing denial-row convention above). Judgment call: Configuration lookups are tenant-wide
    // control-plane settings, not per-user working data, so Member-read/Admin-write fits the
    // existing Admin/Member split's intent -- see phase-2-status.md's scope decisions.
    private static readonly Guid AdminCreditTermViewId = Guid.Parse("00000000-0000-0000-0002-000000000005");
    private static readonly Guid AdminCreditTermManageId = Guid.Parse("00000000-0000-0000-0002-000000000006");
    private static readonly Guid MemberCreditTermViewId = Guid.Parse("00000000-0000-0000-0002-000000000007");
    private static readonly Guid MemberCreditTermManageId = Guid.Parse("00000000-0000-0000-0002-000000000008");

    private static readonly Guid AdminPaymentModeViewId = Guid.Parse("00000000-0000-0000-0002-000000000009");
    private static readonly Guid AdminPaymentModeManageId = Guid.Parse("00000000-0000-0000-0002-00000000000a");
    private static readonly Guid MemberPaymentModeViewId = Guid.Parse("00000000-0000-0000-0002-00000000000b");
    private static readonly Guid MemberPaymentModeManageId = Guid.Parse("00000000-0000-0000-0002-00000000000c");

    private static readonly Guid AdminCustomStatusViewId = Guid.Parse("00000000-0000-0000-0002-00000000000d");
    private static readonly Guid AdminCustomStatusManageId = Guid.Parse("00000000-0000-0000-0002-00000000000e");
    private static readonly Guid MemberCustomStatusViewId = Guid.Parse("00000000-0000-0000-0002-00000000000f");
    private static readonly Guid MemberCustomStatusManageId = Guid.Parse("00000000-0000-0000-0002-000000000010");

    private static readonly Guid AdminReportingTagCategoryViewId = Guid.Parse("00000000-0000-0000-0002-000000000011");
    private static readonly Guid AdminReportingTagCategoryManageId = Guid.Parse("00000000-0000-0000-0002-000000000012");
    private static readonly Guid MemberReportingTagCategoryViewId = Guid.Parse("00000000-0000-0000-0002-000000000013");
    private static readonly Guid MemberReportingTagCategoryManageId = Guid.Parse("00000000-0000-0000-0002-000000000014");

    private static readonly Guid AdminReportingTagOptionViewId = Guid.Parse("00000000-0000-0000-0002-000000000015");
    private static readonly Guid AdminReportingTagOptionManageId = Guid.Parse("00000000-0000-0000-0002-000000000016");
    private static readonly Guid MemberReportingTagOptionViewId = Guid.Parse("00000000-0000-0000-0002-000000000017");
    private static readonly Guid MemberReportingTagOptionManageId = Guid.Parse("00000000-0000-0000-0002-000000000018");

    private static readonly Guid AdminCustomFieldDefinitionViewId = Guid.Parse("00000000-0000-0000-0002-000000000019");
    private static readonly Guid AdminCustomFieldDefinitionManageId = Guid.Parse("00000000-0000-0000-0002-00000000001a");
    private static readonly Guid MemberCustomFieldDefinitionViewId = Guid.Parse("00000000-0000-0000-0002-00000000001b");
    private static readonly Guid MemberCustomFieldDefinitionManageId = Guid.Parse("00000000-0000-0000-0002-00000000001c");

    // Phase 3 (Contacts & Catalog) -- ContactGroup/ProductCategory/UnitOfMeasurement are
    // taxonomy/control-plane (same Member-View-only/Admin-write split as Phase 2's lookups
    // above). Contact/Product are working data Members create/edit daily, so Member gets
    // View+Manage there instead -- see phase-3-status.md's scope decisions.
    private static readonly Guid AdminContactGroupViewId = Guid.Parse("00000000-0000-0000-0002-00000000001d");
    private static readonly Guid AdminContactGroupManageId = Guid.Parse("00000000-0000-0000-0002-00000000001e");
    private static readonly Guid MemberContactGroupViewId = Guid.Parse("00000000-0000-0000-0002-00000000001f");
    private static readonly Guid MemberContactGroupManageId = Guid.Parse("00000000-0000-0000-0002-000000000020");

    private static readonly Guid AdminContactViewId = Guid.Parse("00000000-0000-0000-0002-000000000021");
    private static readonly Guid AdminContactManageId = Guid.Parse("00000000-0000-0000-0002-000000000022");
    private static readonly Guid MemberContactViewId = Guid.Parse("00000000-0000-0000-0002-000000000023");
    private static readonly Guid MemberContactManageId = Guid.Parse("00000000-0000-0000-0002-000000000024");

    private static readonly Guid AdminProductCategoryViewId = Guid.Parse("00000000-0000-0000-0002-000000000025");
    private static readonly Guid AdminProductCategoryManageId = Guid.Parse("00000000-0000-0000-0002-000000000026");
    private static readonly Guid MemberProductCategoryViewId = Guid.Parse("00000000-0000-0000-0002-000000000027");
    private static readonly Guid MemberProductCategoryManageId = Guid.Parse("00000000-0000-0000-0002-000000000028");

    private static readonly Guid AdminUnitOfMeasurementViewId = Guid.Parse("00000000-0000-0000-0002-000000000029");
    private static readonly Guid AdminUnitOfMeasurementManageId = Guid.Parse("00000000-0000-0000-0002-00000000002a");
    private static readonly Guid MemberUnitOfMeasurementViewId = Guid.Parse("00000000-0000-0000-0002-00000000002b");
    private static readonly Guid MemberUnitOfMeasurementManageId = Guid.Parse("00000000-0000-0000-0002-00000000002c");

    private static readonly Guid AdminProductViewId = Guid.Parse("00000000-0000-0000-0002-00000000002d");
    private static readonly Guid AdminProductManageId = Guid.Parse("00000000-0000-0000-0002-00000000002e");
    private static readonly Guid MemberProductViewId = Guid.Parse("00000000-0000-0000-0002-00000000002f");
    private static readonly Guid MemberProductManageId = Guid.Parse("00000000-0000-0000-0002-000000000030");

    // Phase 4 (Accounting core) -- AccountGroup/Account are simple master data (Chart of
    // Accounts), same Member-View-only/Admin-write split as Phase 2/3's taxonomy lookups.
    // JournalVoucher/CashTransfer are the first real ApprovableTransaction document types: Admin
    // is granted all four actions; Member is granted View+Create+Edit but Approve is explicitly
    // denied (IsGranted=false) -- the concrete maker-checker cut architecture-spec.md §3.2 calls
    // for, now that a real Draft->Approve document type exists (see phase-4-status.md's scope
    // decisions).
    private static readonly Guid AdminAccountGroupViewId = Guid.Parse("00000000-0000-0000-0002-000000000031");
    private static readonly Guid AdminAccountGroupManageId = Guid.Parse("00000000-0000-0000-0002-000000000032");
    private static readonly Guid MemberAccountGroupViewId = Guid.Parse("00000000-0000-0000-0002-000000000033");
    private static readonly Guid MemberAccountGroupManageId = Guid.Parse("00000000-0000-0000-0002-000000000034");

    private static readonly Guid AdminAccountViewId = Guid.Parse("00000000-0000-0000-0002-000000000035");
    private static readonly Guid AdminAccountManageId = Guid.Parse("00000000-0000-0000-0002-000000000036");
    private static readonly Guid MemberAccountViewId = Guid.Parse("00000000-0000-0000-0002-000000000037");
    private static readonly Guid MemberAccountManageId = Guid.Parse("00000000-0000-0000-0002-000000000038");

    private static readonly Guid AdminJournalVoucherViewId = Guid.Parse("00000000-0000-0000-0002-000000000039");
    private static readonly Guid AdminJournalVoucherCreateId = Guid.Parse("00000000-0000-0000-0002-00000000003a");
    private static readonly Guid AdminJournalVoucherEditId = Guid.Parse("00000000-0000-0000-0002-00000000003b");
    private static readonly Guid AdminJournalVoucherApproveId = Guid.Parse("00000000-0000-0000-0002-00000000003c");
    private static readonly Guid MemberJournalVoucherViewId = Guid.Parse("00000000-0000-0000-0002-00000000003d");
    private static readonly Guid MemberJournalVoucherCreateId = Guid.Parse("00000000-0000-0000-0002-00000000003e");
    private static readonly Guid MemberJournalVoucherEditId = Guid.Parse("00000000-0000-0000-0002-00000000003f");
    private static readonly Guid MemberJournalVoucherApproveId = Guid.Parse("00000000-0000-0000-0002-000000000040");

    private static readonly Guid AdminCashTransferViewId = Guid.Parse("00000000-0000-0000-0002-000000000041");
    private static readonly Guid AdminCashTransferCreateId = Guid.Parse("00000000-0000-0000-0002-000000000042");
    private static readonly Guid AdminCashTransferEditId = Guid.Parse("00000000-0000-0000-0002-000000000043");
    private static readonly Guid AdminCashTransferApproveId = Guid.Parse("00000000-0000-0000-0002-000000000044");
    private static readonly Guid MemberCashTransferViewId = Guid.Parse("00000000-0000-0000-0002-000000000045");
    private static readonly Guid MemberCashTransferCreateId = Guid.Parse("00000000-0000-0000-0002-000000000046");
    private static readonly Guid MemberCashTransferEditId = Guid.Parse("00000000-0000-0000-0002-000000000047");
    private static readonly Guid MemberCashTransferApproveId = Guid.Parse("00000000-0000-0000-0002-000000000048");

    // Phase 5 (Sales chain) -- Warehouse is simple master data (Member View-only/Admin-write,
    // same as AccountGroup/Account). Quotation/SalesOrder/Invoice/CreditNote/Payment continue the
    // Phase 4 maker-checker split (Member gets View+Create+Edit, Approve explicitly denied).
    // AccountingDefaultsManage is Admin-only (a settings-like key, no Member grant at all) --
    // see phase-5-status.md's scope decisions for the TenantSettings default-GL-accounts fallback.
    private static readonly Guid AdminWarehouseViewId = Guid.Parse("00000000-0000-0000-0002-000000000049");
    private static readonly Guid AdminWarehouseManageId = Guid.Parse("00000000-0000-0000-0002-00000000004a");
    private static readonly Guid MemberWarehouseViewId = Guid.Parse("00000000-0000-0000-0002-00000000004b");
    private static readonly Guid MemberWarehouseManageId = Guid.Parse("00000000-0000-0000-0002-00000000004c");

    private static readonly Guid AdminQuotationViewId = Guid.Parse("00000000-0000-0000-0002-00000000004d");
    private static readonly Guid AdminQuotationCreateId = Guid.Parse("00000000-0000-0000-0002-00000000004e");
    private static readonly Guid AdminQuotationEditId = Guid.Parse("00000000-0000-0000-0002-00000000004f");
    private static readonly Guid AdminQuotationApproveId = Guid.Parse("00000000-0000-0000-0002-000000000050");
    private static readonly Guid MemberQuotationViewId = Guid.Parse("00000000-0000-0000-0002-000000000051");
    private static readonly Guid MemberQuotationCreateId = Guid.Parse("00000000-0000-0000-0002-000000000052");
    private static readonly Guid MemberQuotationEditId = Guid.Parse("00000000-0000-0000-0002-000000000053");
    private static readonly Guid MemberQuotationApproveId = Guid.Parse("00000000-0000-0000-0002-000000000054");

    private static readonly Guid AdminSalesOrderViewId = Guid.Parse("00000000-0000-0000-0002-000000000055");
    private static readonly Guid AdminSalesOrderCreateId = Guid.Parse("00000000-0000-0000-0002-000000000056");
    private static readonly Guid AdminSalesOrderEditId = Guid.Parse("00000000-0000-0000-0002-000000000057");
    private static readonly Guid AdminSalesOrderApproveId = Guid.Parse("00000000-0000-0000-0002-000000000058");
    private static readonly Guid MemberSalesOrderViewId = Guid.Parse("00000000-0000-0000-0002-000000000059");
    private static readonly Guid MemberSalesOrderCreateId = Guid.Parse("00000000-0000-0000-0002-00000000005a");
    private static readonly Guid MemberSalesOrderEditId = Guid.Parse("00000000-0000-0000-0002-00000000005b");
    private static readonly Guid MemberSalesOrderApproveId = Guid.Parse("00000000-0000-0000-0002-00000000005c");

    private static readonly Guid AdminInvoiceViewId = Guid.Parse("00000000-0000-0000-0002-00000000005d");
    private static readonly Guid AdminInvoiceCreateId = Guid.Parse("00000000-0000-0000-0002-00000000005e");
    private static readonly Guid AdminInvoiceEditId = Guid.Parse("00000000-0000-0000-0002-00000000005f");
    private static readonly Guid AdminInvoiceApproveId = Guid.Parse("00000000-0000-0000-0002-000000000060");
    private static readonly Guid MemberInvoiceViewId = Guid.Parse("00000000-0000-0000-0002-000000000061");
    private static readonly Guid MemberInvoiceCreateId = Guid.Parse("00000000-0000-0000-0002-000000000062");
    private static readonly Guid MemberInvoiceEditId = Guid.Parse("00000000-0000-0000-0002-000000000063");
    private static readonly Guid MemberInvoiceApproveId = Guid.Parse("00000000-0000-0000-0002-000000000064");

    private static readonly Guid AdminCreditNoteViewId = Guid.Parse("00000000-0000-0000-0002-000000000065");
    private static readonly Guid AdminCreditNoteCreateId = Guid.Parse("00000000-0000-0000-0002-000000000066");
    private static readonly Guid AdminCreditNoteEditId = Guid.Parse("00000000-0000-0000-0002-000000000067");
    private static readonly Guid AdminCreditNoteApproveId = Guid.Parse("00000000-0000-0000-0002-000000000068");
    private static readonly Guid MemberCreditNoteViewId = Guid.Parse("00000000-0000-0000-0002-000000000069");
    private static readonly Guid MemberCreditNoteCreateId = Guid.Parse("00000000-0000-0000-0002-00000000006a");
    private static readonly Guid MemberCreditNoteEditId = Guid.Parse("00000000-0000-0000-0002-00000000006b");
    private static readonly Guid MemberCreditNoteApproveId = Guid.Parse("00000000-0000-0000-0002-00000000006c");

    private static readonly Guid AdminPaymentViewId = Guid.Parse("00000000-0000-0000-0002-00000000006d");
    private static readonly Guid AdminPaymentCreateId = Guid.Parse("00000000-0000-0000-0002-00000000006e");
    private static readonly Guid AdminPaymentEditId = Guid.Parse("00000000-0000-0000-0002-00000000006f");
    private static readonly Guid AdminPaymentApproveId = Guid.Parse("00000000-0000-0000-0002-000000000070");
    private static readonly Guid MemberPaymentViewId = Guid.Parse("00000000-0000-0000-0002-000000000071");
    private static readonly Guid MemberPaymentCreateId = Guid.Parse("00000000-0000-0000-0002-000000000072");
    private static readonly Guid MemberPaymentEditId = Guid.Parse("00000000-0000-0000-0002-000000000073");
    private static readonly Guid MemberPaymentApproveId = Guid.Parse("00000000-0000-0000-0002-000000000074");

    private static readonly Guid AdminAccountingDefaultsManageId = Guid.Parse("00000000-0000-0000-0002-000000000075");
    private static readonly Guid MemberAccountingDefaultsManageId = Guid.Parse("00000000-0000-0000-0002-000000000076");

    // Phase 6 (Purchase chain) -- TdsType is a simple View/Manage lookup pair (Member View-only/
    // Admin-write, same as CreditTerm/PaymentMode). PurchaseOrder/PurchaseBill/Expense/DebitNote
    // continue the Phase 4/5 maker-checker split (Member View+Create+Edit, Approve explicitly
    // denied). Payments.Payment.* permissions above are reused as-is for Supplier Payment too --
    // no new rows needed (see phase-6-status.md's scope decision on why the permission matrix
    // wasn't split Sales.Payment/Purchasing.Payment).
    private static readonly Guid AdminTdsTypeViewId = Guid.Parse("00000000-0000-0000-0002-000000000077");
    private static readonly Guid AdminTdsTypeManageId = Guid.Parse("00000000-0000-0000-0002-000000000078");
    private static readonly Guid MemberTdsTypeViewId = Guid.Parse("00000000-0000-0000-0002-000000000079");
    private static readonly Guid MemberTdsTypeManageId = Guid.Parse("00000000-0000-0000-0002-00000000007a");

    private static readonly Guid AdminPurchaseOrderViewId = Guid.Parse("00000000-0000-0000-0002-00000000007b");
    private static readonly Guid AdminPurchaseOrderCreateId = Guid.Parse("00000000-0000-0000-0002-00000000007c");
    private static readonly Guid AdminPurchaseOrderEditId = Guid.Parse("00000000-0000-0000-0002-00000000007d");
    private static readonly Guid AdminPurchaseOrderApproveId = Guid.Parse("00000000-0000-0000-0002-00000000007e");
    private static readonly Guid MemberPurchaseOrderViewId = Guid.Parse("00000000-0000-0000-0002-00000000007f");
    private static readonly Guid MemberPurchaseOrderCreateId = Guid.Parse("00000000-0000-0000-0002-000000000080");
    private static readonly Guid MemberPurchaseOrderEditId = Guid.Parse("00000000-0000-0000-0002-000000000081");
    private static readonly Guid MemberPurchaseOrderApproveId = Guid.Parse("00000000-0000-0000-0002-000000000082");

    private static readonly Guid AdminPurchaseBillViewId = Guid.Parse("00000000-0000-0000-0002-000000000083");
    private static readonly Guid AdminPurchaseBillCreateId = Guid.Parse("00000000-0000-0000-0002-000000000084");
    private static readonly Guid AdminPurchaseBillEditId = Guid.Parse("00000000-0000-0000-0002-000000000085");
    private static readonly Guid AdminPurchaseBillApproveId = Guid.Parse("00000000-0000-0000-0002-000000000086");
    private static readonly Guid MemberPurchaseBillViewId = Guid.Parse("00000000-0000-0000-0002-000000000087");
    private static readonly Guid MemberPurchaseBillCreateId = Guid.Parse("00000000-0000-0000-0002-000000000088");
    private static readonly Guid MemberPurchaseBillEditId = Guid.Parse("00000000-0000-0000-0002-000000000089");
    private static readonly Guid MemberPurchaseBillApproveId = Guid.Parse("00000000-0000-0000-0002-00000000008a");

    private static readonly Guid AdminExpenseViewId = Guid.Parse("00000000-0000-0000-0002-00000000008b");
    private static readonly Guid AdminExpenseCreateId = Guid.Parse("00000000-0000-0000-0002-00000000008c");
    private static readonly Guid AdminExpenseEditId = Guid.Parse("00000000-0000-0000-0002-00000000008d");
    private static readonly Guid AdminExpenseApproveId = Guid.Parse("00000000-0000-0000-0002-00000000008e");
    private static readonly Guid MemberExpenseViewId = Guid.Parse("00000000-0000-0000-0002-00000000008f");
    private static readonly Guid MemberExpenseCreateId = Guid.Parse("00000000-0000-0000-0002-000000000090");
    private static readonly Guid MemberExpenseEditId = Guid.Parse("00000000-0000-0000-0002-000000000091");
    private static readonly Guid MemberExpenseApproveId = Guid.Parse("00000000-0000-0000-0002-000000000092");

    private static readonly Guid AdminDebitNoteViewId = Guid.Parse("00000000-0000-0000-0002-000000000093");
    private static readonly Guid AdminDebitNoteCreateId = Guid.Parse("00000000-0000-0000-0002-000000000094");
    private static readonly Guid AdminDebitNoteEditId = Guid.Parse("00000000-0000-0000-0002-000000000095");
    private static readonly Guid AdminDebitNoteApproveId = Guid.Parse("00000000-0000-0000-0002-000000000096");
    private static readonly Guid MemberDebitNoteViewId = Guid.Parse("00000000-0000-0000-0002-000000000097");
    private static readonly Guid MemberDebitNoteCreateId = Guid.Parse("00000000-0000-0000-0002-000000000098");
    private static readonly Guid MemberDebitNoteEditId = Guid.Parse("00000000-0000-0000-0002-000000000099");
    private static readonly Guid MemberDebitNoteApproveId = Guid.Parse("00000000-0000-0000-0002-00000000009a");

    // Phase 7 (Inventory & stock ledger) -- WarehouseTransfer/InventoryAdjustment continue the
    // maker-checker split (Member View+Create+Edit, Approve explicitly denied), same as every
    // ApprovableTransaction since Phase 4. InventoryLedgerView is granted to both roles -- it's a
    // read-only report over data Member already has visibility into via the documents that create
    // it (PurchaseBill/Invoice/WarehouseTransfer/InventoryAdjustment).
    private static readonly Guid AdminWarehouseTransferViewId = Guid.Parse("00000000-0000-0000-0002-00000000009b");
    private static readonly Guid AdminWarehouseTransferCreateId = Guid.Parse("00000000-0000-0000-0002-00000000009c");
    private static readonly Guid AdminWarehouseTransferEditId = Guid.Parse("00000000-0000-0000-0002-00000000009d");
    private static readonly Guid AdminWarehouseTransferApproveId = Guid.Parse("00000000-0000-0000-0002-00000000009e");
    private static readonly Guid MemberWarehouseTransferViewId = Guid.Parse("00000000-0000-0000-0002-00000000009f");
    private static readonly Guid MemberWarehouseTransferCreateId = Guid.Parse("00000000-0000-0000-0002-0000000000a0");
    private static readonly Guid MemberWarehouseTransferEditId = Guid.Parse("00000000-0000-0000-0002-0000000000a1");
    private static readonly Guid MemberWarehouseTransferApproveId = Guid.Parse("00000000-0000-0000-0002-0000000000a2");

    private static readonly Guid AdminInventoryAdjustmentViewId = Guid.Parse("00000000-0000-0000-0002-0000000000a3");
    private static readonly Guid AdminInventoryAdjustmentCreateId = Guid.Parse("00000000-0000-0000-0002-0000000000a4");
    private static readonly Guid AdminInventoryAdjustmentEditId = Guid.Parse("00000000-0000-0000-0002-0000000000a5");
    private static readonly Guid AdminInventoryAdjustmentApproveId = Guid.Parse("00000000-0000-0000-0002-0000000000a6");
    private static readonly Guid MemberInventoryAdjustmentViewId = Guid.Parse("00000000-0000-0000-0002-0000000000a7");
    private static readonly Guid MemberInventoryAdjustmentCreateId = Guid.Parse("00000000-0000-0000-0002-0000000000a8");
    private static readonly Guid MemberInventoryAdjustmentEditId = Guid.Parse("00000000-0000-0000-0002-0000000000a9");
    private static readonly Guid MemberInventoryAdjustmentApproveId = Guid.Parse("00000000-0000-0000-0002-0000000000aa");

    private static readonly Guid AdminInventoryLedgerViewId = Guid.Parse("00000000-0000-0000-0002-0000000000ab");
    private static readonly Guid MemberInventoryLedgerViewId = Guid.Parse("00000000-0000-0000-0002-0000000000ac");

    // Phase 8a (Core Financial Reports) -- granted to both roles, same reasoning as
    // InventoryLedgerView above (see phase-8a-status.md's scope decision).
    private static readonly Guid AdminTrialBalanceViewId = Guid.Parse("00000000-0000-0000-0002-0000000000ad");
    private static readonly Guid MemberTrialBalanceViewId = Guid.Parse("00000000-0000-0000-0002-0000000000ae");
    private static readonly Guid AdminBalanceSheetViewId = Guid.Parse("00000000-0000-0000-0002-0000000000af");
    private static readonly Guid MemberBalanceSheetViewId = Guid.Parse("00000000-0000-0000-0002-0000000000b0");
    private static readonly Guid AdminIncomeStatementViewId = Guid.Parse("00000000-0000-0000-0002-0000000000b1");
    private static readonly Guid MemberIncomeStatementViewId = Guid.Parse("00000000-0000-0000-0002-0000000000b2");

    // Phase 8b (Sales & Purchase Master Reports) -- Admin-only, unlike Phase 8a's reports above
    // (see PermissionKeys.SalesMasterReportView's doc comment / phase-8b-status.md's scope
    // decision). Member gets an explicit IsGranted=false denial row, same convention as every
    // other Admin-only key in this file.
    private static readonly Guid AdminSalesMasterReportViewId = Guid.Parse("00000000-0000-0000-0002-0000000000b3");
    private static readonly Guid MemberSalesMasterReportViewId = Guid.Parse("00000000-0000-0000-0002-0000000000b4");
    private static readonly Guid AdminPurchaseMasterReportViewId = Guid.Parse("00000000-0000-0000-0002-0000000000b5");
    private static readonly Guid MemberPurchaseMasterReportViewId = Guid.Parse("00000000-0000-0000-0002-0000000000b6");

    // Phase 8c (VAT Summary Report) -- granted to both roles, same reasoning as Phase 8a's three
    // reports (see PermissionKeys.VatSummaryView's doc comment / phase-8c-status.md's scope
    // decision): this report's output is a rollup, not a flat per-transaction fact table.
    private static readonly Guid AdminVatSummaryViewId = Guid.Parse("00000000-0000-0000-0002-0000000000b7");
    private static readonly Guid MemberVatSummaryViewId = Guid.Parse("00000000-0000-0000-0002-0000000000b8");

    // Phase 8d (TDS Report) -- Admin-only, same convention as Phase 8b's Master Reports (see
    // PermissionKeys.TdsReportView's doc comment / phase-8d-status.md's scope decision). Member
    // gets an explicit IsGranted=false denial row, same convention as every other Admin-only key.
    private static readonly Guid AdminTdsReportViewId = Guid.Parse("00000000-0000-0000-0002-0000000000b9");
    private static readonly Guid MemberTdsReportViewId = Guid.Parse("00000000-0000-0000-0002-0000000000ba");

    // Phase 8e (Annex 13 Report) -- Admin-only (see PermissionKeys.AnnexThirteenView's doc comment /
    // phase-8e-status.md's scope decision). Member gets an explicit IsGranted=false denial row, same
    // convention as every other Admin-only key.
    private static readonly Guid AdminAnnexThirteenViewId = Guid.Parse("00000000-0000-0000-0002-0000000000bb");
    private static readonly Guid MemberAnnexThirteenViewId = Guid.Parse("00000000-0000-0000-0002-0000000000bc");

    // Phase 8f (Annex 5 Report) -- Admin-only (see PermissionKeys.AnnexFiveView's doc comment /
    // phase-8f-status.md's scope decision). Member gets an explicit IsGranted=false denial row, same
    // convention as every other Admin-only key.
    private static readonly Guid AdminAnnexFiveViewId = Guid.Parse("00000000-0000-0000-0002-0000000000bd");
    private static readonly Guid MemberAnnexFiveViewId = Guid.Parse("00000000-0000-0000-0002-0000000000be");

    // Phase 9 (Customer & Supplier Ageing + Statement Reports) -- Admin-only for all four keys, the
    // strongest PAN/identity-exposure case yet (see PermissionKeys.CustomerAgeingSummaryView's doc
    // comment / phase-9-status.md's scope decision). Member gets an explicit IsGranted=false denial
    // row for each, same convention as every other Admin-only key in this file.
    private static readonly Guid AdminCustomerAgeingSummaryViewId = Guid.Parse("00000000-0000-0000-0002-0000000000bf");
    private static readonly Guid MemberCustomerAgeingSummaryViewId = Guid.Parse("00000000-0000-0000-0002-0000000000c0");
    private static readonly Guid AdminSupplierAgeingSummaryViewId = Guid.Parse("00000000-0000-0000-0002-0000000000c1");
    private static readonly Guid MemberSupplierAgeingSummaryViewId = Guid.Parse("00000000-0000-0000-0002-0000000000c2");
    private static readonly Guid AdminCustomerStatementViewId = Guid.Parse("00000000-0000-0000-0002-0000000000c3");
    private static readonly Guid MemberCustomerStatementViewId = Guid.Parse("00000000-0000-0000-0002-0000000000c4");
    private static readonly Guid AdminSupplierStatementViewId = Guid.Parse("00000000-0000-0000-0002-0000000000c5");
    private static readonly Guid MemberSupplierStatementViewId = Guid.Parse("00000000-0000-0000-0002-0000000000c6");

    // Phase 12 (Transaction Approval Queue) -- Admin+Member, unlike most Reports.*.View keys above
    // (see PermissionKeys.TransactionApprovalView's doc comment / phase-12-status.md's scope
    // decision): this key exists to satisfy AuthorizationBehavior's org-membership check, not to
    // gate exposure -- the query's own per-document-type *.Approve filtering already narrows what a
    // Member sees down to nothing if they hold no Approve grants at all.
    private static readonly Guid AdminTransactionApprovalViewId = Guid.Parse("00000000-0000-0000-0002-0000000000c7");
    private static readonly Guid MemberTransactionApprovalViewId = Guid.Parse("00000000-0000-0000-0002-0000000000c8");

    // Phase 13 (Tasks, the second Workflow-context feature) -- TaskView/TaskManage both granted to
    // Member (see PermissionKeys.TaskView's doc comment: routine daily-use working data, the same
    // Member-View+Manage precedent Contact/Product set, not a maker-checker document). TaskTypeView/
    // TaskTypeManage follow every other Configuration lookup's Member-View-only/Admin-write split.
    private static readonly Guid AdminTaskViewId = Guid.Parse("00000000-0000-0000-0002-0000000000c9");
    private static readonly Guid AdminTaskManageId = Guid.Parse("00000000-0000-0000-0002-0000000000ca");
    private static readonly Guid MemberTaskViewId = Guid.Parse("00000000-0000-0000-0002-0000000000cb");
    private static readonly Guid MemberTaskManageId = Guid.Parse("00000000-0000-0000-0002-0000000000cc");

    private static readonly Guid AdminTaskTypeViewId = Guid.Parse("00000000-0000-0000-0002-0000000000cd");
    private static readonly Guid AdminTaskTypeManageId = Guid.Parse("00000000-0000-0000-0002-0000000000ce");
    private static readonly Guid MemberTaskTypeViewId = Guid.Parse("00000000-0000-0000-0002-0000000000cf");
    private static readonly Guid MemberTaskTypeManageId = Guid.Parse("00000000-0000-0000-0002-0000000000d0");

    // Phase 14 (Role Reference) -- Admin-only for both keys (see PermissionKeys.RoleView's doc
    // comment). Member gets an explicit IsGranted=false denial row, same convention as every other
    // Admin-only key in this file.
    private static readonly Guid AdminRoleViewId = Guid.Parse("00000000-0000-0000-0002-0000000000d1");
    private static readonly Guid AdminRoleManageId = Guid.Parse("00000000-0000-0000-0002-0000000000d2");
    private static readonly Guid MemberRoleViewId = Guid.Parse("00000000-0000-0000-0002-0000000000d3");
    private static readonly Guid MemberRoleManageId = Guid.Parse("00000000-0000-0000-0002-0000000000d4");

    // Phase 15 (Deals, the CRM module's first feature) -- Crm.Deal.* both granted to Member (see
    // PermissionKeys.DealView's doc comment: routine daily-use working data, the same Member-View+
    // Manage precedent Contact/Product/Task set). Crm.LeadSource.*/Crm.DealStage.* follow every
    // other Configuration lookup's Member-View-only/Admin-write split.
    private static readonly Guid AdminDealViewId = Guid.Parse("00000000-0000-0000-0002-0000000000d5");
    private static readonly Guid AdminDealManageId = Guid.Parse("00000000-0000-0000-0002-0000000000d6");
    private static readonly Guid MemberDealViewId = Guid.Parse("00000000-0000-0000-0002-0000000000d7");
    private static readonly Guid MemberDealManageId = Guid.Parse("00000000-0000-0000-0002-0000000000d8");

    private static readonly Guid AdminLeadSourceViewId = Guid.Parse("00000000-0000-0000-0002-0000000000d9");
    private static readonly Guid AdminLeadSourceManageId = Guid.Parse("00000000-0000-0000-0002-0000000000da");
    private static readonly Guid MemberLeadSourceViewId = Guid.Parse("00000000-0000-0000-0002-0000000000db");
    private static readonly Guid MemberLeadSourceManageId = Guid.Parse("00000000-0000-0000-0002-0000000000dc");

    private static readonly Guid AdminDealStageViewId = Guid.Parse("00000000-0000-0000-0002-0000000000dd");
    private static readonly Guid AdminDealStageManageId = Guid.Parse("00000000-0000-0000-0002-0000000000de");
    private static readonly Guid MemberDealStageViewId = Guid.Parse("00000000-0000-0000-0002-0000000000df");
    private static readonly Guid MemberDealStageManageId = Guid.Parse("00000000-0000-0000-0002-0000000000e0");

    // Phase 16a (Void lifecycle + lock-date enforcement) -- every *.Void key is Admin-granted/
    // Member-denied, the same maker-checker default every *.Approve key above already uses (see
    // PermissionKeys.QuotationVoid's doc comment). Tenancy.Organization.LockDateManage is
    // Admin-only for both View and Manage (one key covers both, same single-key shape
    // AccountingDefaultsManage already uses for its own get+update pair).
    private static readonly Guid AdminQuotationVoidId = Guid.Parse("00000000-0000-0000-0002-0000000000e1");
    private static readonly Guid MemberQuotationVoidId = Guid.Parse("00000000-0000-0000-0002-0000000000e2");
    private static readonly Guid AdminSalesOrderVoidId = Guid.Parse("00000000-0000-0000-0002-0000000000e3");
    private static readonly Guid MemberSalesOrderVoidId = Guid.Parse("00000000-0000-0000-0002-0000000000e4");
    private static readonly Guid AdminInvoiceVoidId = Guid.Parse("00000000-0000-0000-0002-0000000000e5");
    private static readonly Guid MemberInvoiceVoidId = Guid.Parse("00000000-0000-0000-0002-0000000000e6");
    private static readonly Guid AdminCreditNoteVoidId = Guid.Parse("00000000-0000-0000-0002-0000000000e7");
    private static readonly Guid MemberCreditNoteVoidId = Guid.Parse("00000000-0000-0000-0002-0000000000e8");
    private static readonly Guid AdminPaymentVoidId = Guid.Parse("00000000-0000-0000-0002-0000000000e9");
    private static readonly Guid MemberPaymentVoidId = Guid.Parse("00000000-0000-0000-0002-0000000000ea");
    private static readonly Guid AdminPurchaseOrderVoidId = Guid.Parse("00000000-0000-0000-0002-0000000000eb");
    private static readonly Guid MemberPurchaseOrderVoidId = Guid.Parse("00000000-0000-0000-0002-0000000000ec");
    private static readonly Guid AdminPurchaseBillVoidId = Guid.Parse("00000000-0000-0000-0002-0000000000ed");
    private static readonly Guid MemberPurchaseBillVoidId = Guid.Parse("00000000-0000-0000-0002-0000000000ee");
    private static readonly Guid AdminExpenseVoidId = Guid.Parse("00000000-0000-0000-0002-0000000000ef");
    private static readonly Guid MemberExpenseVoidId = Guid.Parse("00000000-0000-0000-0002-0000000000f0");
    private static readonly Guid AdminDebitNoteVoidId = Guid.Parse("00000000-0000-0000-0002-0000000000f1");
    private static readonly Guid MemberDebitNoteVoidId = Guid.Parse("00000000-0000-0000-0002-0000000000f2");
    private static readonly Guid AdminJournalVoucherVoidId = Guid.Parse("00000000-0000-0000-0002-0000000000f3");
    private static readonly Guid MemberJournalVoucherVoidId = Guid.Parse("00000000-0000-0000-0002-0000000000f4");
    private static readonly Guid AdminCashTransferVoidId = Guid.Parse("00000000-0000-0000-0002-0000000000f5");
    private static readonly Guid MemberCashTransferVoidId = Guid.Parse("00000000-0000-0000-0002-0000000000f6");
    private static readonly Guid AdminWarehouseTransferVoidId = Guid.Parse("00000000-0000-0000-0002-0000000000f7");
    private static readonly Guid MemberWarehouseTransferVoidId = Guid.Parse("00000000-0000-0000-0002-0000000000f8");
    private static readonly Guid AdminInventoryAdjustmentVoidId = Guid.Parse("00000000-0000-0000-0002-0000000000f9");
    private static readonly Guid MemberInventoryAdjustmentVoidId = Guid.Parse("00000000-0000-0000-0002-0000000000fa");

    private static readonly Guid AdminOrganizationLockDateManageId = Guid.Parse("00000000-0000-0000-0002-0000000000fb");
    private static readonly Guid MemberOrganizationLockDateManageId = Guid.Parse("00000000-0000-0000-0002-0000000000fc");

    private static readonly Guid AdminSystemAuditViewId = Guid.Parse("00000000-0000-0000-0002-0000000000fd");
    private static readonly Guid MemberSystemAuditViewId = Guid.Parse("00000000-0000-0000-0002-0000000000fe");

    // Phase 17 (Accounting breadth) -- continues the tail past 0xff into a new byte (see this
    // file's own header comment). Bank: same Member-View/Admin-Manage lookup split as every other
    // Configuration lookup. BankAccount: View-only new key (see PermissionKeys.cs's own comment on
    // why there's no BankAccountManage sibling). Cheque: standard View/Manage pair, both
    // Member-granted (routine daily-use working data, same bar as PaymentCreate/PaymentEdit --
    // status transitions here have no GL effect per decision #4, so they don't need the
    // Approve-style maker-checker split). OpeningBalance: View/Edit pair per FR-3.4's own confirmed
    // split, Admin-only Edit (a "day zero" balance correction after go-live is exactly the kind of
    // backdated-looking change LockDate exists to gate elsewhere -- Member gets View only).
    private static readonly Guid AdminBankViewId = Guid.Parse("00000000-0000-0000-0002-0000000000ff");
    private static readonly Guid AdminBankManageId = Guid.Parse("00000000-0000-0000-0002-000000000100");
    private static readonly Guid MemberBankViewId = Guid.Parse("00000000-0000-0000-0002-000000000101");
    private static readonly Guid MemberBankManageId = Guid.Parse("00000000-0000-0000-0002-000000000102");

    private static readonly Guid AdminBankAccountViewId = Guid.Parse("00000000-0000-0000-0002-000000000103");
    private static readonly Guid MemberBankAccountViewId = Guid.Parse("00000000-0000-0000-0002-000000000104");

    private static readonly Guid AdminChequeViewId = Guid.Parse("00000000-0000-0000-0002-000000000105");
    private static readonly Guid AdminChequeManageId = Guid.Parse("00000000-0000-0000-0002-000000000106");
    private static readonly Guid MemberChequeViewId = Guid.Parse("00000000-0000-0000-0002-000000000107");
    private static readonly Guid MemberChequeManageId = Guid.Parse("00000000-0000-0000-0002-000000000108");

    private static readonly Guid AdminOpeningBalanceViewId = Guid.Parse("00000000-0000-0000-0002-000000000109");
    private static readonly Guid AdminOpeningBalanceEditId = Guid.Parse("00000000-0000-0000-0002-00000000010a");
    private static readonly Guid MemberOpeningBalanceViewId = Guid.Parse("00000000-0000-0000-0002-00000000010b");
    private static readonly Guid MemberOpeningBalanceEditId = Guid.Parse("00000000-0000-0000-0002-00000000010c");

    // Phase 18 (CRM completion) -- see PermissionKeys.cs's own comment for the reasoning behind
    // each key's Admin/Member split. SmsSend is the one Admin-only key in this set (Member row is
    // an explicit IsGranted=false denial, matching every other Admin-only key's convention).
    private static readonly Guid AdminSmsSendId = Guid.Parse("00000000-0000-0000-0002-00000000010d");
    private static readonly Guid MemberSmsSendId = Guid.Parse("00000000-0000-0000-0002-00000000010e");

    private static readonly Guid AdminSmsTemplateViewId = Guid.Parse("00000000-0000-0000-0002-00000000010f");
    private static readonly Guid AdminSmsTemplateManageId = Guid.Parse("00000000-0000-0000-0002-000000000110");
    private static readonly Guid MemberSmsTemplateViewId = Guid.Parse("00000000-0000-0000-0002-000000000111");
    private static readonly Guid MemberSmsTemplateManageId = Guid.Parse("00000000-0000-0000-0002-000000000112");

    private static readonly Guid AdminSmsCreditLedgerViewId = Guid.Parse("00000000-0000-0000-0002-000000000113");
    private static readonly Guid AdminSmsCreditLedgerAdjustId = Guid.Parse("00000000-0000-0000-0002-000000000114");
    private static readonly Guid MemberSmsCreditLedgerViewId = Guid.Parse("00000000-0000-0000-0002-000000000115");
    private static readonly Guid MemberSmsCreditLedgerAdjustId = Guid.Parse("00000000-0000-0000-0002-000000000116");

    private static readonly Guid AdminSmsLogViewId = Guid.Parse("00000000-0000-0000-0002-000000000117");
    private static readonly Guid MemberSmsLogViewId = Guid.Parse("00000000-0000-0000-0002-000000000118");

    // Phase 19 (Reporting Tags + remaining reports) -- see PermissionKeys.cs's own comment for the
    // reasoning behind each key's Admin/Member split.
    private static readonly Guid AdminCashFlowSummaryViewId = Guid.Parse("00000000-0000-0000-0002-000000000119");
    private static readonly Guid MemberCashFlowSummaryViewId = Guid.Parse("00000000-0000-0000-0002-00000000011a");
    private static readonly Guid AdminSalesRegisterViewId = Guid.Parse("00000000-0000-0000-0002-00000000011b");
    private static readonly Guid MemberSalesRegisterViewId = Guid.Parse("00000000-0000-0000-0002-00000000011c");
    private static readonly Guid AdminPurchaseRegisterViewId = Guid.Parse("00000000-0000-0000-0002-00000000011d");
    private static readonly Guid MemberPurchaseRegisterViewId = Guid.Parse("00000000-0000-0000-0002-00000000011e");
    private static readonly Guid AdminStockAgeingViewId = Guid.Parse("00000000-0000-0000-0002-00000000011f");
    private static readonly Guid MemberStockAgeingViewId = Guid.Parse("00000000-0000-0000-0002-000000000120");
    private static readonly Guid AdminProductProfitabilityViewId = Guid.Parse("00000000-0000-0000-0002-000000000121");
    private static readonly Guid MemberProductProfitabilityViewId = Guid.Parse("00000000-0000-0000-0002-000000000122");
    private static readonly Guid AdminRatioAnalysisViewId = Guid.Parse("00000000-0000-0000-0002-000000000123");
    private static readonly Guid MemberRatioAnalysisViewId = Guid.Parse("00000000-0000-0000-0002-000000000124");

    // Phase 20c (Cost Terms) -- Member reads (Phase 25's BOM/Production forms need the picker),
    // Admin curates; see PermissionKeys.cs's CostTerm comment.
    private static readonly Guid AdminCostTermViewId = Guid.Parse("00000000-0000-0000-0002-000000000125");
    private static readonly Guid AdminCostTermManageId = Guid.Parse("00000000-0000-0000-0002-000000000126");
    private static readonly Guid MemberCostTermViewId = Guid.Parse("00000000-0000-0000-0002-000000000127");
    private static readonly Guid MemberCostTermManageId = Guid.Parse("00000000-0000-0000-0002-000000000128");

    // Phase 20d (Printing Templates / Custom Templates) -- Admin-only for all four keys; see
    // PermissionKeys.cs's own comment for the reasoning (no Member-facing picker reads either
    // table, unlike CostTerm/CreditTerm/PaymentMode above).
    private static readonly Guid AdminPrintingTemplateViewId = Guid.Parse("00000000-0000-0000-0002-000000000129");
    private static readonly Guid AdminPrintingTemplateManageId = Guid.Parse("00000000-0000-0000-0002-00000000012a");
    private static readonly Guid MemberPrintingTemplateViewId = Guid.Parse("00000000-0000-0000-0002-00000000012b");
    private static readonly Guid MemberPrintingTemplateManageId = Guid.Parse("00000000-0000-0000-0002-00000000012c");
    private static readonly Guid AdminCustomTemplateViewId = Guid.Parse("00000000-0000-0000-0002-00000000012d");
    private static readonly Guid AdminCustomTemplateManageId = Guid.Parse("00000000-0000-0000-0002-00000000012e");
    private static readonly Guid MemberCustomTemplateViewId = Guid.Parse("00000000-0000-0000-0002-00000000012f");
    private static readonly Guid MemberCustomTemplateManageId = Guid.Parse("00000000-0000-0000-0002-000000000130");

    // Phase 20f (tenant feature-flag enforcement) -- granted to both roles; the Angular shell
    // needs it for every user to render feature-gated nav correctly. See PermissionKeys'
    // SubscriptionView note for why this departs from Phase 20d's Admin-only control-plane bar.
    private static readonly Guid AdminSubscriptionViewId = Guid.Parse("00000000-0000-0000-0002-000000000131");
    private static readonly Guid MemberSubscriptionViewId = Guid.Parse("00000000-0000-0000-0002-000000000132");

    // Phase 20e (Alert Scheduler) -- Admin-only on all three, including View: an alert definition
    // records where the tenant's figures are being mailed, and the send log records every address
    // actually mailed. See PermissionKeys.AlertDefinitionView's note for the full derivation.
    private static readonly Guid AdminAlertDefinitionViewId = Guid.Parse("00000000-0000-0000-0002-000000000133");
    private static readonly Guid AdminAlertDefinitionManageId = Guid.Parse("00000000-0000-0000-0002-000000000134");
    private static readonly Guid MemberAlertDefinitionViewId = Guid.Parse("00000000-0000-0000-0002-000000000135");
    private static readonly Guid MemberAlertDefinitionManageId = Guid.Parse("00000000-0000-0000-0002-000000000136");
    private static readonly Guid AdminAlertSendLogViewId = Guid.Parse("00000000-0000-0000-0002-000000000137");
    private static readonly Guid MemberAlertSendLogViewId = Guid.Parse("00000000-0000-0000-0002-000000000138");

    // Phase 21a (Bulk import, FR-2.9). Admin-only for both -- see PermissionKeys.ImportJobManage /
    // ImportJobView for the derivation (mass master-data mutation on one side, an error report that
    // quotes uploaded PAN/phone/email back to the reader on the other).
    private static readonly Guid AdminImportJobViewId = Guid.Parse("00000000-0000-0000-0002-000000000139");
    private static readonly Guid AdminImportJobManageId = Guid.Parse("00000000-0000-0000-0002-00000000013a");
    private static readonly Guid MemberImportJobViewId = Guid.Parse("00000000-0000-0000-0002-00000000013b");
    private static readonly Guid MemberImportJobManageId = Guid.Parse("00000000-0000-0000-0002-00000000013c");

    // Phase 21b (Full-tenant data export, FR-2.8). Admin-only for both -- see
    // PermissionKeys.ExportJobView / ExportJobManage. The View key gates the download itself, which
    // is what actually controls whether the tenant's whole data set leaves the system.
    private static readonly Guid AdminExportJobViewId = Guid.Parse("00000000-0000-0000-0002-00000000013d");
    private static readonly Guid AdminExportJobManageId = Guid.Parse("00000000-0000-0000-0002-00000000013e");
    private static readonly Guid MemberExportJobViewId = Guid.Parse("00000000-0000-0000-0002-00000000013f");
    private static readonly Guid MemberExportJobManageId = Guid.Parse("00000000-0000-0000-0002-000000000140");

    // Phase 21c (Migrated tax-register import, FR-2.10). Admin-only for all three -- see
    // PermissionKeys.MigratedSalesRegisterView / MigratedPurchaseRegisterView /
    // MigratedRegisterManage for the derivation (the same flat-register-with-PAN bar as Phase 19's
    // live pair on the read side; on the write side, the least reviewable write in the product --
    // statutory numbers with no document, approval or GL posting behind them).
    private static readonly Guid AdminMigratedSalesRegisterViewId = Guid.Parse("00000000-0000-0000-0002-000000000141");
    private static readonly Guid AdminMigratedPurchaseRegisterViewId = Guid.Parse("00000000-0000-0000-0002-000000000142");
    private static readonly Guid AdminMigratedRegisterManageId = Guid.Parse("00000000-0000-0000-0002-000000000143");
    private static readonly Guid MemberMigratedSalesRegisterViewId = Guid.Parse("00000000-0000-0000-0002-000000000144");
    private static readonly Guid MemberMigratedPurchaseRegisterViewId = Guid.Parse("00000000-0000-0000-0002-000000000145");
    private static readonly Guid MemberMigratedRegisterManageId = Guid.Parse("00000000-0000-0000-0002-000000000146");

    // Phase 22 (Document inbox, FR-10.3). View/Manage are Admin+Member -- the inbox is routine
    // daily-use working data for whoever photographs the bills, and the transactions it produces
    // are already Member-visible. Extract and the tenant-wide AI consent toggle are Admin-only:
    // one spends money and sends a customer document to a third party, the other decides whether
    // that egress is permitted at all. See PermissionKeys.InboxDocument* for the full derivation.
    private static readonly Guid AdminInboxDocumentViewId = Guid.Parse("00000000-0000-0000-0002-000000000147");
    private static readonly Guid AdminInboxDocumentManageId = Guid.Parse("00000000-0000-0000-0002-000000000148");
    private static readonly Guid AdminInboxDocumentExtractId = Guid.Parse("00000000-0000-0000-0002-000000000149");
    private static readonly Guid AdminAiDocumentExtractionManageId = Guid.Parse("00000000-0000-0000-0002-00000000014a");
    private static readonly Guid MemberInboxDocumentViewId = Guid.Parse("00000000-0000-0000-0002-00000000014b");
    private static readonly Guid MemberInboxDocumentManageId = Guid.Parse("00000000-0000-0000-0002-00000000014c");
    private static readonly Guid MemberInboxDocumentExtractId = Guid.Parse("00000000-0000-0000-0002-00000000014d");
    private static readonly Guid MemberAiDocumentExtractionManageId = Guid.Parse("00000000-0000-0000-0002-00000000014e");

    // Phase 23 (Home dashboard recent-activity feed). Admin+Member. This key only gets the request
    // past AuthorizationBehavior's org-membership check -- what the feed actually shows is gated per
    // document type inside RecentTransactionsQueryHandler against each type's own *.View grant, so
    // granting this on its own shows an empty feed rather than anything new. See
    // PermissionKeys.RecentTransactionView for the full derivation.
    private static readonly Guid AdminRecentTransactionViewId = Guid.Parse("00000000-0000-0000-0002-00000000014f");
    private static readonly Guid MemberRecentTransactionViewId = Guid.Parse("00000000-0000-0000-0002-000000000150");

    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("RolePermissions", schema: "tenancy");

        builder.HasKey(rp => rp.Id);

        builder.Property(rp => rp.PermissionKey).HasMaxLength(200).IsRequired();
        builder.Property(rp => rp.IsGranted).IsRequired();

        builder.HasIndex(rp => new { rp.RoleId, rp.PermissionKey }).IsUnique();

        builder.HasOne<Role>()
            .WithMany()
            .HasForeignKey(rp => rp.RoleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasData(
            RolePermission.Create(AdminInviteUserId, Role.AdminId, PermissionKeys.OrganizationInviteUser, true),
            RolePermission.Create(AdminAcceptRequestId, Role.AdminId, PermissionKeys.OrganizationAcceptRequest, true),
            RolePermission.Create(MemberInviteUserId, Role.MemberId, PermissionKeys.OrganizationInviteUser, false),
            RolePermission.Create(MemberAcceptRequestId, Role.MemberId, PermissionKeys.OrganizationAcceptRequest, false),

            RolePermission.Create(AdminCreditTermViewId, Role.AdminId, PermissionKeys.CreditTermView, true),
            RolePermission.Create(AdminCreditTermManageId, Role.AdminId, PermissionKeys.CreditTermManage, true),
            RolePermission.Create(MemberCreditTermViewId, Role.MemberId, PermissionKeys.CreditTermView, true),
            RolePermission.Create(MemberCreditTermManageId, Role.MemberId, PermissionKeys.CreditTermManage, false),

            RolePermission.Create(AdminPaymentModeViewId, Role.AdminId, PermissionKeys.PaymentModeView, true),
            RolePermission.Create(AdminPaymentModeManageId, Role.AdminId, PermissionKeys.PaymentModeManage, true),
            RolePermission.Create(MemberPaymentModeViewId, Role.MemberId, PermissionKeys.PaymentModeView, true),
            RolePermission.Create(MemberPaymentModeManageId, Role.MemberId, PermissionKeys.PaymentModeManage, false),

            RolePermission.Create(AdminCustomStatusViewId, Role.AdminId, PermissionKeys.CustomStatusView, true),
            RolePermission.Create(AdminCustomStatusManageId, Role.AdminId, PermissionKeys.CustomStatusManage, true),
            RolePermission.Create(MemberCustomStatusViewId, Role.MemberId, PermissionKeys.CustomStatusView, true),
            RolePermission.Create(MemberCustomStatusManageId, Role.MemberId, PermissionKeys.CustomStatusManage, false),

            RolePermission.Create(AdminReportingTagCategoryViewId, Role.AdminId, PermissionKeys.ReportingTagCategoryView, true),
            RolePermission.Create(AdminReportingTagCategoryManageId, Role.AdminId, PermissionKeys.ReportingTagCategoryManage, true),
            RolePermission.Create(MemberReportingTagCategoryViewId, Role.MemberId, PermissionKeys.ReportingTagCategoryView, true),
            RolePermission.Create(MemberReportingTagCategoryManageId, Role.MemberId, PermissionKeys.ReportingTagCategoryManage, false),

            RolePermission.Create(AdminReportingTagOptionViewId, Role.AdminId, PermissionKeys.ReportingTagOptionView, true),
            RolePermission.Create(AdminReportingTagOptionManageId, Role.AdminId, PermissionKeys.ReportingTagOptionManage, true),
            RolePermission.Create(MemberReportingTagOptionViewId, Role.MemberId, PermissionKeys.ReportingTagOptionView, true),
            RolePermission.Create(MemberReportingTagOptionManageId, Role.MemberId, PermissionKeys.ReportingTagOptionManage, false),

            RolePermission.Create(AdminCustomFieldDefinitionViewId, Role.AdminId, PermissionKeys.CustomFieldDefinitionView, true),
            RolePermission.Create(AdminCustomFieldDefinitionManageId, Role.AdminId, PermissionKeys.CustomFieldDefinitionManage, true),
            RolePermission.Create(MemberCustomFieldDefinitionViewId, Role.MemberId, PermissionKeys.CustomFieldDefinitionView, true),
            RolePermission.Create(MemberCustomFieldDefinitionManageId, Role.MemberId, PermissionKeys.CustomFieldDefinitionManage, false),

            RolePermission.Create(AdminContactGroupViewId, Role.AdminId, PermissionKeys.ContactGroupView, true),
            RolePermission.Create(AdminContactGroupManageId, Role.AdminId, PermissionKeys.ContactGroupManage, true),
            RolePermission.Create(MemberContactGroupViewId, Role.MemberId, PermissionKeys.ContactGroupView, true),
            RolePermission.Create(MemberContactGroupManageId, Role.MemberId, PermissionKeys.ContactGroupManage, false),

            RolePermission.Create(AdminContactViewId, Role.AdminId, PermissionKeys.ContactView, true),
            RolePermission.Create(AdminContactManageId, Role.AdminId, PermissionKeys.ContactManage, true),
            RolePermission.Create(MemberContactViewId, Role.MemberId, PermissionKeys.ContactView, true),
            RolePermission.Create(MemberContactManageId, Role.MemberId, PermissionKeys.ContactManage, true),

            RolePermission.Create(AdminProductCategoryViewId, Role.AdminId, PermissionKeys.ProductCategoryView, true),
            RolePermission.Create(AdminProductCategoryManageId, Role.AdminId, PermissionKeys.ProductCategoryManage, true),
            RolePermission.Create(MemberProductCategoryViewId, Role.MemberId, PermissionKeys.ProductCategoryView, true),
            RolePermission.Create(MemberProductCategoryManageId, Role.MemberId, PermissionKeys.ProductCategoryManage, false),

            RolePermission.Create(AdminUnitOfMeasurementViewId, Role.AdminId, PermissionKeys.UnitOfMeasurementView, true),
            RolePermission.Create(AdminUnitOfMeasurementManageId, Role.AdminId, PermissionKeys.UnitOfMeasurementManage, true),
            RolePermission.Create(MemberUnitOfMeasurementViewId, Role.MemberId, PermissionKeys.UnitOfMeasurementView, true),
            RolePermission.Create(MemberUnitOfMeasurementManageId, Role.MemberId, PermissionKeys.UnitOfMeasurementManage, false),

            RolePermission.Create(AdminProductViewId, Role.AdminId, PermissionKeys.ProductView, true),
            RolePermission.Create(AdminProductManageId, Role.AdminId, PermissionKeys.ProductManage, true),
            RolePermission.Create(MemberProductViewId, Role.MemberId, PermissionKeys.ProductView, true),
            RolePermission.Create(MemberProductManageId, Role.MemberId, PermissionKeys.ProductManage, true),

            RolePermission.Create(AdminAccountGroupViewId, Role.AdminId, PermissionKeys.AccountGroupView, true),
            RolePermission.Create(AdminAccountGroupManageId, Role.AdminId, PermissionKeys.AccountGroupManage, true),
            RolePermission.Create(MemberAccountGroupViewId, Role.MemberId, PermissionKeys.AccountGroupView, true),
            RolePermission.Create(MemberAccountGroupManageId, Role.MemberId, PermissionKeys.AccountGroupManage, false),

            RolePermission.Create(AdminAccountViewId, Role.AdminId, PermissionKeys.AccountView, true),
            RolePermission.Create(AdminAccountManageId, Role.AdminId, PermissionKeys.AccountManage, true),
            RolePermission.Create(MemberAccountViewId, Role.MemberId, PermissionKeys.AccountView, true),
            RolePermission.Create(MemberAccountManageId, Role.MemberId, PermissionKeys.AccountManage, false),

            RolePermission.Create(AdminJournalVoucherViewId, Role.AdminId, PermissionKeys.JournalVoucherView, true),
            RolePermission.Create(AdminJournalVoucherCreateId, Role.AdminId, PermissionKeys.JournalVoucherCreate, true),
            RolePermission.Create(AdminJournalVoucherEditId, Role.AdminId, PermissionKeys.JournalVoucherEdit, true),
            RolePermission.Create(AdminJournalVoucherApproveId, Role.AdminId, PermissionKeys.JournalVoucherApprove, true),
            RolePermission.Create(MemberJournalVoucherViewId, Role.MemberId, PermissionKeys.JournalVoucherView, true),
            RolePermission.Create(MemberJournalVoucherCreateId, Role.MemberId, PermissionKeys.JournalVoucherCreate, true),
            RolePermission.Create(MemberJournalVoucherEditId, Role.MemberId, PermissionKeys.JournalVoucherEdit, true),
            RolePermission.Create(MemberJournalVoucherApproveId, Role.MemberId, PermissionKeys.JournalVoucherApprove, false),

            RolePermission.Create(AdminCashTransferViewId, Role.AdminId, PermissionKeys.CashTransferView, true),
            RolePermission.Create(AdminCashTransferCreateId, Role.AdminId, PermissionKeys.CashTransferCreate, true),
            RolePermission.Create(AdminCashTransferEditId, Role.AdminId, PermissionKeys.CashTransferEdit, true),
            RolePermission.Create(AdminCashTransferApproveId, Role.AdminId, PermissionKeys.CashTransferApprove, true),
            RolePermission.Create(MemberCashTransferViewId, Role.MemberId, PermissionKeys.CashTransferView, true),
            RolePermission.Create(MemberCashTransferCreateId, Role.MemberId, PermissionKeys.CashTransferCreate, true),
            RolePermission.Create(MemberCashTransferEditId, Role.MemberId, PermissionKeys.CashTransferEdit, true),
            RolePermission.Create(MemberCashTransferApproveId, Role.MemberId, PermissionKeys.CashTransferApprove, false),

            RolePermission.Create(AdminWarehouseViewId, Role.AdminId, PermissionKeys.WarehouseView, true),
            RolePermission.Create(AdminWarehouseManageId, Role.AdminId, PermissionKeys.WarehouseManage, true),
            RolePermission.Create(MemberWarehouseViewId, Role.MemberId, PermissionKeys.WarehouseView, true),
            RolePermission.Create(MemberWarehouseManageId, Role.MemberId, PermissionKeys.WarehouseManage, false),

            RolePermission.Create(AdminQuotationViewId, Role.AdminId, PermissionKeys.QuotationView, true),
            RolePermission.Create(AdminQuotationCreateId, Role.AdminId, PermissionKeys.QuotationCreate, true),
            RolePermission.Create(AdminQuotationEditId, Role.AdminId, PermissionKeys.QuotationEdit, true),
            RolePermission.Create(AdminQuotationApproveId, Role.AdminId, PermissionKeys.QuotationApprove, true),
            RolePermission.Create(MemberQuotationViewId, Role.MemberId, PermissionKeys.QuotationView, true),
            RolePermission.Create(MemberQuotationCreateId, Role.MemberId, PermissionKeys.QuotationCreate, true),
            RolePermission.Create(MemberQuotationEditId, Role.MemberId, PermissionKeys.QuotationEdit, true),
            RolePermission.Create(MemberQuotationApproveId, Role.MemberId, PermissionKeys.QuotationApprove, false),

            RolePermission.Create(AdminSalesOrderViewId, Role.AdminId, PermissionKeys.SalesOrderView, true),
            RolePermission.Create(AdminSalesOrderCreateId, Role.AdminId, PermissionKeys.SalesOrderCreate, true),
            RolePermission.Create(AdminSalesOrderEditId, Role.AdminId, PermissionKeys.SalesOrderEdit, true),
            RolePermission.Create(AdminSalesOrderApproveId, Role.AdminId, PermissionKeys.SalesOrderApprove, true),
            RolePermission.Create(MemberSalesOrderViewId, Role.MemberId, PermissionKeys.SalesOrderView, true),
            RolePermission.Create(MemberSalesOrderCreateId, Role.MemberId, PermissionKeys.SalesOrderCreate, true),
            RolePermission.Create(MemberSalesOrderEditId, Role.MemberId, PermissionKeys.SalesOrderEdit, true),
            RolePermission.Create(MemberSalesOrderApproveId, Role.MemberId, PermissionKeys.SalesOrderApprove, false),

            RolePermission.Create(AdminInvoiceViewId, Role.AdminId, PermissionKeys.InvoiceView, true),
            RolePermission.Create(AdminInvoiceCreateId, Role.AdminId, PermissionKeys.InvoiceCreate, true),
            RolePermission.Create(AdminInvoiceEditId, Role.AdminId, PermissionKeys.InvoiceEdit, true),
            RolePermission.Create(AdminInvoiceApproveId, Role.AdminId, PermissionKeys.InvoiceApprove, true),
            RolePermission.Create(MemberInvoiceViewId, Role.MemberId, PermissionKeys.InvoiceView, true),
            RolePermission.Create(MemberInvoiceCreateId, Role.MemberId, PermissionKeys.InvoiceCreate, true),
            RolePermission.Create(MemberInvoiceEditId, Role.MemberId, PermissionKeys.InvoiceEdit, true),
            RolePermission.Create(MemberInvoiceApproveId, Role.MemberId, PermissionKeys.InvoiceApprove, false),

            RolePermission.Create(AdminCreditNoteViewId, Role.AdminId, PermissionKeys.CreditNoteView, true),
            RolePermission.Create(AdminCreditNoteCreateId, Role.AdminId, PermissionKeys.CreditNoteCreate, true),
            RolePermission.Create(AdminCreditNoteEditId, Role.AdminId, PermissionKeys.CreditNoteEdit, true),
            RolePermission.Create(AdminCreditNoteApproveId, Role.AdminId, PermissionKeys.CreditNoteApprove, true),
            RolePermission.Create(MemberCreditNoteViewId, Role.MemberId, PermissionKeys.CreditNoteView, true),
            RolePermission.Create(MemberCreditNoteCreateId, Role.MemberId, PermissionKeys.CreditNoteCreate, true),
            RolePermission.Create(MemberCreditNoteEditId, Role.MemberId, PermissionKeys.CreditNoteEdit, true),
            RolePermission.Create(MemberCreditNoteApproveId, Role.MemberId, PermissionKeys.CreditNoteApprove, false),

            RolePermission.Create(AdminPaymentViewId, Role.AdminId, PermissionKeys.PaymentView, true),
            RolePermission.Create(AdminPaymentCreateId, Role.AdminId, PermissionKeys.PaymentCreate, true),
            RolePermission.Create(AdminPaymentEditId, Role.AdminId, PermissionKeys.PaymentEdit, true),
            RolePermission.Create(AdminPaymentApproveId, Role.AdminId, PermissionKeys.PaymentApprove, true),
            RolePermission.Create(MemberPaymentViewId, Role.MemberId, PermissionKeys.PaymentView, true),
            RolePermission.Create(MemberPaymentCreateId, Role.MemberId, PermissionKeys.PaymentCreate, true),
            RolePermission.Create(MemberPaymentEditId, Role.MemberId, PermissionKeys.PaymentEdit, true),
            RolePermission.Create(MemberPaymentApproveId, Role.MemberId, PermissionKeys.PaymentApprove, false),

            RolePermission.Create(AdminAccountingDefaultsManageId, Role.AdminId, PermissionKeys.AccountingDefaultsManage, true),
            RolePermission.Create(MemberAccountingDefaultsManageId, Role.MemberId, PermissionKeys.AccountingDefaultsManage, false),

            RolePermission.Create(AdminTdsTypeViewId, Role.AdminId, PermissionKeys.TdsTypeView, true),
            RolePermission.Create(AdminTdsTypeManageId, Role.AdminId, PermissionKeys.TdsTypeManage, true),
            RolePermission.Create(MemberTdsTypeViewId, Role.MemberId, PermissionKeys.TdsTypeView, true),
            RolePermission.Create(MemberTdsTypeManageId, Role.MemberId, PermissionKeys.TdsTypeManage, false),

            RolePermission.Create(AdminPurchaseOrderViewId, Role.AdminId, PermissionKeys.PurchaseOrderView, true),
            RolePermission.Create(AdminPurchaseOrderCreateId, Role.AdminId, PermissionKeys.PurchaseOrderCreate, true),
            RolePermission.Create(AdminPurchaseOrderEditId, Role.AdminId, PermissionKeys.PurchaseOrderEdit, true),
            RolePermission.Create(AdminPurchaseOrderApproveId, Role.AdminId, PermissionKeys.PurchaseOrderApprove, true),
            RolePermission.Create(MemberPurchaseOrderViewId, Role.MemberId, PermissionKeys.PurchaseOrderView, true),
            RolePermission.Create(MemberPurchaseOrderCreateId, Role.MemberId, PermissionKeys.PurchaseOrderCreate, true),
            RolePermission.Create(MemberPurchaseOrderEditId, Role.MemberId, PermissionKeys.PurchaseOrderEdit, true),
            RolePermission.Create(MemberPurchaseOrderApproveId, Role.MemberId, PermissionKeys.PurchaseOrderApprove, false),

            RolePermission.Create(AdminPurchaseBillViewId, Role.AdminId, PermissionKeys.PurchaseBillView, true),
            RolePermission.Create(AdminPurchaseBillCreateId, Role.AdminId, PermissionKeys.PurchaseBillCreate, true),
            RolePermission.Create(AdminPurchaseBillEditId, Role.AdminId, PermissionKeys.PurchaseBillEdit, true),
            RolePermission.Create(AdminPurchaseBillApproveId, Role.AdminId, PermissionKeys.PurchaseBillApprove, true),
            RolePermission.Create(MemberPurchaseBillViewId, Role.MemberId, PermissionKeys.PurchaseBillView, true),
            RolePermission.Create(MemberPurchaseBillCreateId, Role.MemberId, PermissionKeys.PurchaseBillCreate, true),
            RolePermission.Create(MemberPurchaseBillEditId, Role.MemberId, PermissionKeys.PurchaseBillEdit, true),
            RolePermission.Create(MemberPurchaseBillApproveId, Role.MemberId, PermissionKeys.PurchaseBillApprove, false),

            RolePermission.Create(AdminExpenseViewId, Role.AdminId, PermissionKeys.ExpenseView, true),
            RolePermission.Create(AdminExpenseCreateId, Role.AdminId, PermissionKeys.ExpenseCreate, true),
            RolePermission.Create(AdminExpenseEditId, Role.AdminId, PermissionKeys.ExpenseEdit, true),
            RolePermission.Create(AdminExpenseApproveId, Role.AdminId, PermissionKeys.ExpenseApprove, true),
            RolePermission.Create(MemberExpenseViewId, Role.MemberId, PermissionKeys.ExpenseView, true),
            RolePermission.Create(MemberExpenseCreateId, Role.MemberId, PermissionKeys.ExpenseCreate, true),
            RolePermission.Create(MemberExpenseEditId, Role.MemberId, PermissionKeys.ExpenseEdit, true),
            RolePermission.Create(MemberExpenseApproveId, Role.MemberId, PermissionKeys.ExpenseApprove, false),

            RolePermission.Create(AdminDebitNoteViewId, Role.AdminId, PermissionKeys.DebitNoteView, true),
            RolePermission.Create(AdminDebitNoteCreateId, Role.AdminId, PermissionKeys.DebitNoteCreate, true),
            RolePermission.Create(AdminDebitNoteEditId, Role.AdminId, PermissionKeys.DebitNoteEdit, true),
            RolePermission.Create(AdminDebitNoteApproveId, Role.AdminId, PermissionKeys.DebitNoteApprove, true),
            RolePermission.Create(MemberDebitNoteViewId, Role.MemberId, PermissionKeys.DebitNoteView, true),
            RolePermission.Create(MemberDebitNoteCreateId, Role.MemberId, PermissionKeys.DebitNoteCreate, true),
            RolePermission.Create(MemberDebitNoteEditId, Role.MemberId, PermissionKeys.DebitNoteEdit, true),
            RolePermission.Create(MemberDebitNoteApproveId, Role.MemberId, PermissionKeys.DebitNoteApprove, false),

            RolePermission.Create(AdminWarehouseTransferViewId, Role.AdminId, PermissionKeys.WarehouseTransferView, true),
            RolePermission.Create(AdminWarehouseTransferCreateId, Role.AdminId, PermissionKeys.WarehouseTransferCreate, true),
            RolePermission.Create(AdminWarehouseTransferEditId, Role.AdminId, PermissionKeys.WarehouseTransferEdit, true),
            RolePermission.Create(AdminWarehouseTransferApproveId, Role.AdminId, PermissionKeys.WarehouseTransferApprove, true),
            RolePermission.Create(MemberWarehouseTransferViewId, Role.MemberId, PermissionKeys.WarehouseTransferView, true),
            RolePermission.Create(MemberWarehouseTransferCreateId, Role.MemberId, PermissionKeys.WarehouseTransferCreate, true),
            RolePermission.Create(MemberWarehouseTransferEditId, Role.MemberId, PermissionKeys.WarehouseTransferEdit, true),
            RolePermission.Create(MemberWarehouseTransferApproveId, Role.MemberId, PermissionKeys.WarehouseTransferApprove, false),

            RolePermission.Create(AdminInventoryAdjustmentViewId, Role.AdminId, PermissionKeys.InventoryAdjustmentView, true),
            RolePermission.Create(AdminInventoryAdjustmentCreateId, Role.AdminId, PermissionKeys.InventoryAdjustmentCreate, true),
            RolePermission.Create(AdminInventoryAdjustmentEditId, Role.AdminId, PermissionKeys.InventoryAdjustmentEdit, true),
            RolePermission.Create(AdminInventoryAdjustmentApproveId, Role.AdminId, PermissionKeys.InventoryAdjustmentApprove, true),
            RolePermission.Create(MemberInventoryAdjustmentViewId, Role.MemberId, PermissionKeys.InventoryAdjustmentView, true),
            RolePermission.Create(MemberInventoryAdjustmentCreateId, Role.MemberId, PermissionKeys.InventoryAdjustmentCreate, true),
            RolePermission.Create(MemberInventoryAdjustmentEditId, Role.MemberId, PermissionKeys.InventoryAdjustmentEdit, true),
            RolePermission.Create(MemberInventoryAdjustmentApproveId, Role.MemberId, PermissionKeys.InventoryAdjustmentApprove, false),

            RolePermission.Create(AdminInventoryLedgerViewId, Role.AdminId, PermissionKeys.InventoryLedgerView, true),
            RolePermission.Create(MemberInventoryLedgerViewId, Role.MemberId, PermissionKeys.InventoryLedgerView, true),

            RolePermission.Create(AdminTrialBalanceViewId, Role.AdminId, PermissionKeys.TrialBalanceView, true),
            RolePermission.Create(MemberTrialBalanceViewId, Role.MemberId, PermissionKeys.TrialBalanceView, true),
            RolePermission.Create(AdminBalanceSheetViewId, Role.AdminId, PermissionKeys.BalanceSheetView, true),
            RolePermission.Create(MemberBalanceSheetViewId, Role.MemberId, PermissionKeys.BalanceSheetView, true),
            RolePermission.Create(AdminIncomeStatementViewId, Role.AdminId, PermissionKeys.IncomeStatementView, true),
            RolePermission.Create(MemberIncomeStatementViewId, Role.MemberId, PermissionKeys.IncomeStatementView, true),

            RolePermission.Create(AdminSalesMasterReportViewId, Role.AdminId, PermissionKeys.SalesMasterReportView, true),
            RolePermission.Create(MemberSalesMasterReportViewId, Role.MemberId, PermissionKeys.SalesMasterReportView, false),
            RolePermission.Create(AdminPurchaseMasterReportViewId, Role.AdminId, PermissionKeys.PurchaseMasterReportView, true),
            RolePermission.Create(MemberPurchaseMasterReportViewId, Role.MemberId, PermissionKeys.PurchaseMasterReportView, false),

            RolePermission.Create(AdminVatSummaryViewId, Role.AdminId, PermissionKeys.VatSummaryView, true),
            RolePermission.Create(MemberVatSummaryViewId, Role.MemberId, PermissionKeys.VatSummaryView, true),

            RolePermission.Create(AdminTdsReportViewId, Role.AdminId, PermissionKeys.TdsReportView, true),
            RolePermission.Create(MemberTdsReportViewId, Role.MemberId, PermissionKeys.TdsReportView, false),

            RolePermission.Create(AdminAnnexThirteenViewId, Role.AdminId, PermissionKeys.AnnexThirteenView, true),
            RolePermission.Create(MemberAnnexThirteenViewId, Role.MemberId, PermissionKeys.AnnexThirteenView, false),

            RolePermission.Create(AdminAnnexFiveViewId, Role.AdminId, PermissionKeys.AnnexFiveView, true),
            RolePermission.Create(MemberAnnexFiveViewId, Role.MemberId, PermissionKeys.AnnexFiveView, false),

            RolePermission.Create(AdminCustomerAgeingSummaryViewId, Role.AdminId, PermissionKeys.CustomerAgeingSummaryView, true),
            RolePermission.Create(MemberCustomerAgeingSummaryViewId, Role.MemberId, PermissionKeys.CustomerAgeingSummaryView, false),
            RolePermission.Create(AdminSupplierAgeingSummaryViewId, Role.AdminId, PermissionKeys.SupplierAgeingSummaryView, true),
            RolePermission.Create(MemberSupplierAgeingSummaryViewId, Role.MemberId, PermissionKeys.SupplierAgeingSummaryView, false),
            RolePermission.Create(AdminCustomerStatementViewId, Role.AdminId, PermissionKeys.CustomerStatementView, true),
            RolePermission.Create(MemberCustomerStatementViewId, Role.MemberId, PermissionKeys.CustomerStatementView, false),
            RolePermission.Create(AdminSupplierStatementViewId, Role.AdminId, PermissionKeys.SupplierStatementView, true),
            RolePermission.Create(MemberSupplierStatementViewId, Role.MemberId, PermissionKeys.SupplierStatementView, false),

            RolePermission.Create(AdminTransactionApprovalViewId, Role.AdminId, PermissionKeys.TransactionApprovalView, true),
            RolePermission.Create(MemberTransactionApprovalViewId, Role.MemberId, PermissionKeys.TransactionApprovalView, true),

            RolePermission.Create(AdminTaskViewId, Role.AdminId, PermissionKeys.TaskView, true),
            RolePermission.Create(AdminTaskManageId, Role.AdminId, PermissionKeys.TaskManage, true),
            RolePermission.Create(MemberTaskViewId, Role.MemberId, PermissionKeys.TaskView, true),
            RolePermission.Create(MemberTaskManageId, Role.MemberId, PermissionKeys.TaskManage, true),

            RolePermission.Create(AdminTaskTypeViewId, Role.AdminId, PermissionKeys.TaskTypeView, true),
            RolePermission.Create(AdminTaskTypeManageId, Role.AdminId, PermissionKeys.TaskTypeManage, true),
            RolePermission.Create(MemberTaskTypeViewId, Role.MemberId, PermissionKeys.TaskTypeView, true),
            RolePermission.Create(MemberTaskTypeManageId, Role.MemberId, PermissionKeys.TaskTypeManage, false),

            RolePermission.Create(AdminRoleViewId, Role.AdminId, PermissionKeys.RoleView, true),
            RolePermission.Create(AdminRoleManageId, Role.AdminId, PermissionKeys.RoleManage, true),
            RolePermission.Create(MemberRoleViewId, Role.MemberId, PermissionKeys.RoleView, false),
            RolePermission.Create(MemberRoleManageId, Role.MemberId, PermissionKeys.RoleManage, false),

            RolePermission.Create(AdminDealViewId, Role.AdminId, PermissionKeys.DealView, true),
            RolePermission.Create(AdminDealManageId, Role.AdminId, PermissionKeys.DealManage, true),
            RolePermission.Create(MemberDealViewId, Role.MemberId, PermissionKeys.DealView, true),
            RolePermission.Create(MemberDealManageId, Role.MemberId, PermissionKeys.DealManage, true),

            RolePermission.Create(AdminLeadSourceViewId, Role.AdminId, PermissionKeys.LeadSourceView, true),
            RolePermission.Create(AdminLeadSourceManageId, Role.AdminId, PermissionKeys.LeadSourceManage, true),
            RolePermission.Create(MemberLeadSourceViewId, Role.MemberId, PermissionKeys.LeadSourceView, true),
            RolePermission.Create(MemberLeadSourceManageId, Role.MemberId, PermissionKeys.LeadSourceManage, false),

            RolePermission.Create(AdminDealStageViewId, Role.AdminId, PermissionKeys.DealStageView, true),
            RolePermission.Create(AdminDealStageManageId, Role.AdminId, PermissionKeys.DealStageManage, true),
            RolePermission.Create(MemberDealStageViewId, Role.MemberId, PermissionKeys.DealStageView, true),
            RolePermission.Create(MemberDealStageManageId, Role.MemberId, PermissionKeys.DealStageManage, false),

            RolePermission.Create(AdminQuotationVoidId, Role.AdminId, PermissionKeys.QuotationVoid, true),
            RolePermission.Create(MemberQuotationVoidId, Role.MemberId, PermissionKeys.QuotationVoid, false),
            RolePermission.Create(AdminSalesOrderVoidId, Role.AdminId, PermissionKeys.SalesOrderVoid, true),
            RolePermission.Create(MemberSalesOrderVoidId, Role.MemberId, PermissionKeys.SalesOrderVoid, false),
            RolePermission.Create(AdminInvoiceVoidId, Role.AdminId, PermissionKeys.InvoiceVoid, true),
            RolePermission.Create(MemberInvoiceVoidId, Role.MemberId, PermissionKeys.InvoiceVoid, false),
            RolePermission.Create(AdminCreditNoteVoidId, Role.AdminId, PermissionKeys.CreditNoteVoid, true),
            RolePermission.Create(MemberCreditNoteVoidId, Role.MemberId, PermissionKeys.CreditNoteVoid, false),
            RolePermission.Create(AdminPaymentVoidId, Role.AdminId, PermissionKeys.PaymentVoid, true),
            RolePermission.Create(MemberPaymentVoidId, Role.MemberId, PermissionKeys.PaymentVoid, false),
            RolePermission.Create(AdminPurchaseOrderVoidId, Role.AdminId, PermissionKeys.PurchaseOrderVoid, true),
            RolePermission.Create(MemberPurchaseOrderVoidId, Role.MemberId, PermissionKeys.PurchaseOrderVoid, false),
            RolePermission.Create(AdminPurchaseBillVoidId, Role.AdminId, PermissionKeys.PurchaseBillVoid, true),
            RolePermission.Create(MemberPurchaseBillVoidId, Role.MemberId, PermissionKeys.PurchaseBillVoid, false),
            RolePermission.Create(AdminExpenseVoidId, Role.AdminId, PermissionKeys.ExpenseVoid, true),
            RolePermission.Create(MemberExpenseVoidId, Role.MemberId, PermissionKeys.ExpenseVoid, false),
            RolePermission.Create(AdminDebitNoteVoidId, Role.AdminId, PermissionKeys.DebitNoteVoid, true),
            RolePermission.Create(MemberDebitNoteVoidId, Role.MemberId, PermissionKeys.DebitNoteVoid, false),
            RolePermission.Create(AdminJournalVoucherVoidId, Role.AdminId, PermissionKeys.JournalVoucherVoid, true),
            RolePermission.Create(MemberJournalVoucherVoidId, Role.MemberId, PermissionKeys.JournalVoucherVoid, false),
            RolePermission.Create(AdminCashTransferVoidId, Role.AdminId, PermissionKeys.CashTransferVoid, true),
            RolePermission.Create(MemberCashTransferVoidId, Role.MemberId, PermissionKeys.CashTransferVoid, false),
            RolePermission.Create(AdminWarehouseTransferVoidId, Role.AdminId, PermissionKeys.WarehouseTransferVoid, true),
            RolePermission.Create(MemberWarehouseTransferVoidId, Role.MemberId, PermissionKeys.WarehouseTransferVoid, false),
            RolePermission.Create(AdminInventoryAdjustmentVoidId, Role.AdminId, PermissionKeys.InventoryAdjustmentVoid, true),
            RolePermission.Create(MemberInventoryAdjustmentVoidId, Role.MemberId, PermissionKeys.InventoryAdjustmentVoid, false),

            RolePermission.Create(
                AdminOrganizationLockDateManageId, Role.AdminId, PermissionKeys.OrganizationLockDateManage, true),
            RolePermission.Create(
                MemberOrganizationLockDateManageId, Role.MemberId, PermissionKeys.OrganizationLockDateManage, false),

            RolePermission.Create(AdminSystemAuditViewId, Role.AdminId, PermissionKeys.SystemAuditView, true),
            RolePermission.Create(MemberSystemAuditViewId, Role.MemberId, PermissionKeys.SystemAuditView, false),

            RolePermission.Create(AdminBankViewId, Role.AdminId, PermissionKeys.BankView, true),
            RolePermission.Create(AdminBankManageId, Role.AdminId, PermissionKeys.BankManage, true),
            RolePermission.Create(MemberBankViewId, Role.MemberId, PermissionKeys.BankView, true),
            RolePermission.Create(MemberBankManageId, Role.MemberId, PermissionKeys.BankManage, false),

            RolePermission.Create(AdminBankAccountViewId, Role.AdminId, PermissionKeys.BankAccountView, true),
            RolePermission.Create(MemberBankAccountViewId, Role.MemberId, PermissionKeys.BankAccountView, true),

            RolePermission.Create(AdminChequeViewId, Role.AdminId, PermissionKeys.ChequeView, true),
            RolePermission.Create(AdminChequeManageId, Role.AdminId, PermissionKeys.ChequeManage, true),
            RolePermission.Create(MemberChequeViewId, Role.MemberId, PermissionKeys.ChequeView, true),
            RolePermission.Create(MemberChequeManageId, Role.MemberId, PermissionKeys.ChequeManage, true),

            RolePermission.Create(AdminOpeningBalanceViewId, Role.AdminId, PermissionKeys.OpeningBalanceView, true),
            RolePermission.Create(AdminOpeningBalanceEditId, Role.AdminId, PermissionKeys.OpeningBalanceEdit, true),
            RolePermission.Create(MemberOpeningBalanceViewId, Role.MemberId, PermissionKeys.OpeningBalanceView, true),
            RolePermission.Create(MemberOpeningBalanceEditId, Role.MemberId, PermissionKeys.OpeningBalanceEdit, false),

            RolePermission.Create(AdminSmsSendId, Role.AdminId, PermissionKeys.SmsSend, true),
            RolePermission.Create(MemberSmsSendId, Role.MemberId, PermissionKeys.SmsSend, false),

            RolePermission.Create(AdminSmsTemplateViewId, Role.AdminId, PermissionKeys.SmsTemplateView, true),
            RolePermission.Create(AdminSmsTemplateManageId, Role.AdminId, PermissionKeys.SmsTemplateManage, true),
            RolePermission.Create(MemberSmsTemplateViewId, Role.MemberId, PermissionKeys.SmsTemplateView, true),
            RolePermission.Create(MemberSmsTemplateManageId, Role.MemberId, PermissionKeys.SmsTemplateManage, false),

            RolePermission.Create(AdminSmsCreditLedgerViewId, Role.AdminId, PermissionKeys.SmsCreditLedgerView, true),
            RolePermission.Create(AdminSmsCreditLedgerAdjustId, Role.AdminId, PermissionKeys.SmsCreditLedgerAdjust, true),
            RolePermission.Create(MemberSmsCreditLedgerViewId, Role.MemberId, PermissionKeys.SmsCreditLedgerView, true),
            RolePermission.Create(MemberSmsCreditLedgerAdjustId, Role.MemberId, PermissionKeys.SmsCreditLedgerAdjust, false),

            RolePermission.Create(AdminSmsLogViewId, Role.AdminId, PermissionKeys.SmsLogView, true),
            RolePermission.Create(MemberSmsLogViewId, Role.MemberId, PermissionKeys.SmsLogView, true),

            RolePermission.Create(AdminCashFlowSummaryViewId, Role.AdminId, PermissionKeys.CashFlowSummaryView, true),
            RolePermission.Create(MemberCashFlowSummaryViewId, Role.MemberId, PermissionKeys.CashFlowSummaryView, true),

            RolePermission.Create(AdminSalesRegisterViewId, Role.AdminId, PermissionKeys.SalesRegisterView, true),
            RolePermission.Create(MemberSalesRegisterViewId, Role.MemberId, PermissionKeys.SalesRegisterView, false),

            RolePermission.Create(AdminPurchaseRegisterViewId, Role.AdminId, PermissionKeys.PurchaseRegisterView, true),
            RolePermission.Create(MemberPurchaseRegisterViewId, Role.MemberId, PermissionKeys.PurchaseRegisterView, false),

            RolePermission.Create(AdminStockAgeingViewId, Role.AdminId, PermissionKeys.StockAgeingView, true),
            RolePermission.Create(MemberStockAgeingViewId, Role.MemberId, PermissionKeys.StockAgeingView, true),

            RolePermission.Create(AdminProductProfitabilityViewId, Role.AdminId, PermissionKeys.ProductProfitabilityView, true),
            RolePermission.Create(MemberProductProfitabilityViewId, Role.MemberId, PermissionKeys.ProductProfitabilityView, false),

            RolePermission.Create(AdminRatioAnalysisViewId, Role.AdminId, PermissionKeys.RatioAnalysisView, true),
            RolePermission.Create(MemberRatioAnalysisViewId, Role.MemberId, PermissionKeys.RatioAnalysisView, true),

            RolePermission.Create(AdminCostTermViewId, Role.AdminId, PermissionKeys.CostTermView, true),
            RolePermission.Create(AdminCostTermManageId, Role.AdminId, PermissionKeys.CostTermManage, true),
            RolePermission.Create(MemberCostTermViewId, Role.MemberId, PermissionKeys.CostTermView, true),
            RolePermission.Create(MemberCostTermManageId, Role.MemberId, PermissionKeys.CostTermManage, false),
            RolePermission.Create(AdminPrintingTemplateViewId, Role.AdminId, PermissionKeys.PrintingTemplateView, true),
            RolePermission.Create(AdminPrintingTemplateManageId, Role.AdminId, PermissionKeys.PrintingTemplateManage, true),
            RolePermission.Create(MemberPrintingTemplateViewId, Role.MemberId, PermissionKeys.PrintingTemplateView, false),
            RolePermission.Create(MemberPrintingTemplateManageId, Role.MemberId, PermissionKeys.PrintingTemplateManage, false),
            RolePermission.Create(AdminCustomTemplateViewId, Role.AdminId, PermissionKeys.CustomTemplateView, true),
            RolePermission.Create(AdminCustomTemplateManageId, Role.AdminId, PermissionKeys.CustomTemplateManage, true),
            RolePermission.Create(MemberCustomTemplateViewId, Role.MemberId, PermissionKeys.CustomTemplateView, false),
            RolePermission.Create(MemberCustomTemplateManageId, Role.MemberId, PermissionKeys.CustomTemplateManage, false),

            RolePermission.Create(AdminSubscriptionViewId, Role.AdminId, PermissionKeys.SubscriptionView, true),
            RolePermission.Create(MemberSubscriptionViewId, Role.MemberId, PermissionKeys.SubscriptionView, true),

            RolePermission.Create(AdminAlertDefinitionViewId, Role.AdminId, PermissionKeys.AlertDefinitionView, true),
            RolePermission.Create(AdminAlertDefinitionManageId, Role.AdminId, PermissionKeys.AlertDefinitionManage, true),
            RolePermission.Create(MemberAlertDefinitionViewId, Role.MemberId, PermissionKeys.AlertDefinitionView, false),
            RolePermission.Create(MemberAlertDefinitionManageId, Role.MemberId, PermissionKeys.AlertDefinitionManage, false),
            RolePermission.Create(AdminAlertSendLogViewId, Role.AdminId, PermissionKeys.AlertSendLogView, true),
            RolePermission.Create(MemberAlertSendLogViewId, Role.MemberId, PermissionKeys.AlertSendLogView, false),
            RolePermission.Create(AdminImportJobViewId, Role.AdminId, PermissionKeys.ImportJobView, true),
            RolePermission.Create(AdminImportJobManageId, Role.AdminId, PermissionKeys.ImportJobManage, true),
            RolePermission.Create(MemberImportJobViewId, Role.MemberId, PermissionKeys.ImportJobView, false),
            RolePermission.Create(MemberImportJobManageId, Role.MemberId, PermissionKeys.ImportJobManage, false),

            RolePermission.Create(AdminExportJobViewId, Role.AdminId, PermissionKeys.ExportJobView, true),
            RolePermission.Create(AdminExportJobManageId, Role.AdminId, PermissionKeys.ExportJobManage, true),
            RolePermission.Create(MemberExportJobViewId, Role.MemberId, PermissionKeys.ExportJobView, false),
            RolePermission.Create(MemberExportJobManageId, Role.MemberId, PermissionKeys.ExportJobManage, false),

            RolePermission.Create(AdminMigratedSalesRegisterViewId, Role.AdminId, PermissionKeys.MigratedSalesRegisterView, true),
            RolePermission.Create(AdminMigratedPurchaseRegisterViewId, Role.AdminId, PermissionKeys.MigratedPurchaseRegisterView, true),
            RolePermission.Create(AdminMigratedRegisterManageId, Role.AdminId, PermissionKeys.MigratedRegisterManage, true),
            RolePermission.Create(MemberMigratedSalesRegisterViewId, Role.MemberId, PermissionKeys.MigratedSalesRegisterView, false),
            RolePermission.Create(MemberMigratedPurchaseRegisterViewId, Role.MemberId, PermissionKeys.MigratedPurchaseRegisterView, false),
            RolePermission.Create(MemberMigratedRegisterManageId, Role.MemberId, PermissionKeys.MigratedRegisterManage, false),

            RolePermission.Create(AdminInboxDocumentViewId, Role.AdminId, PermissionKeys.InboxDocumentView, true),
            RolePermission.Create(AdminInboxDocumentManageId, Role.AdminId, PermissionKeys.InboxDocumentManage, true),
            RolePermission.Create(AdminInboxDocumentExtractId, Role.AdminId, PermissionKeys.InboxDocumentExtract, true),
            RolePermission.Create(AdminAiDocumentExtractionManageId, Role.AdminId, PermissionKeys.AiDocumentExtractionManage, true),
            RolePermission.Create(MemberInboxDocumentViewId, Role.MemberId, PermissionKeys.InboxDocumentView, true),
            RolePermission.Create(MemberInboxDocumentManageId, Role.MemberId, PermissionKeys.InboxDocumentManage, true),
            RolePermission.Create(MemberInboxDocumentExtractId, Role.MemberId, PermissionKeys.InboxDocumentExtract, false),
            RolePermission.Create(MemberAiDocumentExtractionManageId, Role.MemberId, PermissionKeys.AiDocumentExtractionManage, false),

            RolePermission.Create(AdminRecentTransactionViewId, Role.AdminId, PermissionKeys.RecentTransactionView, true),
            RolePermission.Create(MemberRecentTransactionViewId, Role.MemberId, PermissionKeys.RecentTransactionView, true));
    }
}
