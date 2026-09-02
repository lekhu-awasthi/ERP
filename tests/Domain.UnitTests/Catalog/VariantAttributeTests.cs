using ErpApp.Domain.Catalog;

namespace ErpApp.Domain.UnitTests.Catalog;

public class VariantAttributeTests
{
    [Fact]
    public void AddOption_appends_in_order()
    {
        var attribute = VariantAttribute.Create(Guid.NewGuid(), "Size");

        attribute.AddOption("S");
        attribute.AddOption("M");

        Assert.Equal(["S", "M"], attribute.Options.OrderBy(x => x.SortOrder).Select(x => x.Value));
        Assert.Equal([0, 1], attribute.Options.OrderBy(x => x.SortOrder).Select(x => x.SortOrder));
    }

    [Fact]
    public void AddOption_rejects_a_duplicate_value_case_insensitively()
    {
        var attribute = VariantAttribute.Create(Guid.NewGuid(), "Color");
        attribute.AddOption("Red");

        Assert.Throws<InvalidOperationException>(() => attribute.AddOption("red"));
    }

    [Fact]
    public void AddOption_rejects_a_duplicate_of_an_inactive_option_too()
    {
        // Retiring "Large" and re-adding it would otherwise create a second option that variant
        // generation treats as distinct from the one existing variants are built on.
        var attribute = VariantAttribute.Create(Guid.NewGuid(), "Size");
        var large = attribute.AddOption("Large");
        attribute.DeactivateOption(large.Id);

        Assert.Throws<InvalidOperationException>(() => attribute.AddOption("Large"));
    }

    [Fact]
    public void Deactivating_an_option_retires_it_without_removing_it()
    {
        var attribute = VariantAttribute.Create(Guid.NewGuid(), "Size");
        var option = attribute.AddOption("XXL");

        attribute.DeactivateOption(option.Id);

        Assert.Single(attribute.Options);
        Assert.False(attribute.Options[0].IsActive);
    }

    [Fact]
    public void Renaming_an_option_keeps_its_identity_so_existing_variants_follow_the_new_label()
    {
        var attribute = VariantAttribute.Create(Guid.NewGuid(), "Color");
        var option = attribute.AddOption("Blu");

        attribute.RenameOption(option.Id, "Blue");

        Assert.Equal(option.Id, attribute.Options[0].Id);
        Assert.Equal("Blue", attribute.Options[0].Value);
    }

    [Fact]
    public void Renaming_onto_another_existing_value_is_rejected()
    {
        var attribute = VariantAttribute.Create(Guid.NewGuid(), "Color");
        attribute.AddOption("Red");
        var blue = attribute.AddOption("Blue");

        Assert.Throws<InvalidOperationException>(() => attribute.RenameOption(blue.Id, "red"));
    }

    [Fact]
    public void Values_and_names_are_trimmed()
    {
        var attribute = VariantAttribute.Create(Guid.NewGuid(), "  Color  ");
        attribute.AddOption("  Red  ");

        Assert.Equal("Color", attribute.Name);
        Assert.Equal("Red", attribute.Options[0].Value);
    }

    [Fact]
    public void An_unknown_option_id_is_rejected()
    {
        var attribute = VariantAttribute.Create(Guid.NewGuid(), "Color");

        Assert.Throws<InvalidOperationException>(() => attribute.DeactivateOption(Guid.NewGuid()));
    }
}
