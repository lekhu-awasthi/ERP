using ErpApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ErpApp.Infrastructure.UnitTests.TestSupport;

/// <summary>
/// The real <see cref="AppDbContext"/> model, built once. EF builds a model from the configurations
/// and the conventions without opening a connection, so this needs no database and no Docker -- the
/// connection string below is never dialled.
///
/// <para>It must be the SQL Server provider and not InMemory: the indexes under test carry
/// provider-specific metadata (descending key order, INCLUDE columns, filters), and the InMemory
/// provider does not model an index at all.</para>
/// </summary>
public sealed class ModelFixture
{
    public ModelFixture()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=(unused);Database=ModelOnly;Trusted_Connection=True;")
            .Options;

        using var context = new AppDbContext(options);
        Model = context.Model;
    }

    public IModel Model { get; }

    public IEntityType Entity(string clrTypeName) =>
        Model.GetEntityTypes().SingleOrDefault(e => e.ClrType.Name == clrTypeName)
        ?? throw new InvalidOperationException($"No entity type named '{clrTypeName}' is mapped.");

    /// <summary>
    /// True when some index on the entity leads on <c>(OrganizationId, column)</c>. Leading is the
    /// whole question -- a column buried at position three of a composite is not an ordering the
    /// optimizer can satisfy without a sort, which is the reason <c>ListSort</c> exists.
    /// </summary>
    public static bool HasIndexLeadingOn(IEntityType entity, string column) =>
        entity.GetIndexes().Any(i =>
            i.Properties.Count >= 2
            && i.Properties[0].Name == "OrganizationId"
            && i.Properties[1].Name == column);
}

[CollectionDefinition(nameof(ModelCollection))]
public sealed class ModelCollection : ICollectionFixture<ModelFixture>;
