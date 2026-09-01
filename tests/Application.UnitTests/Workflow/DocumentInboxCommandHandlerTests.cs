using System.Text;
using ErpApp.Application.Catalog.Commands.CreateProduct;
using ErpApp.Application.Catalog.Commands.CreateProductCategory;
using ErpApp.Application.Catalog.Commands.CreateUnitOfMeasurement;
using ErpApp.Application.Common.DocumentExtraction;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Contacts.Commands.CreateContact;
using ErpApp.Application.Purchasing;
using ErpApp.Application.Purchasing.Commands.CreatePurchaseBill;
using ErpApp.Application.Tenancy.Commands.CreateWarehouse;
using ErpApp.Application.Tenancy.Commands.UpdateAiDocumentExtractionSetting;
using ErpApp.Application.Tenancy.Queries.GetAiDocumentExtractionSetting;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Application.Workflow;
using ErpApp.Application.Workflow.Commands.ClearInboxDocumentExtraction;
using ErpApp.Application.Workflow.Commands.DeleteInboxDocument;
using ErpApp.Application.Workflow.Commands.ExtractInboxDocument;
using ErpApp.Application.Workflow.Commands.LinkInboxDocument;
using ErpApp.Application.Workflow.Commands.UpdateInboxDocument;
using ErpApp.Application.Workflow.Commands.UploadInboxDocument;
using ErpApp.Application.Workflow.Queries.GetInboxDocumentForDownload;
using ErpApp.Application.Workflow.Queries.GetInboxDocumentPrefill;
using ErpApp.Application.Workflow.Queries.ListInboxDocuments;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Purchasing;
using ErpApp.Domain.Tenancy;
using ErpApp.Domain.Workflow;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Workflow;

/// <summary>
/// Roadmap Phase 22 (Document inbox, FR-10.3).
///
/// <para>The two exit criteria are tests here, not a demo: an uploaded file converts into a
/// <b>Draft</b> Purchase Bill with nothing approved, numbered or posted, and the source file is
/// still retrievable <i>from the resulting transaction</i> afterwards. Everything AI-shaped runs
/// through <see cref="FakeDocumentExtractor"/> -- <b>no test in this file touches the
/// network</b>, and every extraction assertion is about the contract, never about model output.</para>
/// </summary>
public class DocumentInboxCommandHandlerTests
{
    // ---------------------------------------------------------------- upload & isolation

    [Fact]
    public async Task Upload_stores_the_file_and_starts_pending_with_no_extraction_and_no_link()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var uploaderId = await CreateUserAsync(db);
        var storage = new FakeFileStorage();

        var uploaded = await UploadAsync(db, storage, organizationId, uploaderId, "bill.pdf");

        Assert.Equal(UploadedDocumentStatus.Pending, uploaded.Status);
        Assert.Equal(DocumentExtractionStatus.NotAttempted, uploaded.ExtractionStatus);
        Assert.False(uploaded.IsLinked);
        Assert.Null(uploaded.LinkedTransactionType);
        Assert.True(uploaded.IsExtractable);

        var stored = await db.UploadedDocuments.SingleAsync(x => x.Id == uploaded.Id);
        Assert.True(storage.Contains(stored.StorageKey));
    }

    /// <summary>Upload never triggers extraction as a side effect -- nothing may leave the tenant
    /// because somebody dragged a file onto a page (docs/phase-22-status.md, Decision C).</summary>
    [Fact]
    public async Task Upload_never_calls_the_extractor()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var uploaderId = await CreateUserAsync(db);
        var extractor = new FakeDocumentExtractor();

        await UploadAsync(db, new FakeFileStorage(), organizationId, uploaderId, "bill.pdf");

        Assert.Equal(0, extractor.CallCount);
    }

    /// <summary>
    /// Tenant isolation at phase-21b's bar: organization A's documents must be <b>absent</b> from
    /// B's answer, not merely outnumbered. There is no EF global query filter in this codebase, so
    /// this is per-handler and has to be asserted per-handler.
    /// </summary>
    [Fact]
    public async Task List_and_download_never_reach_another_organizations_documents()
    {
        var db = TestAppDbContext.Create();
        var organizationA = Guid.NewGuid();
        var organizationB = Guid.NewGuid();
        var uploaderId = await CreateUserAsync(db);
        var storage = new FakeFileStorage();

        var aDocument = await UploadAsync(db, storage, organizationA, uploaderId, "a-bill.pdf");
        await UploadAsync(db, storage, organizationB, uploaderId, "b-bill.pdf");

        var bList = await new ListInboxDocumentsQueryHandler(db).Handle(
            new ListInboxDocumentsQuery(organizationB), CancellationToken.None);

        Assert.DoesNotContain(bList.Items, x => x.Id == aDocument.Id);
        Assert.DoesNotContain(bList.Items, x => x.FileName == "a-bill.pdf");

        await Assert.ThrowsAsync<NotFoundException>(() => new GetInboxDocumentForDownloadQueryHandler(db).Handle(
            new GetInboxDocumentForDownloadQuery(organizationB, aDocument.Id), CancellationToken.None));
    }

    // ---------------------------------------------------------------- exit criteria

    /// <summary>
    /// <b>Exit criterion #1.</b> A converted document produces a Draft Purchase Bill -- nothing
    /// approved, nothing numbered, nothing posted to the General Ledger or the stock ledger. The
    /// conversion itself creates nothing; the ordinary CreatePurchaseBillCommand does, exactly as it
    /// would from the form (docs/phase-22-status.md, Decision B).
    /// </summary>
    [Fact]
    public async Task Converting_produces_a_draft_purchase_bill_with_nothing_approved_or_posted()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedPurchasingAsync(db);
        var storage = new FakeFileStorage();

        var document = await UploadAsync(db, storage, seed.OrganizationId, seed.UploaderId, "supplier-bill.pdf");

        var bill = await CreatePurchaseBillAsync(db, seed);

        await new LinkInboxDocumentCommandHandler(db, TimeProvider.System).Handle(
            new LinkInboxDocumentCommand(seed.OrganizationId, document.Id, DocumentType.PurchaseBill, bill.Id),
            CancellationToken.None);

        var stored = await db.PurchaseBills.SingleAsync(x => x.Id == bill.Id);
        Assert.Equal(PurchaseBillStatus.Draft, stored.Status);
        Assert.Empty(await db.GlJournalEntries.Where(x => x.SourceDocumentId == bill.Id).ToListAsync());
        Assert.Empty(await db.StockLedgerEntries.Where(x => x.OrganizationId == seed.OrganizationId).ToListAsync());

        var linked = await db.UploadedDocuments.SingleAsync(x => x.Id == document.Id);
        Assert.True(linked.IsLinked);
        Assert.Equal(DocumentType.PurchaseBill, linked.LinkedTransactionType);
        Assert.Equal(bill.Id, linked.LinkedTransactionId);
        Assert.Equal(UploadedDocumentStatus.Done, linked.Status);
    }

    /// <summary>
    /// <b>Exit criterion #2.</b> "The source image stays linked and viewable from the resulting
    /// document" is a requirement on the <i>transaction</i>, so this walks that direction: find the
    /// document by the transaction it produced, then fetch the bytes back through the authenticated
    /// download path.
    /// </summary>
    [Fact]
    public async Task The_source_file_is_still_retrievable_from_the_resulting_transaction()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedPurchasingAsync(db);
        var storage = new FakeFileStorage();
        var originalBytes = Encoding.UTF8.GetBytes("scanned bill bytes");

        var document = await UploadAsync(
            db, storage, seed.OrganizationId, seed.UploaderId, "supplier-bill.pdf", originalBytes);

        var bill = await CreatePurchaseBillAsync(db, seed);
        await new LinkInboxDocumentCommandHandler(db, TimeProvider.System).Handle(
            new LinkInboxDocumentCommand(seed.OrganizationId, document.Id, DocumentType.PurchaseBill, bill.Id),
            CancellationToken.None);

        var fromTransaction = await new ListInboxDocumentsQueryHandler(db).Handle(
            new ListInboxDocumentsQuery(
                seed.OrganizationId, LinkedTransactionType: DocumentType.PurchaseBill, LinkedTransactionId: bill.Id),
            CancellationToken.None);

        var row = Assert.Single(fromTransaction.Items);
        Assert.Equal(document.Id, row.Id);

        var metadata = await new GetInboxDocumentForDownloadQueryHandler(db).Handle(
            new GetInboxDocumentForDownloadQuery(seed.OrganizationId, row.Id), CancellationToken.None);

        using var stream = await storage.OpenReadAsync(metadata.StorageKey, CancellationToken.None);
        using var reader = new StreamReader(stream);

        Assert.Equal("scanned bill bytes", await reader.ReadToEndAsync());
        Assert.Equal("supplier-bill.pdf", metadata.FileName);
    }

    // ---------------------------------------------------------------- convert twice

    [Fact]
    public async Task Linking_a_second_transaction_is_refused_and_says_why()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedPurchasingAsync(db);
        var document = await UploadAsync(db, new FakeFileStorage(), seed.OrganizationId, seed.UploaderId, "bill.pdf");

        var first = await CreatePurchaseBillAsync(db, seed);
        var second = await CreatePurchaseBillAsync(db, seed);
        var handler = new LinkInboxDocumentCommandHandler(db, TimeProvider.System);

        await handler.Handle(
            new LinkInboxDocumentCommand(seed.OrganizationId, document.Id, DocumentType.PurchaseBill, first.Id),
            CancellationToken.None);

        var conflict = await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new LinkInboxDocumentCommand(seed.OrganizationId, document.Id, DocumentType.PurchaseBill, second.Id),
            CancellationToken.None));

        Assert.Contains("already been converted", conflict.Message, StringComparison.OrdinalIgnoreCase);

        var stored = await db.UploadedDocuments.SingleAsync(x => x.Id == document.Id);
        Assert.Equal(first.Id, stored.LinkedTransactionId);
    }

    /// <summary>An already-converted document cannot even be pre-filled again, so the second
    /// conversion is refused before the user has typed anything into a form.</summary>
    [Fact]
    public async Task Prefill_is_refused_for_an_already_converted_document()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedPurchasingAsync(db);
        var document = await UploadAsync(db, new FakeFileStorage(), seed.OrganizationId, seed.UploaderId, "bill.pdf");
        var bill = await CreatePurchaseBillAsync(db, seed);

        await new LinkInboxDocumentCommandHandler(db, TimeProvider.System).Handle(
            new LinkInboxDocumentCommand(seed.OrganizationId, document.Id, DocumentType.PurchaseBill, bill.Id),
            CancellationToken.None);

        await Assert.ThrowsAsync<ConflictException>(() => new GetInboxDocumentPrefillQueryHandler(db).Handle(
            new GetInboxDocumentPrefillQuery(seed.OrganizationId, document.Id, DocumentType.PurchaseBill),
            CancellationToken.None));
    }

    [Fact]
    public async Task Linking_a_transaction_from_another_organization_is_not_found()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedPurchasingAsync(db);
        var otherOrganizationId = Guid.NewGuid();
        var document = await UploadAsync(db, new FakeFileStorage(), seed.OrganizationId, seed.UploaderId, "bill.pdf");
        var bill = await CreatePurchaseBillAsync(db, seed);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            new LinkInboxDocumentCommandHandler(db, TimeProvider.System).Handle(
                new LinkInboxDocumentCommand(otherOrganizationId, document.Id, DocumentType.PurchaseBill, bill.Id),
                CancellationToken.None));
    }

    // ---------------------------------------------------------------- lifecycle & deletion

    [Fact]
    public async Task A_document_can_be_filed_done_by_hand_and_reopened_while_it_is_unlinked()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var uploaderId = await CreateUserAsync(db);
        var document = await UploadAsync(db, new FakeFileStorage(), organizationId, uploaderId, "receipt.jpg");
        var handler = new UpdateInboxDocumentCommandHandler(db);

        var done = await handler.Handle(
            new UpdateInboxDocumentCommand(organizationId, document.Id, "Filed, never posted", "Receipt", UploadedDocumentStatus.Done),
            CancellationToken.None);
        Assert.Equal(UploadedDocumentStatus.Done, done.Status);
        Assert.Equal("Receipt", done.Label);

        var reopened = await handler.Handle(
            new UpdateInboxDocumentCommand(organizationId, document.Id, null, null, UploadedDocumentStatus.Pending),
            CancellationToken.None);
        Assert.Equal(UploadedDocumentStatus.Pending, reopened.Status);
    }

    [Fact]
    public async Task A_linked_document_can_be_neither_reopened_nor_deleted()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedPurchasingAsync(db);
        var storage = new FakeFileStorage();
        var document = await UploadAsync(db, storage, seed.OrganizationId, seed.UploaderId, "bill.pdf");
        var bill = await CreatePurchaseBillAsync(db, seed);

        await new LinkInboxDocumentCommandHandler(db, TimeProvider.System).Handle(
            new LinkInboxDocumentCommand(seed.OrganizationId, document.Id, DocumentType.PurchaseBill, bill.Id),
            CancellationToken.None);

        await Assert.ThrowsAsync<ConflictException>(() => new UpdateInboxDocumentCommandHandler(db).Handle(
            new UpdateInboxDocumentCommand(seed.OrganizationId, document.Id, null, null, UploadedDocumentStatus.Pending),
            CancellationToken.None));

        await Assert.ThrowsAsync<ConflictException>(() => new DeleteInboxDocumentCommandHandler(db, storage).Handle(
            new DeleteInboxDocumentCommand(seed.OrganizationId, document.Id), CancellationToken.None));

        var stored = await db.UploadedDocuments.SingleAsync(x => x.Id == document.Id);
        Assert.True(storage.Contains(stored.StorageKey));
    }

    [Fact]
    public async Task Deleting_an_unlinked_document_removes_both_the_row_and_the_stored_file()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var uploaderId = await CreateUserAsync(db);
        var storage = new FakeFileStorage();
        var document = await UploadAsync(db, storage, organizationId, uploaderId, "bill.pdf");
        var storageKey = (await db.UploadedDocuments.SingleAsync(x => x.Id == document.Id)).StorageKey;

        await new DeleteInboxDocumentCommandHandler(db, storage).Handle(
            new DeleteInboxDocumentCommand(organizationId, document.Id), CancellationToken.None);

        Assert.Null(await db.UploadedDocuments.SingleOrDefaultAsync(x => x.Id == document.Id));
        Assert.False(storage.Contains(storageKey));
    }

    // ---------------------------------------------------------------- extraction: the seam

    [Fact]
    public async Task Extraction_is_refused_until_the_tenant_opts_in()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var uploaderId = await CreateUserAsync(db);
        db.TenantSettings.Add(TenantSettings.CreateDefault(organizationId));
        await db.SaveChangesAsync(CancellationToken.None);

        var storage = new FakeFileStorage();
        var document = await UploadAsync(db, storage, organizationId, uploaderId, "bill.pdf");
        var extractor = new FakeDocumentExtractor();

        var conflict = await Assert.ThrowsAsync<ConflictException>(() =>
            new ExtractInboxDocumentCommandHandler(db, storage, extractor, TimeProvider.System).Handle(
                new ExtractInboxDocumentCommand(organizationId, document.Id), CancellationToken.None));

        Assert.Contains("turned off", conflict.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, extractor.CallCount);
    }

    [Fact]
    public async Task A_successful_extraction_is_stored_and_prefills_the_target_form()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedPurchasingAsync(db, aiEnabled: true);
        var storage = new FakeFileStorage();
        var document = await UploadAsync(db, storage, seed.OrganizationId, seed.UploaderId, "bill.pdf");

        var extractor = new FakeDocumentExtractor(data: new ExtractedDocumentData
        {
            PartyName = "Global Supplies",
            PartyPan = "301234567",
            DocumentDate = new DateOnly(2026, 4, 17),
            Reference = "INV-4471",
            TotalAmount = 1130m,
            VatAmount = 130m,
            Lines = [new ExtractedDocumentLine { Description = "Widget", Quantity = 10m, Rate = 100m, Amount = 1000m }],
        });

        var extracted = await new ExtractInboxDocumentCommandHandler(db, storage, extractor, TimeProvider.System).Handle(
            new ExtractInboxDocumentCommand(seed.OrganizationId, document.Id), CancellationToken.None);

        Assert.Equal(1, extractor.CallCount);
        Assert.Equal(DocumentExtractionStatus.Succeeded, extracted.ExtractionStatus);
        Assert.Equal("fake-model-1", extracted.ExtractionModelId);
        Assert.Equal("Global Supplies", extracted.ExtractedData?.PartyName);

        var prefill = await new GetInboxDocumentPrefillQueryHandler(db).Handle(
            new GetInboxDocumentPrefillQuery(seed.OrganizationId, document.Id, DocumentType.PurchaseBill),
            CancellationToken.None);

        Assert.True(prefill.HasExtraction);
        Assert.Equal(seed.SupplierId, prefill.ContactId);
        Assert.Equal(new DateOnly(2026, 4, 17), prefill.Date);
        Assert.Equal("INV-4471", prefill.Reference);
        Assert.Equal(1130m, prefill.TotalAmount);

        var line = Assert.Single(prefill.Lines);
        Assert.Equal(seed.ProductId, line.ProductId);
        Assert.Equal(10m, line.Quantity);
    }

    /// <summary>An unmatched party comes back as raw text with a null id -- never as a plausible
    /// neighbouring Contact, and never as a newly minted one.</summary>
    [Fact]
    public async Task An_unmatched_party_and_product_resolve_to_null_and_keep_the_raw_text()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedPurchasingAsync(db, aiEnabled: true);
        var storage = new FakeFileStorage();
        var document = await UploadAsync(db, storage, seed.OrganizationId, seed.UploaderId, "bill.pdf");

        var extractor = new FakeDocumentExtractor(data: new ExtractedDocumentData
        {
            PartyName = "Someone Not In The Contact List",
            Lines = [new ExtractedDocumentLine { Description = "Unknown item", Quantity = 1m }],
        });

        await new ExtractInboxDocumentCommandHandler(db, storage, extractor, TimeProvider.System).Handle(
            new ExtractInboxDocumentCommand(seed.OrganizationId, document.Id), CancellationToken.None);

        var prefill = await new GetInboxDocumentPrefillQueryHandler(db).Handle(
            new GetInboxDocumentPrefillQuery(seed.OrganizationId, document.Id, DocumentType.PurchaseBill),
            CancellationToken.None);

        Assert.Null(prefill.ContactId);
        Assert.Equal("Someone Not In The Contact List", prefill.PartyNameRaw);

        var line = Assert.Single(prefill.Lines);
        Assert.Null(line.ProductId);
        Assert.Equal("Unknown item", line.DescriptionRaw);

        var contactCount = await db.Contacts.CountAsync(x => x.OrganizationId == seed.OrganizationId);
        Assert.Equal(1, contactCount);
    }

    /// <summary>A sales conversion must not resolve a Supplier: the candidate set is narrowed by
    /// ContactType per target, so an Invoice looks only at Customers.</summary>
    [Fact]
    public async Task Contact_resolution_respects_the_target_types_contact_direction()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedPurchasingAsync(db, aiEnabled: true);
        var storage = new FakeFileStorage();
        var document = await UploadAsync(db, storage, seed.OrganizationId, seed.UploaderId, "bill.pdf");

        var extractor = new FakeDocumentExtractor(
            data: new ExtractedDocumentData { PartyName = "Global Supplies" });

        await new ExtractInboxDocumentCommandHandler(db, storage, extractor, TimeProvider.System).Handle(
            new ExtractInboxDocumentCommand(seed.OrganizationId, document.Id), CancellationToken.None);

        var handler = new GetInboxDocumentPrefillQueryHandler(db);

        var asPurchase = await handler.Handle(
            new GetInboxDocumentPrefillQuery(seed.OrganizationId, document.Id, DocumentType.PurchaseBill),
            CancellationToken.None);
        var asInvoice = await handler.Handle(
            new GetInboxDocumentPrefillQuery(seed.OrganizationId, document.Id, DocumentType.Invoice),
            CancellationToken.None);

        Assert.Equal(seed.SupplierId, asPurchase.ContactId);
        Assert.Null(asInvoice.ContactId);
        Assert.Equal("Global Supplies", asInvoice.PartyNameRaw);
    }

    [Theory]
    [InlineData(FakeDocumentExtractor.Behavior.Fail, DocumentExtractionStatus.Failed)]
    [InlineData(FakeDocumentExtractor.Behavior.Unavailable, DocumentExtractionStatus.Unavailable)]
    [InlineData(FakeDocumentExtractor.Behavior.Throw, DocumentExtractionStatus.Failed)]
    public async Task A_failed_extraction_leaves_the_document_manually_convertible_with_a_readable_reason(
        FakeDocumentExtractor.Behavior behavior, DocumentExtractionStatus expected)
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedPurchasingAsync(db, aiEnabled: true);
        var storage = new FakeFileStorage();
        var document = await UploadAsync(db, storage, seed.OrganizationId, seed.UploaderId, "bill.pdf");

        var result = await new ExtractInboxDocumentCommandHandler(
            db, storage, new FakeDocumentExtractor(behavior), TimeProvider.System).Handle(
            new ExtractInboxDocumentCommand(seed.OrganizationId, document.Id), CancellationToken.None);

        Assert.Equal(expected, result.ExtractionStatus);
        Assert.False(string.IsNullOrWhiteSpace(result.ExtractionFailureReason));
        Assert.Null(result.ExtractedData);

        // The document is still fully convertible -- that is the whole point of the failure being an
        // outcome rather than an error.
        var prefill = await new GetInboxDocumentPrefillQueryHandler(db).Handle(
            new GetInboxDocumentPrefillQuery(seed.OrganizationId, document.Id, DocumentType.PurchaseBill),
            CancellationToken.None);
        Assert.False(prefill.HasExtraction);
        Assert.Empty(prefill.Lines);

        var bill = await CreatePurchaseBillAsync(db, seed);
        await new LinkInboxDocumentCommandHandler(db, TimeProvider.System).Handle(
            new LinkInboxDocumentCommand(seed.OrganizationId, document.Id, DocumentType.PurchaseBill, bill.Id),
            CancellationToken.None);

        Assert.True((await db.UploadedDocuments.SingleAsync(x => x.Id == document.Id)).IsLinked);
    }

    [Fact]
    public async Task Extraction_is_refused_for_a_file_type_nothing_can_read()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedPurchasingAsync(db, aiEnabled: true);
        var storage = new FakeFileStorage();
        var document = await UploadAsync(db, storage, seed.OrganizationId, seed.UploaderId, "quote.xlsx");
        var extractor = new FakeDocumentExtractor();

        Assert.False(document.IsExtractable);

        await Assert.ThrowsAsync<ConflictException>(() =>
            new ExtractInboxDocumentCommandHandler(db, storage, extractor, TimeProvider.System).Handle(
                new ExtractInboxDocumentCommand(seed.OrganizationId, document.Id), CancellationToken.None));

        Assert.Equal(0, extractor.CallCount);
    }

    /// <summary>The "these numbers are not mine" escape hatch: clearing returns the document to
    /// exactly the state it was in before extraction ever ran.</summary>
    [Fact]
    public async Task Clearing_an_extraction_leaves_the_file_untouched_and_the_suggestion_gone()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedPurchasingAsync(db, aiEnabled: true);
        var storage = new FakeFileStorage();
        var document = await UploadAsync(db, storage, seed.OrganizationId, seed.UploaderId, "bill.pdf");

        await new ExtractInboxDocumentCommandHandler(
            db, storage, new FakeDocumentExtractor(), TimeProvider.System).Handle(
            new ExtractInboxDocumentCommand(seed.OrganizationId, document.Id), CancellationToken.None);

        var cleared = await new ClearInboxDocumentExtractionCommandHandler(db).Handle(
            new ClearInboxDocumentExtractionCommand(seed.OrganizationId, document.Id), CancellationToken.None);

        Assert.Equal(DocumentExtractionStatus.NotAttempted, cleared.ExtractionStatus);
        Assert.Null(cleared.ExtractedData);
        Assert.Null(cleared.ExtractionModelId);

        var stored = await db.UploadedDocuments.SingleAsync(x => x.Id == document.Id);
        Assert.True(storage.Contains(stored.StorageKey));
    }

    /// <summary>Only the one document's bytes leave the tenant. Nothing else is handed to the
    /// extractor -- the Contact and Product resolution happens afterwards, in this codebase.</summary>
    [Fact]
    public async Task Only_the_documents_own_bytes_are_handed_to_the_extractor()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedPurchasingAsync(db, aiEnabled: true);
        var storage = new FakeFileStorage();
        var bytes = Encoding.UTF8.GetBytes("the scan itself");
        var document = await UploadAsync(db, storage, seed.OrganizationId, seed.UploaderId, "bill.pdf", bytes);
        var extractor = new FakeDocumentExtractor();

        await new ExtractInboxDocumentCommandHandler(db, storage, extractor, TimeProvider.System).Handle(
            new ExtractInboxDocumentCommand(seed.OrganizationId, document.Id), CancellationToken.None);

        Assert.Equal(bytes, extractor.LastContent);
    }

    // ---------------------------------------------------------------- tenant consent

    [Fact]
    public async Task The_tenant_ai_setting_defaults_off_and_round_trips()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        db.TenantSettings.Add(TenantSettings.CreateDefault(organizationId));
        await db.SaveChangesAsync(CancellationToken.None);

        var extractor = new FakeDocumentExtractor();

        var initial = await new GetAiDocumentExtractionSettingQueryHandler(db, extractor).Handle(
            new GetAiDocumentExtractionSettingQuery(organizationId), CancellationToken.None);
        Assert.False(initial.Enabled);
        Assert.True(initial.ExtractorConfigured);
        Assert.Equal("fake-model-1", initial.ModelId);

        var updated = await new UpdateAiDocumentExtractionSettingCommandHandler(db, extractor).Handle(
            new UpdateAiDocumentExtractionSettingCommand(organizationId, true), CancellationToken.None);
        Assert.True(updated.Enabled);

        Assert.True((await db.TenantSettings.SingleAsync(x => x.OrganizationId == organizationId))
            .AiDocumentExtractionEnabled);
    }

    /// <summary>Withdrawing consent stops the very next extraction, not the next process restart --
    /// the flag is re-read on every run.</summary>
    [Fact]
    public async Task Withdrawing_consent_stops_the_next_extraction()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedPurchasingAsync(db, aiEnabled: true);
        var storage = new FakeFileStorage();
        var document = await UploadAsync(db, storage, seed.OrganizationId, seed.UploaderId, "bill.pdf");
        var extractor = new FakeDocumentExtractor();
        var handler = new ExtractInboxDocumentCommandHandler(db, storage, extractor, TimeProvider.System);

        await handler.Handle(new ExtractInboxDocumentCommand(seed.OrganizationId, document.Id), CancellationToken.None);
        Assert.Equal(1, extractor.CallCount);

        await new UpdateAiDocumentExtractionSettingCommandHandler(db, extractor).Handle(
            new UpdateAiDocumentExtractionSettingCommand(seed.OrganizationId, false), CancellationToken.None);

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new ExtractInboxDocumentCommand(seed.OrganizationId, document.Id), CancellationToken.None));
        Assert.Equal(1, extractor.CallCount);
    }

    // ---------------------------------------------------------------- the conversion seam

    /// <summary>
    /// Decision D's seam, asserted rather than described: the prefill query's permission key is the
    /// target type's own Create key, so the inbox can never be a side door around
    /// AuthorizationBehavior. A fifth target would extend exactly these two lists.
    /// </summary>
    [Fact]
    public void Every_supported_target_resolves_to_that_document_types_own_create_key()
    {
        Assert.Equal(
            [DocumentType.Invoice, DocumentType.PurchaseBill, DocumentType.Expense, DocumentType.Payment],
            InboxConversionTargets.Supported);

        Assert.Equal(PermissionKeys.InvoiceCreate,
            new GetInboxDocumentPrefillQuery(Guid.NewGuid(), Guid.NewGuid(), DocumentType.Invoice).PermissionKey);
        Assert.Equal(PermissionKeys.PurchaseBillCreate,
            new GetInboxDocumentPrefillQuery(Guid.NewGuid(), Guid.NewGuid(), DocumentType.PurchaseBill).PermissionKey);
        Assert.Equal(PermissionKeys.ExpenseCreate,
            new GetInboxDocumentPrefillQuery(Guid.NewGuid(), Guid.NewGuid(), DocumentType.Expense).PermissionKey);
        Assert.Equal(PermissionKeys.PaymentCreate,
            new GetInboxDocumentPrefillQuery(Guid.NewGuid(), Guid.NewGuid(), DocumentType.Payment).PermissionKey);

        Assert.False(InboxConversionTargets.IsSupported(DocumentType.CreditNote));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => InboxConversionTargets.CreatePermissionFor(DocumentType.CreditNote));
    }

    [Fact]
    public async Task The_status_filter_separates_the_pending_and_done_tabs()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var uploaderId = await CreateUserAsync(db);
        var storage = new FakeFileStorage();

        var pending = await UploadAsync(db, storage, organizationId, uploaderId, "pending.pdf");
        var filed = await UploadAsync(db, storage, organizationId, uploaderId, "filed.pdf");

        await new UpdateInboxDocumentCommandHandler(db).Handle(
            new UpdateInboxDocumentCommand(organizationId, filed.Id, null, null, UploadedDocumentStatus.Done),
            CancellationToken.None);

        var handler = new ListInboxDocumentsQueryHandler(db);

        var pendingTab = await handler.Handle(
            new ListInboxDocumentsQuery(organizationId, UploadedDocumentStatus.Pending), CancellationToken.None);
        var doneTab = await handler.Handle(
            new ListInboxDocumentsQuery(organizationId, UploadedDocumentStatus.Done), CancellationToken.None);

        Assert.Equal(pending.Id, Assert.Single(pendingTab.Items).Id);
        Assert.Equal(filed.Id, Assert.Single(doneTab.Items).Id);
    }

    // ---------------------------------------------------------------- helpers

    private static async Task<InboxDocumentDto> UploadAsync(
        IAppDbContext db,
        FakeFileStorage storage,
        Guid organizationId,
        Guid uploaderId,
        string fileName,
        byte[]? bytes = null)
    {
        bytes ??= Encoding.UTF8.GetBytes("scan");

        return await new UploadInboxDocumentCommandHandler(
            db, storage, new FakeCurrentUserService(uploaderId), TimeProvider.System).Handle(
            new UploadInboxDocumentCommand(
                organizationId, fileName, bytes.Length, "application/pdf", new MemoryStream(bytes)),
            CancellationToken.None);
    }

    private static async Task<Guid> CreateUserAsync(IAppDbContext db)
    {
        var user = Domain.Identity.User.Register("Uploader", $"{Guid.NewGuid():N}@example.com", "9800000000", "hash");
        db.Users.Add(user);
        await db.SaveChangesAsync(CancellationToken.None);
        return user.Id;
    }

    private static async Task<CreatePurchaseBillResult> CreatePurchaseBillAsync(IAppDbContext db, PurchasingSeed seed) =>
        await new CreatePurchaseBillCommandHandler(db).Handle(
            new CreatePurchaseBillCommand(
                seed.OrganizationId, seed.SupplierId, seed.WarehouseId, new DateOnly(2026, 4, 17),
                null, "INV-4471", false, null, null, null, null,
                [new PurchaseBillLineInput(seed.ProductId, 10m, 100m, VatRate.ThirteenPercentVat, ExpenditureClassification.Others, 0m)]),
            CancellationToken.None);

    private static async Task<PurchasingSeed> SeedPurchasingAsync(IAppDbContext db, bool aiEnabled = false)
    {
        var organizationId = Guid.NewGuid();
        var numberGenerator = new FakeDocumentNumberGenerator();
        var uploaderId = await CreateUserAsync(db);

        var supplier = await new CreateContactCommandHandler(db, numberGenerator).Handle(
            new CreateContactCommand(
                organizationId, ContactType.Supplier, "Global Supplies", null, "301234567", null, null, null, 0m),
            CancellationToken.None);

        var warehouse = await new CreateWarehouseCommandHandler(db).Handle(
            new CreateWarehouseCommand(organizationId, "Main Warehouse"), CancellationToken.None);

        var category = await new CreateProductCategoryCommandHandler(db).Handle(
            new CreateProductCategoryCommand(organizationId, "General", null), CancellationToken.None);
        var unit = await new CreateUnitOfMeasurementCommandHandler(db).Handle(
            new CreateUnitOfMeasurementCommand(organizationId, "Piece", "pc"), CancellationToken.None);
        var product = await new CreateProductCommandHandler(db, numberGenerator).Handle(
            new CreateProductCommand(
                organizationId, ProductType.Goods, "Widget", category.Id, unit.Id, null, true, 150m, 100m,
                VatRate.ThirteenPercentVat, 0, true),
            CancellationToken.None);

        var settings = TenantSettings.CreateDefault(organizationId);
        settings.SetAiDocumentExtractionEnabled(aiEnabled);
        db.TenantSettings.Add(settings);
        await db.SaveChangesAsync(CancellationToken.None);

        return new PurchasingSeed(organizationId, uploaderId, supplier.Id, warehouse.Id, product.Id);
    }

    private sealed record PurchasingSeed(
        Guid OrganizationId, Guid UploaderId, Guid SupplierId, Guid WarehouseId, Guid ProductId);
}
