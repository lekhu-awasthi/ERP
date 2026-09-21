using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Imports;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Imports;

/// <summary>
/// Phase 45 -- the Attributes Used importer, phase 38's carried ergonomic gap.
///
/// <para><b>What is worth asserting here is the thing this importer does differently from every
/// other one</b>: a row <i>adds</i> to a set the command it sends would otherwise replace. So the
/// load-bearing tests are the ones proving accumulation across rows (two rows naming one product
/// leave that product offering both options, not the last one) and the refusal to remove
/// (re-running a file changes nothing, and no row can drop an option a variant is built from).
/// </para>
/// </summary>
public class ProductAttributePoolImportTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 10, 0, 0, TimeSpan.Zero);

    private static readonly string[] PoolHeaders = ["Product Code", "Attribute", "Value"];

    /// <summary>
    /// The whole point of the type: several rows naming one product build up one pool. A wholesale
    /// replace per row -- which is what SetProductVariantAttributesCommand does on its own -- would
    /// leave this product offering only "Large".
    /// </summary>
    [Fact]
    public async Task Rows_naming_one_product_accumulate_into_a_single_pool()
    {
        using var host = new ImportTestHost(Now);
        var db = host.NewDbContext();
        var seed = await SeedAsync(db);

        host.FileReader.Returns(
            PoolHeaders,
            ["PRD-0001", "Colour", "Blue"],
            ["PRD-0001", "Colour", "Red"],
            ["PRD-0001", "Size", "Large"]);

        var job = await RunAsync(host, db, seed.Tenant);

        Assert.Equal(ImportJobStatus.Completed, job.Status);
        Assert.Equal(3, job.SucceededRowCount);
        Assert.Equal(0, job.FailedRowCount);

        var read = host.NewDbContext();
        var usages = await read.ProductVariantAttributeUsages
            .Where(x => x.ProductId == seed.ProductId)
            .ToListAsync();

        Assert.Equal(3, usages.Count);

        // ...and the product is now a variant parent, which is the ergonomic outcome the phase-38
        // gap was blocking: the variant importer refuses a row whose parent offers nothing.
        var product = await read.Products.SingleAsync(x => x.Id == seed.ProductId);
        Assert.True(product.HasVariants);
    }

    [Fact]
    public async Task Re_running_the_same_file_changes_nothing()
    {
        using var host = new ImportTestHost(Now);
        var db = host.NewDbContext();
        var seed = await SeedAsync(db);

        host.FileReader.Returns(PoolHeaders, ["PRD-0001", "Colour", "Blue"], ["PRD-0001", "Colour", "Red"]);
        await RunAsync(host, db, seed.Tenant);

        host.FileReader.Returns(PoolHeaders, ["PRD-0001", "Colour", "Blue"], ["PRD-0001", "Colour", "Red"]);
        var second = await RunAsync(host, host.NewDbContext(), seed.Tenant);

        Assert.Equal(2, second.SucceededRowCount);
        Assert.Equal(0, second.FailedRowCount);

        var read = host.NewDbContext();
        Assert.Equal(
            2,
            await read.ProductVariantAttributeUsages.CountAsync(x => x.ProductId == seed.ProductId));
    }

    /// <summary>
    /// An import adds and never removes, so a file that omits an option the product already offers
    /// leaves it alone -- and therefore can never strand a variant built from it. Removal stays on
    /// the product's own Variants tab, where the refusal that protects those children lives.
    /// </summary>
    [Fact]
    public async Task A_file_that_omits_an_existing_option_does_not_remove_it()
    {
        using var host = new ImportTestHost(Now);
        var db = host.NewDbContext();
        var seed = await SeedAsync(db);

        host.FileReader.Returns(PoolHeaders, ["PRD-0001", "Colour", "Blue"], ["PRD-0001", "Colour", "Red"]);
        await RunAsync(host, db, seed.Tenant);

        host.FileReader.Returns(PoolHeaders, ["PRD-0001", "Size", "Large"]);
        await RunAsync(host, host.NewDbContext(), seed.Tenant);

        var read = host.NewDbContext();
        Assert.Equal(
            3,
            await read.ProductVariantAttributeUsages.CountAsync(x => x.ProductId == seed.ProductId));
    }

    [Fact]
    public async Task An_unknown_product_attribute_or_option_is_one_row_error_and_the_rest_imports()
    {
        using var host = new ImportTestHost(Now);
        var db = host.NewDbContext();
        var seed = await SeedAsync(db);

        host.FileReader.Returns(
            PoolHeaders,
            ["PRD-9999", "Colour", "Blue"],
            ["PRD-0001", "Nonexistent", "Blue"],
            ["PRD-0001", "Colour", "Chartreuse"],
            ["PRD-0001", "Colour", "Blue"]);

        var job = await RunAsync(host, db, seed.Tenant);

        Assert.Equal(ImportJobStatus.Completed, job.Status);
        Assert.Equal(1, job.SucceededRowCount);
        Assert.Equal(3, job.FailedRowCount);

        var read = host.NewDbContext();
        var failures = await read.ImportJobRows
            .Where(r => r.ImportJobId == job.Id && r.Message != null)
            .ToListAsync();

        // Each failure names the column the user has to fix, not just "row 3".
        Assert.Contains(failures, f => f.ColumnName == "Product Code");
        Assert.Contains(failures, f => f.ColumnName == "Attribute");
        Assert.Contains(failures, f => f.ColumnName == "Value");

        Assert.Equal(
            1,
            await read.ProductVariantAttributeUsages.CountAsync(x => x.ProductId == seed.ProductId));
    }

    /// <summary>
    /// A variant child cannot offer options of its own. The command 409s, but that reaches the row
    /// ledger as an untyped message; rejecting it in PlanAsync makes it a row error the review step
    /// can show before anything is written.
    /// </summary>
    [Fact]
    public async Task A_row_naming_a_variant_child_is_refused_with_the_column_named()
    {
        using var host = new ImportTestHost(Now);
        var db = host.NewDbContext();
        var seed = await SeedAsync(db);

        host.FileReader.Returns(PoolHeaders, ["PRD-0001", "Colour", "Blue"]);
        await RunAsync(host, db, seed.Tenant);

        // Promote the product and give it a child, then aim a row at the child.
        var write = host.NewDbContext();
        var parent = await write.Products
            .Include(x => x.VariantAttributeUsages)
            .SingleAsync(x => x.Id == seed.ProductId);
        var child = parent.CreateVariant(
            "PRD-0002", "Widget Blue", [(seed.ColourAttributeId, seed.BlueOptionId)], 100m, 80m, null, null);
        write.Products.Add(child);
        write.ProductVariantValues.AddRange(child.VariantValues);
        await write.SaveChangesAsync(CancellationToken.None);

        host.FileReader.Returns(PoolHeaders, ["PRD-0002", "Size", "Large"]);
        var job = await RunAsync(host, host.NewDbContext(), seed.Tenant);

        Assert.Equal(1, job.FailedRowCount);

        var read = host.NewDbContext();
        var failed = await read.ImportJobRows.SingleAsync(r => r.ImportJobId == job.Id && r.Message != null);
        Assert.Equal("Product Code", failed.ColumnName);
        Assert.Contains("is itself a variant", failed.Message!, StringComparison.Ordinal);
    }

    /// <summary>
    /// Phase 24 read live that attribute names are deliberately not unique -- the reference tenant
    /// carries both "size" and "Size" -- so this importer must resolve an ambiguous name to a row
    /// error rather than to whichever row came back first.
    /// </summary>
    [Fact]
    public async Task An_ambiguous_attribute_name_is_a_row_error_rather_than_a_coin_flip()
    {
        using var host = new ImportTestHost(Now);
        var db = host.NewDbContext();
        var seed = await SeedAsync(db);

        var write = host.NewDbContext();
        var duplicate = VariantAttribute.Create(seed.Tenant.OrganizationId, "Colour");
        var option = duplicate.AddOption("Blue");
        write.VariantAttributes.Add(duplicate);
        write.VariantAttributeOptions.Add(option);
        await write.SaveChangesAsync(CancellationToken.None);

        host.FileReader.Returns(PoolHeaders, ["PRD-0001", "Colour", "Blue"]);
        var job = await RunAsync(host, host.NewDbContext(), seed.Tenant);

        Assert.Equal(1, job.FailedRowCount);

        var read = host.NewDbContext();
        var failed = await read.ImportJobRows.SingleAsync(r => r.ImportJobId == job.Id && r.Message != null);
        Assert.Equal("Attribute", failed.ColumnName);
        Assert.Contains("more than one", failed.Message!, StringComparison.Ordinal);
    }

    [Fact]
    public void Update_mode_is_rejected_at_upload()
    {
        var command = new ErpApp.Application.Imports.Commands.CreateImportJob.CreateImportJobCommand(
            Guid.NewGuid(), ImportEntityType.ProductAttributePool, ImportMode.UpdateExisting,
            "pool.xlsx", 1024, Stream.Null, BankAccountId: null);

        var result = new ErpApp.Application.Imports.Commands.CreateImportJob.CreateImportJobCommandValidator()
            .Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            e => e.ErrorMessage.Contains("can only create records", StringComparison.Ordinal));
    }

    private sealed record PoolSeed(
        ImportTenant Tenant, Guid ProductId, Guid ColourAttributeId, Guid BlueOptionId);

    private static async Task<PoolSeed> SeedAsync(IAppDbContext db)
    {
        var tenant = await ImportTestSeed.SeedAsync(
            db, PermissionKeys.ImportJobManage, PermissionKeys.ProductManage);

        var product = Product.Create(
            tenant.OrganizationId, ProductType.Goods, "Widget", "PRD-0001", tenant.CategoryId,
            tenant.UnitId, null, true, 100m, 80m, VatRate.NoVat, 0, true);
        db.Products.Add(product);

        var colour = VariantAttribute.Create(tenant.OrganizationId, "Colour");
        var blue = colour.AddOption("Blue");
        var red = colour.AddOption("Red");
        var size = VariantAttribute.Create(tenant.OrganizationId, "Size");
        var large = size.AddOption("Large");

        db.VariantAttributes.AddRange(colour, size);
        db.VariantAttributeOptions.AddRange(blue, red, large);
        await db.SaveChangesAsync();

        return new PoolSeed(tenant, product.Id, colour.Id, blue.Id);
    }

    private static async Task<ImportJob> RunAsync(ImportTestHost host, IAppDbContext db, ImportTenant tenant)
    {
        var jobId = await ImportTestSeed.QueueJobAsync(
            db, tenant, ImportEntityType.ProductAttributePool, ImportMode.CreateNew, Now, host.FileStorage);

        Assert.True(await host.NewProcessor().ProcessNextAsync(CancellationToken.None));

        return await host.NewDbContext().ImportJobs.AsNoTracking().SingleAsync(j => j.Id == jobId);
    }
}
