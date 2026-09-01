using ErpApp.Application.Common.Email;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Common.Storage;
using ErpApp.Application.Exports;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace ErpApp.Application.UnitTests.TestSupport;

/// <summary>
/// A real DI container for the export suite, for the same reason Phase 21a built
/// <see cref="ImportTestHost"/>: the claims worth testing are about the pipeline, and a stubbed
/// <c>ISender</c> would make every permission assertion vacuous. <see cref="Send"/> puts a command
/// or query through the genuine six-behavior pipeline with a real
/// <c>AuthorizationBehavior</c> reading real <c>RolePermission</c> rows.
///
/// <para>The one thing this host does <i>not</i> need, and Phase 21a's did: an
/// <c>IJobActingUser</c>. An export processor sends no MediatR request at all, because it only
/// reads (Decision D). The <see cref="MutableCurrentUserService"/> below exists purely to drive the
/// enqueue/list/download <i>requests</i>, which are ordinary authenticated HTTP work.</para>
///
/// <para>All scopes share one named InMemory database, so a scope-per-job processor sees what its
/// previous scopes committed, exactly as it would against SQL Server.</para>
/// </summary>
public sealed class ExportTestHost : IDisposable
{
    private readonly ServiceProvider _services;

    /// <param name="configureServices">Last-word override, applied after <c>AddApplication</c>, for
    /// the two tests that need a stub <see cref="IExportCategoryReader"/> -- the row cap and the
    /// cancel-mid-run boundary are both reachable only by controlling what a reader does.</param>
    public ExportTestHost(DateTimeOffset now, Action<IServiceCollection>? configureServices = null)
    {
        DatabaseName = Guid.NewGuid().ToString();
        Clock = new FakeTimeProvider(now);

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
        services.AddApplication();

        services.AddScoped(_ => TestAppDbContext.Create(DatabaseName));

        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton<IFileStorage>(FileStorage);
        services.AddSingleton<IEmailSender>(EmailSender);
        services.AddSingleton<IExportWorkbookWriter>(WorkbookWriter);
        services.AddSingleton<ICurrentUserService>(CurrentUser);

        configureServices?.Invoke(services);

        _services = services.BuildServiceProvider();
    }

    public string DatabaseName { get; }

    public FakeTimeProvider Clock { get; }

    public FakeFileStorage FileStorage { get; } = new();

    public FakeEmailSender EmailSender { get; } = new();

    public RecordingExportWorkbookWriter WorkbookWriter { get; } = new();

    public MutableCurrentUserService CurrentUser { get; } = new();

    /// <summary>A fresh context over the shared store -- use for seeding and for asserting, never to
    /// re-read an entity the processor is holding.</summary>
    public IAppDbContext NewDbContext() => TestAppDbContext.Create(DatabaseName);

    /// <summary>A processor in its own scope, mirroring the hosted service's scope-per-job. Call
    /// this again to simulate a process restart.</summary>
    public IExportJobProcessor NewProcessor()
    {
        var scope = _services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IExportJobProcessor>();
    }

    /// <summary>Sends through the real pipeline, in a fresh scope, as an HTTP request would.</summary>
    public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request)
    {
        using var scope = _services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request);
    }

    public void Dispose() => _services.Dispose();
}

/// <summary>Stands in for the Api's CurrentUserService for the request-side tests. Settable, so one
/// host can act as two different users and prove the cross-tenant negative.</summary>
public sealed class MutableCurrentUserService : ICurrentUserService
{
    public Guid UserId { get; set; }
}

/// <summary>
/// Captures the <see cref="ExportWorkbook"/> the processor builds, and writes something
/// non-empty so the artifact's size and storage round-trip are real.
///
/// <para><b>Deliberately not the ClosedXML writer.</b> Application.UnitTests cannot see
/// Infrastructure, and more importantly these tests are about <i>what is in the workbook</i> --
/// sheet names, headers, and which tenant's rows -- which is far easier to assert on the structure
/// than on parsed .xlsx bytes. That the real writer turns this structure into a file ClosedXML can
/// read back is proved separately, and with the real library, by
/// <c>ExportWorkbookWriterTests</c> in Api.IntegrationTests.</para>
/// </summary>
public sealed class RecordingExportWorkbookWriter : IExportWorkbookWriter
{
    public ExportWorkbook? LastWorkbook { get; private set; }

    public int WriteCount { get; private set; }

    public Func<ExportWorkbook, Exception?>? OnWrite { get; set; }

    public async Task WriteAsync(ExportWorkbook workbook, Stream destination, CancellationToken cancellationToken)
    {
        LastWorkbook = workbook;
        WriteCount++;

        if (OnWrite?.Invoke(workbook) is { } failure)
        {
            throw failure;
        }

        var bytes = System.Text.Encoding.UTF8.GetBytes(
            string.Join("|", workbook.Sheets.Select(s => $"{s.Name}:{s.Rows.Count}")));
        await destination.WriteAsync(bytes, cancellationToken);
    }

    public ExportWorkbookSheet Sheet(string name) =>
        LastWorkbook?.Sheets.SingleOrDefault(s => s.Name == name)
        ?? throw new InvalidOperationException($"No '{name}' sheet was written.");
}
