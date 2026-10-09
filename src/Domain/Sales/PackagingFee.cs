namespace ErpApp.Domain.Sales;

/// <summary>Phase 70 -- how a packaging fee is worked out. Member 0 is <see cref="None"/>, so a missing
/// settings row and an ERP line both read as "no fee" without a nullable projection (phase 58's gotcha
/// about an enum whose zero means something).</summary>
public enum PackagingFeeBasis
{
    None = 0,

    /// <summary>A percent of the line's amount after its discounts, the base service charge is taken on.</summary>
    Percent = 1,

    /// <summary>A flat amount for each unit of the line, in the unit the line was entered in (Rs 20 a plate).</summary>
    PerUnit = 2,
}

/// <summary>
/// Phase 70 -- the terms of a packaging fee: a location's setting, and the same terms frozen on every line
/// that charges one (phase 68's rule: a rate a setting decides is decided once, at the action).
///
/// <para><b>One value and its basis, not a rate and an amount.</b> Two columns of which exactly one may be
/// non-zero is redundant state guarded by a rule (phase 55); the basis says what the one value means.</para>
///
/// <para><b>Its default is a valid value</b>, "no fee", because C# hands every caller a
/// <c>default(PackagingFee)</c> whatever the constructor says (phase 55's <c>StatementAmount</c> hole). The
/// constructor is private and <see cref="Create"/> refuses a value its basis cannot mean, so the only
/// unchecked instance is the one that charges nothing.</para>
/// </summary>
public readonly record struct PackagingFee
{
    /// <summary>A bound, not a rule: more than any box costs.</summary>
    public const decimal MaxPerUnit = 10_000m;

    public static readonly PackagingFee None = default;

    public PackagingFeeBasis Basis { get; }

    /// <summary>Percent for <see cref="PackagingFeeBasis.Percent"/>, rupees a unit for
    /// <see cref="PackagingFeeBasis.PerUnit"/>, zero for <see cref="PackagingFeeBasis.None"/>.</summary>
    public decimal Value { get; }

    private PackagingFee(PackagingFeeBasis basis, decimal value)
    {
        Basis = basis;
        Value = value;
    }

    public bool IsCharged => Basis != PackagingFeeBasis.None;

    public static PackagingFee Create(PackagingFeeBasis basis, decimal value)
    {
        switch (basis)
        {
            case PackagingFeeBasis.None:
                return None;

            case PackagingFeeBasis.Percent when value <= 0m || value > 100m:
                throw new InvalidOperationException("A packaging fee percent must be more than 0% and at most 100%.");

            case PackagingFeeBasis.PerUnit when value <= 0m || value > MaxPerUnit:
                throw new InvalidOperationException(
                    $"A packaging fee per unit must be more than 0 and at most {MaxPerUnit:0}.");

            case PackagingFeeBasis.Percent or PackagingFeeBasis.PerUnit:
                if (decimal.Round(value, 2) != value)
                {
                    throw new InvalidOperationException("A packaging fee has at most two decimals.");
                }

                return new PackagingFee(basis, value);

            default:
                throw new InvalidOperationException($"'{basis}' is not a way to charge a packaging fee.");
        }
    }

    /// <summary>The fee on <paramref name="quantity"/> units whose amount after discounts is
    /// <paramref name="amount"/>, unrounded: <see cref="PosLineArithmetic"/> rounds it to the paisa.</summary>
    public decimal UnroundedFor(decimal quantity, decimal amount) => Basis switch
    {
        PackagingFeeBasis.Percent => amount * Value / 100m,
        PackagingFeeBasis.PerUnit => quantity * Value,
        _ => 0m,
    };
}
