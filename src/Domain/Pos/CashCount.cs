using System.Globalization;

namespace ErpApp.Domain.Pos;

/// <summary>One denomination in a drawer count: the note or coin's face value and how many of it.</summary>
public sealed record DenominationCount(int Value, int Count);

/// <summary>
/// Phase 61 -- a drawer counted note by note, as the vendor's Start Session *Denomination* tab does
/// (<c>denominations: [{value: 500, count: 2}]</c>). A value object: it is stored on the session as
/// text and only ever replaced whole, never edited one denomination at a time.
///
/// <para>Rows with a zero count are dropped rather than stored, so two counts of the same cash
/// compare equal however many empty rows the till sent.</para>
/// </summary>
public sealed class CashCount
{
    private const char RowSeparator = ',';
    private const char PairSeparator = 'x';

    public IReadOnlyList<DenominationCount> Rows { get; }

    public decimal Total => Rows.Sum(x => (decimal)x.Value * x.Count);

    private CashCount(IReadOnlyList<DenominationCount> rows) => Rows = rows;

    /// <summary>
    /// Builds a count against the denominations a location's drawer holds (phase 60's
    /// <c>PosLocationSettings.Denominations</c>). A value the drawer does not hold is refused rather
    /// than kept, because a count is evidence and a note nobody configured is a typo.
    /// </summary>
    public static CashCount From(IEnumerable<DenominationCount> rows, IReadOnlyCollection<int> drawerDenominations)
    {
        var list = rows.ToList();

        if (list.Any(x => x.Count < 0))
        {
            throw new InvalidOperationException("A denomination count cannot be negative.");
        }

        if (list.GroupBy(x => x.Value).Any(g => g.Count() > 1))
        {
            throw new InvalidOperationException("A denomination is counted twice.");
        }

        var unknown = list.Where(x => !drawerDenominations.Contains(x.Value)).Select(x => x.Value).ToList();
        if (unknown.Count > 0)
        {
            throw new InvalidOperationException(
                $"This drawer holds no {string.Join(", ", unknown)} note or coin. It holds: "
                + $"{string.Join(", ", drawerDenominations)}.");
        }

        return new CashCount([.. list.Where(x => x.Count > 0).OrderByDescending(x => x.Value)]);
    }

    /// <summary>The stored form, largest denomination first: <c>1000x2,500x1</c>.</summary>
    public string Serialize() =>
        string.Join(RowSeparator, Rows.Select(x => string.Create(CultureInfo.InvariantCulture, $"{x.Value}{PairSeparator}{x.Count}")));

    public static CashCount Parse(string stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return new CashCount([]);
        }

        var rows = stored
            .Split(RowSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(pair =>
            {
                var parts = pair.Split(PairSeparator);
                return new DenominationCount(
                    int.Parse(parts[0], CultureInfo.InvariantCulture), int.Parse(parts[1], CultureInfo.InvariantCulture));
            })
            .ToList();

        return new CashCount(rows);
    }
}
