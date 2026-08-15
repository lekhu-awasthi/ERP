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
            RolePermission.Create(MemberAnnexFiveViewId, Role.MemberId, PermissionKeys.AnnexFiveView, false));
    }
}
