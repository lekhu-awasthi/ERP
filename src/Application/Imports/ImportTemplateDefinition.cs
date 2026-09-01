using ErpApp.Domain.Imports;

namespace ErpApp.Application.Imports;

/// <summary>
/// The downloadable template's shape, as pure data.
///
/// <para><b>The point of this type is that one declaration drives both halves.</b> The Api renders
/// it to a .xlsx with ClosedXML, and the same <see cref="Columns"/> list is what
/// <see cref="IEntityImporter"/> validates the uploaded file's headers against. A template whose
/// columns can drift from the parser's expectations is the single most likely way for a bulk
/// importer to be wrong in a way no test notices, so they are not allowed to be two lists.</para>
/// </summary>
/// <param name="SampleRow">
/// One filled example row, positionally aligned with <see cref="Columns"/>. Present because the
/// reference product's own templates ship one and it is genuinely how a user learns the format --
/// but see <see cref="IEntityImporter"/> for why leaving it in is a rejected row here rather than a
/// surprise record.
/// </param>
/// <param name="Instructions">Free text rendered in a column to the right of the grid, exactly as
/// the reference product's templates do.</param>
public sealed record ImportTemplateDefinition(
    ImportEntityType EntityType,
    string SheetName,
    string FileNameStem,
    IReadOnlyList<ImportColumn> Columns,
    IReadOnlyList<string?> SampleRow,
    IReadOnlyList<string> Instructions)
{
    /// <summary>Header text as written into the template file: required columns carry the "**"
    /// suffix the reference product uses. <see cref="ImportRowReader.Normalize"/> strips it, so the
    /// marker is presentation only and a user who deletes it still gets a working file.</summary>
    public IReadOnlyList<string> HeaderTexts =>
        [.. Columns.Select(c => c.Required ? c.Name + "**" : c.Name)];
}

/// <param name="Required">
/// Required <i>as a column</i>: the file is rejected outright if it is missing. Whether the cell may
/// be empty is a per-row rule the importer enforces, because it can depend on the mode -- Code is
/// blank in create mode and mandatory in update mode, so its column is always present but only
/// sometimes populated.
/// </param>
public sealed record ImportColumn(string Name, bool Required);
