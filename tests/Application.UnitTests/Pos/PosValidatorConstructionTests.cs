using System.Runtime.CompilerServices;
using ErpApp.Application.Pos.Commands.CreatePosOrder;
using FluentValidation;

namespace ErpApp.Application.UnitTests.Pos;

/// <summary>
/// Phase 64 bug 1 -- a validator that registers one member under two property types
/// (<c>IEnumerable&lt;T&gt;</c> in a shared helper, <c>IReadOnlyList&lt;T&gt;</c> in the validator) throws
/// <c>InvalidCastException</c> when it is <i>built</i>, so every endpoint it guards is a 500 and every
/// handler test, which never builds the validator, passes. This builds every POS validator and runs it
/// over an empty request, which is the whole of what that failure needs.
/// </summary>
public class PosValidatorConstructionTests
{
    private static IReadOnlyList<Type> ValidatorTypes() =>
        [.. typeof(CreatePosOrderCommand).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false })
            .Where(t => t.Namespace?.StartsWith("ErpApp.Application.Pos", StringComparison.Ordinal) == true)
            .Where(t => t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IValidator<>)))
            .OrderBy(t => t.FullName, StringComparer.Ordinal)];

    public static TheoryData<Type> PosValidators()
    {
        var data = new TheoryData<Type>();
        foreach (var type in ValidatorTypes())
        {
            data.Add(type);
        }

        return data;
    }

    [Fact]
    public void The_sweep_finds_the_restaurant_validators()
    {
        var names = ValidatorTypes().Select(t => t.Name).ToList();

        Assert.Contains("CreatePosOrderCommandValidator", names);
        Assert.Contains("VoidPosOrderItemsCommandValidator", names);
        Assert.Contains("SavePosAreaLayoutCommandValidator", names);
        Assert.True(names.Count >= 25, $"Only {names.Count} POS validators were found.");
    }

    [Theory]
    [MemberData(nameof(PosValidators))]
    public async Task Every_POS_validator_builds_and_runs(Type validatorType)
    {
        var validator = (IValidator)Activator.CreateInstance(validatorType)!;
        var requestType = validatorType.GetInterfaces()
            .Single(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IValidator<>))
            .GetGenericArguments()[0];

        var empty = RuntimeHelpers.GetUninitializedObject(requestType);
        var context = (IValidationContext)Activator.CreateInstance(
            typeof(ValidationContext<>).MakeGenericType(requestType), empty)!;

        var result = await validator.ValidateAsync(context);

        Assert.NotNull(result);
    }
}
