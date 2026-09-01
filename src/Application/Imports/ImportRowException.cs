namespace ErpApp.Application.Imports;

/// <summary>
/// One row's rejection, carrying the column it is about. Never escapes the processor: it is caught
/// per row and written to that row's <see cref="ErpApp.Domain.Imports.ImportJobRow"/>, which is why
/// a file of 1,000 rows with 3 bad ones is a <b>Completed</b> job rather than a failed one
/// (Decision C).
///
/// <para>Deliberately not derived from Common.Exceptions' ValidationException/NotFoundException:
/// those are mapped to HTTP status codes by the Api's exception middleware, and this one is never
/// on an HTTP path. Conflating them would make it possible for a row rejection to accidentally
/// become a 400 on some future endpoint.</para>
/// </summary>
public sealed class ImportRowException(string? columnName, string message) : Exception(message)
{
    public string? ColumnName { get; } = columnName;
}
