using System.Globalization;

namespace ErpApp.Application.Imports;

/// <summary>
/// Reads one data row by column <i>name</i>, coercing text into the types the create/update
/// commands want and throwing <see cref="ImportRowException"/> -- which names the offending column
/// -- on anything it cannot coerce.
///
/// <para><b>Mapping is by header name, not by position.</b> The reference product's own templates
/// carry the instruction "Do not change Column Header and their position"; only the first half of
/// that is enforced here. Requiring exact ordering would reject a file that is unambiguously
/// correct, and an ordering-sensitive parser silently imports the wrong column into the wrong field
/// when a user inserts one, which is far worse than a rejection. Unrecognised extra columns are
/// ignored rather than rejected, so a user's own working notes column does not fail the file.</para>
/// </summary>
public sealed class ImportRowReader
{
    private readonly Dictionary<string, int> _columnIndexes;
    private readonly IReadOnlyList<string?> _cells;

    public ImportRowReader(IReadOnlyDictionary<string, int> columnIndexes, ImportSheetRow row)
    {
        _columnIndexes = new Dictionary<string, int>(columnIndexes, StringComparer.OrdinalIgnoreCase);
        _cells = row.Cells;
        RowNumber = row.RowNumber;
    }

    public int RowNumber { get; }

    /// <summary>Builds the header-name to column-index map an <see cref="ImportRowReader"/> needs,
    /// keeping the first occurrence when a file duplicates a header.</summary>
    public static IReadOnlyDictionary<string, int> BuildColumnIndexes(IReadOnlyList<string> headers)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headers.Count; i++)
        {
            var header = Normalize(headers[i]);
            if (header.Length > 0)
            {
                map.TryAdd(header, i);
            }
        }

        return map;
    }

    /// <summary>Template headers carry a "**" required marker (the reference product's convention,
    /// kept verbatim so a user comparing the two files sees the same thing); the marker is not part
    /// of the column's identity.</summary>
    public static string Normalize(string? header) =>
        (header ?? string.Empty).Replace("**", string.Empty, StringComparison.Ordinal).Trim();

    public string? GetOptionalString(string column)
    {
        if (!_columnIndexes.TryGetValue(column, out var index) || index >= _cells.Count)
        {
            return null;
        }

        var value = _cells[index]?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    public string GetRequiredString(string column) =>
        GetOptionalString(column) ?? throw new ImportRowException(column, $"'{column}' is required.");

    public decimal GetOptionalDecimal(string column, decimal fallback = 0m)
    {
        var raw = GetOptionalString(column);
        if (raw is null)
        {
            return fallback;
        }

        // Thousands separators are what a real exported spreadsheet actually contains; rejecting
        // "1,500" as non-numeric would be technically defensible and practically useless.
        var cleaned = raw.Replace(",", string.Empty, StringComparison.Ordinal);
        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new ImportRowException(column, $"'{raw}' is not a valid number.");
    }

    public int GetOptionalInt(string column, int fallback = 0)
    {
        var value = GetOptionalDecimal(column, fallback);
        return value == decimal.Truncate(value)
            ? (int)value
            : throw new ImportRowException(column, $"'{value}' must be a whole number.");
    }

    /// <summary>Accepts the spellings a human actually types in a Yes/No column. Anything else is a
    /// row error rather than a silent false, because silently importing every product as
    /// "not tracked" because someone wrote "Y" is exactly the failure no test catches.</summary>
    public bool GetOptionalBoolean(string column, bool fallback)
    {
        var raw = GetOptionalString(column);
        if (raw is null)
        {
            return fallback;
        }

        return raw.ToLowerInvariant() switch
        {
            "yes" or "y" or "true" or "1" => true,
            "no" or "n" or "false" or "0" => false,
            _ => throw new ImportRowException(column, $"'{raw}' is not valid; use Yes or No."),
        };
    }

    /// <summary>Maps a cell to one of a fixed set of allowed words, listing them all in the error.</summary>
    public TValue GetChoice<TValue>(string column, IReadOnlyDictionary<string, TValue> allowed, bool required, TValue fallback)
    {
        var raw = GetOptionalString(column);
        if (raw is null)
        {
            return required ? throw new ImportRowException(column, $"'{column}' is required.") : fallback;
        }

        if (allowed.TryGetValue(raw, out var value))
        {
            return value;
        }

        throw new ImportRowException(
            column, $"'{raw}' is not valid; expected one of: {string.Join(", ", allowed.Keys)}.");
    }
}
