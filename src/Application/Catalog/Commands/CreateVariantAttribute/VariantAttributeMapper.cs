using ErpApp.Domain.Catalog;

namespace ErpApp.Application.Catalog.Commands.CreateVariantAttribute;

/// <summary>Shared by every VariantAttribute command/query so one shape reaches the client.</summary>
public static class VariantAttributeMapper
{
    public static VariantAttributeResult ToResult(VariantAttribute attribute) =>
        new(attribute.Id,
            attribute.Name,
            attribute.IsActive,
            attribute.Options
                .OrderBy(x => x.SortOrder)
                .Select(x => new VariantAttributeOptionResult(x.Id, x.Value, x.SortOrder, x.IsActive))
                .ToList());
}
