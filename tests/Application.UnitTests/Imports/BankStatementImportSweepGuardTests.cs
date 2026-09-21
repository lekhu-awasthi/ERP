using ErpApp.Application.Imports;
using ErpApp.Application.Imports.Commands.CreateImportJob;
using ErpApp.Domain.Imports;

namespace ErpApp.Application.UnitTests.Imports;

/// <summary>
/// Phase 55 -- <c>ImportJob.BankAccountId</c> is a field meaningful to exactly one of ten
/// <see cref="ImportEntityType"/> members, which is a smell paid for with a rule. This asserts the
/// rule in <b>both</b> directions, because only one of them is the interesting one.
///
/// <para>Requiring it for <c>BankStatement</c> is the obvious half: without it the importer throws
/// mid-run on a file the user cannot fix. <i>Refusing</i> it for the other nine is the half that
/// would otherwise rot -- an account quietly accepted on a Product import and then ignored reads
/// to a client as accepted, which is phase 43's present-and-ignored shape, and nothing else in the
/// tree would ever notice.</para>
///
/// <para>Driven from the enum rather than from a list of names, so a tenth entity type added
/// without a decision fails here instead of silently joining the "may not name an account" side
/// (phase 50's rule that a guard must be driven from the thing that can change).</para>
/// </summary>
public class BankStatementImportSweepGuardTests
{
    private static readonly CreateImportJobCommandValidator Validator = new();

    [Fact]
    public void A_bank_statement_import_must_name_a_bank_account()
    {
        var result = Validator.Validate(Command(ImportEntityType.BankStatement, bankAccountId: null));

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            e => e.PropertyName == nameof(CreateImportJobCommand.BankAccountId));
    }

    [Fact]
    public void A_bank_statement_import_naming_one_is_accepted()
    {
        Assert.True(Validator.Validate(Command(ImportEntityType.BankStatement, Guid.NewGuid())).IsValid);
    }

    [Theory]
    [MemberData(nameof(EveryOtherEntityType))]
    public void No_other_import_type_may_name_a_bank_account(ImportEntityType entityType)
    {
        var result = Validator.Validate(Command(entityType, Guid.NewGuid()));

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            e => e.PropertyName == nameof(CreateImportJobCommand.BankAccountId));
    }

    /// <summary>The premise half: without it, a rename of the enum member would make the theory
    /// above run over every type and pass vacuously (phase 54's UnitlessOutputHeaders lesson).</summary>
    [Fact]
    public void Exactly_one_entity_type_carries_an_account()
    {
        var all = Enum.GetValues<ImportEntityType>();

        Assert.Contains(ImportEntityType.BankStatement, all);
        Assert.Equal(all.Length - 1, EveryOtherEntityType().Count());
    }

    /// <summary>
    /// An importer must exist for the type, or the enum member is an upload that 500s on its first
    /// row -- the mirror of the registration line in <c>DependencyInjection</c>.
    /// </summary>
    [Fact]
    public void The_bank_statement_importer_claims_the_entity_type()
    {
        Assert.Equal(ImportEntityType.BankStatement, new BankStatementImporter().EntityType);
        Assert.Equal(ImportEntityType.BankStatement, new BankStatementImporter().Template.EntityType);
    }

    public static IEnumerable<object[]> EveryOtherEntityType() =>
        Enum.GetValues<ImportEntityType>()
            .Where(t => t != ImportEntityType.BankStatement)
            .Select(t => new object[] { t });

    private static CreateImportJobCommand Command(ImportEntityType entityType, Guid? bankAccountId) =>
        new(Guid.NewGuid(), entityType, ImportMode.CreateNew, "statement.xlsx", 1024, Stream.Null, bankAccountId);
}
