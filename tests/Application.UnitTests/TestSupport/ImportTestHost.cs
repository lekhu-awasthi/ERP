using ErpApp.Application;
using ErpApp.Application.Common.Email;
using ErpApp.Application.Common.Numbering;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Common.Storage;
using ErpApp.Application.Imports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace ErpApp.Application.UnitTests.TestSupport;

/// <summary>
/// A real DI container for the import suite -- the one place in these tests that does not construct
/// a handler by hand.
///
/// <para>That is deliberate and load-bearing. <see cref="ImportJobProcessor"/> creates a DI scope per
/// row, assumes the initiating user's identity in it, and sends the ordinary
/// <c>CreateProductCommand</c>/<c>CreateContactCommand</c> through the <b>full six-behavior
/// pipeline</b>. The two most important claims this phase makes -- that permission is re-checked at
/// execution time on every row, and that a row's failure is the validator's or handler's real
/// message rather than something the importer invented -- are only true if the pipeline actually
/// runs. Stubbing <c>ISender</c> would make every one of those tests vacuous.</para>
///
/// <para>All scopes share one named InMemory database, so a scope-per-row processor sees the rows
/// its previous scopes committed, exactly as it would against SQL Server.</para>
/// </summary>
public sealed class ImportTestHost : IDisposable
{
    private readonly ServiceProvider _services;

    public ImportTestHost(DateTimeOffset now)
    {
        DatabaseName = Guid.NewGuid().ToString();
        Clock = new FakeTimeProvider(now);

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
        services.AddApplication();

        // Scoped, like the real registration: each scope gets its own context over the shared store.
        services.AddScoped(_ => TestAppDbContext.Create(DatabaseName));

        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton<IFileStorage>(FileStorage);
        services.AddSingleton<IEmailSender>(EmailSender);
        services.AddSingleton<IImportFileReader>(FileReader);

        // Singleton on purpose: DocumentNumberGenerator is a per-tenant sequence in production, so a
        // per-scope counter would hand every row of an import the same product code and quietly
        // break every update-by-code test.
        services.AddSingleton<IDocumentNumberGenerator, FakeDocumentNumberGenerator>();

        // Stands in for the Api's CurrentUserService in its no-HttpContext branch, which is the only
        // branch a background job can reach. See IJobActingUser.
        services.AddScoped<ICurrentUserService, JobActingCurrentUserService>();

        _services = services.BuildServiceProvider();
    }

    public string DatabaseName { get; }

    public FakeTimeProvider Clock { get; }

    public FakeFileStorage FileStorage { get; } = new();

    public FakeEmailSender EmailSender { get; } = new();

    public StubImportFileReader FileReader { get; } = new();

    /// <summary>A fresh context over the shared store -- use for seeding and for asserting, never to
    /// re-read an entity the processor is holding.</summary>
    public IAppDbContext NewDbContext() => TestAppDbContext.Create(DatabaseName);

    /// <summary>
    /// A processor in its own scope, mirroring the hosted service's scope-per-job. Call this again
    /// to simulate a process restart: the new processor shares nothing with the old one except the
    /// database, which is the entire point of the resume tests.
    /// </summary>
    public IImportJobProcessor NewProcessor()
    {
        var scope = _services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IImportJobProcessor>();
    }

    public void Dispose() => _services.Dispose();
}

/// <summary>Resolves the acting user the way the real Api does when there is no HttpContext at all
/// -- from <see cref="IJobActingUser"/>, and throwing when no job has assumed one.</summary>
public sealed class JobActingCurrentUserService(IJobActingUser jobActingUser) : ICurrentUserService
{
    public Guid UserId => jobActingUser.UserId
        ?? throw new InvalidOperationException("No background job has assumed an acting user in this scope.");
}

/// <summary>
/// Hands the processor a pre-built <see cref="ImportSheet"/> instead of parsing bytes, so the suite
/// exercises every mapping, validation and outcome rule without a spreadsheet library. The real
/// ClosedXML reader's own job -- bytes to strings -- is verified during manual E2E with a real
/// uploaded file, which is also the only place the .DisableAntiforgery() and Kestrel constraints
/// can surface.
/// </summary>
public sealed class StubImportFileReader : IImportFileReader
{
    private ImportSheet? _sheet;
    private ImportFileFormatException? _failure;

    public void Returns(IReadOnlyList<string> headers, params IReadOnlyList<string?>[] rows)
    {
        _failure = null;
        _sheet = new ImportSheet(
            headers,
            [.. rows.Select((cells, i) => new ImportSheetRow(i + 2, cells))]);
    }

    public void Throws(string message)
    {
        _sheet = null;
        _failure = new ImportFileFormatException(message);
    }

    public Task<ImportSheet> ReadAsync(Stream content, CancellationToken cancellationToken = default)
    {
        if (_failure is not null)
        {
            throw _failure;
        }

        return Task.FromResult(_sheet ?? new ImportSheet([], []));
    }
}
