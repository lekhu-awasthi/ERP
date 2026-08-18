using ErpApp.Domain.Accounting;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Configuration;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Tenancy;

namespace ErpApp.Application.Common.Security;

/// <summary>
/// Resolves the View/Manage PermissionKeys (see <see cref="PermissionKeys"/>) for the generic
/// ListLookupsQuery&lt;TLookup&gt;/DeleteLookupCommand&lt;TLookup&gt; (Application.Configuration).
/// A small switch, not a reflection/attribute lookup -- the key string genuinely differs per
/// closed TLookup, and five cases is well within "a few similar lines beats a generic-string
/// templating abstraction" territory (see this repo's CLAUDE.md).
/// </summary>
public static class LookupPermissionKeys
{
    public static string ViewKeyFor<TLookup>() where TLookup : class, ITenantLookupEntity =>
        typeof(TLookup) switch
        {
            var t when t == typeof(CreditTerm) => PermissionKeys.CreditTermView,
            var t when t == typeof(PaymentMode) => PermissionKeys.PaymentModeView,
            var t when t == typeof(CustomStatus) => PermissionKeys.CustomStatusView,
            var t when t == typeof(ReportingTagCategory) => PermissionKeys.ReportingTagCategoryView,
            var t when t == typeof(ReportingTagOption) => PermissionKeys.ReportingTagOptionView,
            var t when t == typeof(ContactGroup) => PermissionKeys.ContactGroupView,
            var t when t == typeof(ProductCategory) => PermissionKeys.ProductCategoryView,
            var t when t == typeof(UnitOfMeasurement) => PermissionKeys.UnitOfMeasurementView,
            var t when t == typeof(AccountGroup) => PermissionKeys.AccountGroupView,
            var t when t == typeof(Warehouse) => PermissionKeys.WarehouseView,
            var t when t == typeof(TdsType) => PermissionKeys.TdsTypeView,
            var t when t == typeof(TaskType) => PermissionKeys.TaskTypeView,
            _ => throw new NotSupportedException($"No View permission key registered for lookup type {typeof(TLookup).Name}."),
        };

    public static string ManageKeyFor<TLookup>() where TLookup : class, ITenantLookupEntity =>
        typeof(TLookup) switch
        {
            var t when t == typeof(CreditTerm) => PermissionKeys.CreditTermManage,
            var t when t == typeof(PaymentMode) => PermissionKeys.PaymentModeManage,
            var t when t == typeof(CustomStatus) => PermissionKeys.CustomStatusManage,
            var t when t == typeof(ReportingTagCategory) => PermissionKeys.ReportingTagCategoryManage,
            var t when t == typeof(ReportingTagOption) => PermissionKeys.ReportingTagOptionManage,
            var t when t == typeof(ContactGroup) => PermissionKeys.ContactGroupManage,
            var t when t == typeof(ProductCategory) => PermissionKeys.ProductCategoryManage,
            var t when t == typeof(UnitOfMeasurement) => PermissionKeys.UnitOfMeasurementManage,
            var t when t == typeof(AccountGroup) => PermissionKeys.AccountGroupManage,
            var t when t == typeof(Warehouse) => PermissionKeys.WarehouseManage,
            var t when t == typeof(TdsType) => PermissionKeys.TdsTypeManage,
            var t when t == typeof(TaskType) => PermissionKeys.TaskTypeManage,
            _ => throw new NotSupportedException($"No Manage permission key registered for lookup type {typeof(TLookup).Name}."),
        };
}
