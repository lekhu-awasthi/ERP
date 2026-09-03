using ErpApp.Application.Common.Documents;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Configuration.Commands.SetCustomFieldValues;
using ErpApp.Application.Configuration.Commands.SetCustomStatus;
using ErpApp.Application.Configuration.Commands.SetTransactionReportingTags;
using ErpApp.Domain.Common;
using ErpApp.Domain.Workflow;

namespace ErpApp.Application.UnitTests.Common;

/// <summary>
/// Phase 27a, <b>proving the four sweeps complete, mechanically.</b>
///
/// <para>27a rolled four cross-cutting mechanisms out across every document type: Custom Fields (13
/// types), Custom Status (4), Reporting Tags (17) and the Tasks/Documents/Activity detail tabs (15).
/// The failure mode of a sweep phase is not getting one wrong today. It is <b>phase 29 adding a
/// document type and silently getting none of them</b> -- no compiler error, no failing test, just a
/// screen quietly missing its Custom Fields block and a permission map that throws the first time a
/// real user touches it.</para>
///
/// <para>A paragraph of intent in a status doc does not survive that. This does. Every
/// <see cref="DocumentType"/> must be classified -- transactional, or given a written reason in
/// <see cref="DocumentMechanisms.NotApplicableReasons"/> -- and every classified type must actually
/// resolve through the permission maps, the existence reader and the parent enums. Adding an enum
/// member without deciding is a build failure.</para>
///
/// <para>Modelled on phase-23's <c>sweep-guard.spec.ts</c> and phase-24's
/// <c>ProductVariantSweepGuardTests</c>, including their self-checks: that the thing being scanned
/// is non-empty, and that every exemption still refers to something real.</para>
/// </summary>
public class DocumentMechanismSweepGuardTests
{
    private static readonly DocumentType[] AllDocumentTypes = Enum.GetValues<DocumentType>();

    // ---------------------------------------------------------------------------------------
    // The self-checks. Without these the assertions below could pass vacuously, which is the
    // classic way a guard test stops guarding anything.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void The_enum_being_guarded_is_not_empty()
    {
        Assert.True(AllDocumentTypes.Length >= 24, "DocumentType shrank unexpectedly -- the guard below may be vacuous.");
        Assert.NotEmpty(DocumentMechanisms.Transactional);
        Assert.NotEmpty(DocumentMechanisms.NotApplicableReasons);
    }

    [Fact]
    public void Every_document_type_is_classified_exactly_once()
    {
        var transactional = DocumentMechanisms.Transactional.ToHashSet();
        var excluded = DocumentMechanisms.NotApplicableReasons.Keys.ToHashSet();

        var unclassified = AllDocumentTypes.Where(x => !transactional.Contains(x) && !excluded.Contains(x)).ToList();
        Assert.True(
            unclassified.Count == 0,
            "New DocumentType member(s) with no 27a classification: "
                + string.Join(", ", unclassified)
                + ". Add each to DocumentMechanisms.Transactional (and the mechanism lists it belongs "
                + "to) or to NotApplicableReasons with the reason it carries none of them.");

        var bothWays = AllDocumentTypes.Where(x => transactional.Contains(x) && excluded.Contains(x)).ToList();
        Assert.True(
            bothWays.Count == 0,
            "DocumentType member(s) classified as both transactional and not-applicable: " + string.Join(", ", bothWays));
    }

    [Fact]
    public void Every_exclusion_reason_is_a_real_document_type_and_actually_says_something()
    {
        foreach (var (documentType, reason) in DocumentMechanisms.NotApplicableReasons)
        {
            Assert.Contains(documentType, AllDocumentTypes);
            Assert.False(
                string.IsNullOrWhiteSpace(reason),
                $"{documentType} is excluded from the 27a sweeps with an empty reason. Say why.");
        }
    }

    [Fact]
    public void Every_mechanism_list_contains_only_classified_document_types_and_no_duplicates()
    {
        var lists = new (string Name, IReadOnlyList<DocumentType> Types)[]
        {
            (nameof(DocumentMechanisms.CustomFields), DocumentMechanisms.CustomFields),
            (nameof(DocumentMechanisms.CustomStatus), DocumentMechanisms.CustomStatus),
            (nameof(DocumentMechanisms.ReportingTags), DocumentMechanisms.ReportingTags),
            (nameof(DocumentMechanisms.DetailTabs), DocumentMechanisms.DetailTabs),
        };

        foreach (var (name, types) in lists)
        {
            Assert.True(types.Distinct().Count() == types.Count, $"{name} lists a document type twice.");

            foreach (var documentType in types)
            {
                Assert.Contains(documentType, AllDocumentTypes);
            }
        }
    }

    // ---------------------------------------------------------------------------------------
    // Sweep 1 -- Custom Fields.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Every_custom_fields_type_resolves_a_key_and_an_existence_check()
    {
        foreach (var documentType in DocumentMechanisms.CustomFields)
        {
            // Both directions of the pair, since a screen needs View to render and Edit to save.
            Assert.False(string.IsNullOrWhiteSpace(CustomFieldValuePermissions.EditPermissionFor(documentType)));
            Assert.False(string.IsNullOrWhiteSpace(CustomFieldValuePermissions.ViewPermissionFor(documentType)));

            // And the handler must know how to find the document, or every save would 500.
            AssertDocumentExistenceReaderKnows(documentType);
        }
    }

    [Fact]
    public void Custom_fields_is_the_confirmed_subset_of_transactional_types()
    {
        // The two live-confirmed absences. If a later phase decides these should carry custom
        // fields, that is a live-confirm decision to record, not a list to quietly widen.
        Assert.DoesNotContain(DocumentType.WarehouseTransfer, DocumentMechanisms.CustomFields);
        Assert.DoesNotContain(DocumentType.InventoryAdjustment, DocumentMechanisms.CustomFields);

        Assert.Equal(13, DocumentMechanisms.CustomFields.Count);

        foreach (var documentType in DocumentMechanisms.CustomFields)
        {
            Assert.Contains(documentType, DocumentMechanisms.Transactional);
        }
    }

    // ---------------------------------------------------------------------------------------
    // Sweep 2 -- Custom Status.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Every_custom_status_type_resolves_a_key_and_an_existence_check()
    {
        foreach (var documentType in DocumentMechanisms.CustomStatus)
        {
            Assert.False(string.IsNullOrWhiteSpace(CustomStatusPermissions.EditPermissionFor(documentType)));
            AssertDocumentExistenceReaderKnows(documentType);
        }
    }

    [Fact]
    public void Cheque_is_still_excluded_from_custom_status()
    {
        // Phase 20b's finding, restated as a test because it is the one exclusion someone reading
        // "five live sections, four wired" would otherwise assume was an oversight: Cheque's five
        // custom-status values ARE the five members of ChequeStatus, so wiring it would fork the
        // native lifecycle. Cheque has no DocumentType member at all, which is what makes it
        // unreachable -- if a later phase adds one, this assertion tells them to decide deliberately.
        Assert.DoesNotContain("Cheque", Enum.GetNames<DocumentType>());
    }

    // ---------------------------------------------------------------------------------------
    // Sweep 3 -- Reporting Tags.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Every_transactional_type_carries_reporting_tags()
    {
        // The widest sweep: no transactional type is exempt, so this is a strict superset check
        // rather than a list comparison -- a new transactional type is in by default and fails here
        // only if someone removes it.
        foreach (var documentType in DocumentMechanisms.Transactional)
        {
            Assert.Contains(documentType, DocumentMechanisms.ReportingTags);
        }

        Assert.Contains(DocumentType.OpeningBalance, DocumentMechanisms.ReportingTags);
        Assert.Contains(DocumentType.OpeningStock, DocumentMechanisms.ReportingTags);
    }

    [Fact]
    public void Every_reporting_tag_type_resolves_a_key_and_an_existence_check()
    {
        foreach (var documentType in DocumentMechanisms.ReportingTags)
        {
            Assert.False(string.IsNullOrWhiteSpace(TransactionReportingTagPermissions.EditPermissionFor(documentType)));
            Assert.False(string.IsNullOrWhiteSpace(TransactionReportingTagPermissions.ViewPermissionFor(documentType)));
            AssertDocumentExistenceReaderKnows(documentType);
        }
    }

    // ---------------------------------------------------------------------------------------
    // Sweep 4 -- the detail-page tabs, i.e. the three parent enums.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Every_detail_tabs_type_has_a_counterpart_in_all_three_parent_enums()
    {
        foreach (var documentType in DocumentMechanisms.DetailTabs)
        {
            // For<T> throws when the names do not line up, which is the whole assertion.
            var taskParent = DocumentParentTypes.For<TaskParentType>(documentType);
            var attachmentParent = DocumentParentTypes.For<AttachmentParentType>(documentType);
            var commentParent = DocumentParentTypes.For<CommentParentType>(documentType);

            // And back again, so the mapping is a bijection rather than three enums that merely
            // happen to contain the name.
            Assert.Equal(documentType, DocumentParentTypes.TryToDocumentType(taskParent));
            Assert.Equal(documentType, DocumentParentTypes.TryToDocumentType(attachmentParent));
            Assert.Equal(documentType, DocumentParentTypes.TryToDocumentType(commentParent));
        }
    }

    [Fact]
    public void The_only_non_document_parents_are_contact_and_organization()
    {
        // ParentPermissions and WorkflowValidation both switch on these two names. This test is what
        // makes that switch safe: a third non-document parent added to any of the three enums fails
        // here rather than falling into an ArgumentOutOfRangeException at runtime.
        var nonDocumentParents = new[]
        {
            Enum.GetNames<TaskParentType>(),
            Enum.GetNames<AttachmentParentType>(),
            Enum.GetNames<CommentParentType>(),
        }
            .SelectMany(names => names)
            .Where(name => !Enum.TryParse<DocumentType>(name, out var parsed)
                || !DocumentMechanisms.Transactional.Contains(parsed))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(["Contact", "Organization"], nonDocumentParents);
    }

    [Fact]
    public void A_file_or_comment_parent_resolves_a_key_for_every_document_type()
    {
        foreach (var documentType in DocumentMechanisms.DetailTabs)
        {
            var attachmentParent = DocumentParentTypes.For<AttachmentParentType>(documentType);
            var commentParent = DocumentParentTypes.For<CommentParentType>(documentType);

            Assert.Equal(
                DocumentPermissions.EditPermissionFor(documentType),
                ParentPermissions.EditPermissionFor(attachmentParent));
            Assert.Equal(
                DocumentPermissions.ViewPermissionFor(documentType),
                ParentPermissions.ViewPermissionFor(commentParent));
        }

        // Contact keeps its own pre-split pair rather than being forced into View/Edit.
        Assert.Equal(PermissionKeys.ContactManage, ParentPermissions.EditPermissionFor(AttachmentParentType.Contact));
        Assert.Equal(PermissionKeys.ContactView, ParentPermissions.ViewPermissionFor(CommentParentType.Contact));
    }

    // ---------------------------------------------------------------------------------------
    // Cross-cutting: the maps must not answer for anything unclassified.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void The_permission_map_refuses_every_type_nothing_can_be_attached_to()
    {
        // The floor. DocumentPermissions answers for the 15 transactional types plus the two
        // opening-balance kinds; everything else must throw rather than silently returning some
        // neighbouring document's key, which would be an authorization bug rather than a crash.
        var answerable = DocumentMechanisms.ReportingTags.ToHashSet();

        foreach (var documentType in AllDocumentTypes.Where(x => !answerable.Contains(x)))
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => DocumentPermissions.EditPermissionFor(documentType));
            Assert.Throws<ArgumentOutOfRangeException>(() => DocumentPermissions.ViewPermissionFor(documentType));
        }
    }

    [Fact]
    public void Enum_mapping_is_by_name_and_not_by_ordinal()
    {
        // The phase-26a lesson, pinned. TaskParentType leads with Contact and Organization, so its
        // ordinals are offset from DocumentType's by two -- a cast would compile, and would map
        // Quotation to Invoice. If someone "simplifies" DocumentParentTypes to a cast, this fails.
        Assert.Equal(TaskParentType.Invoice, DocumentParentTypes.For<TaskParentType>(DocumentType.Invoice));
        Assert.NotEqual((int)DocumentType.Invoice, (int)TaskParentType.Invoice);

        Assert.Equal(
            AttachmentParentType.ProductionJournal,
            DocumentParentTypes.For<AttachmentParentType>(DocumentType.ProductionJournal));
        Assert.NotEqual((int)DocumentType.ProductionJournal, (int)AttachmentParentType.ProductionJournal);
    }

    /// <summary>
    /// The reader must have a real arm for this type -- proven by the exception it does <i>not</i>
    /// throw. A type it does not know throws ArgumentOutOfRangeException before touching the
    /// database; a type it knows gets as far as the (empty) query and reports NotFound.
    /// </summary>
    private static void AssertDocumentExistenceReaderKnows(DocumentType documentType)
    {
        var db = TestSupport.TestAppDbContext.Create();

        var exception = Record.ExceptionAsync(() => DocumentExistenceReader.EnsureExistsAsync(
            db, Guid.NewGuid(), documentType, Guid.NewGuid(), CancellationToken.None)).GetAwaiter().GetResult();

        Assert.IsNotType<ArgumentOutOfRangeException>(exception);
        Assert.IsType<Application.Common.Exceptions.NotFoundException>(exception);
    }
}
