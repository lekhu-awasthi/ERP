using ErpApp.Application.Common.Documents;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Printing.Queries.PrintDocument;
using ErpApp.Application.Configuration.Commands.SetCustomFieldValues;
using ErpApp.Application.Configuration.Commands.SetCustomStatus;
using ErpApp.Application.Configuration.Commands.SetTransactionReportingTags;
using ErpApp.Domain.Common;
using ErpApp.Domain.Communications;
using ErpApp.Domain.Configuration;
using ErpApp.Domain.Payments;
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
            (nameof(DocumentMechanisms.Emailable), DocumentMechanisms.Emailable),
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

    // ---------------------------------------------------------------------------------------
    // Phase 27b -- print, and Terms and Conditions. Same guard shape as 27a's four: the roadmap
    // said "the 9 unwired types", the live pass said all 15, and this is what keeps that true.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Every_transactional_type_is_printable()
    {
        Assert.Equal(
            DocumentMechanisms.Transactional.OrderBy(x => x).ToList(),
            DocumentMechanisms.Printable.OrderBy(x => x).ToList());
    }

    [Fact]
    public void Every_printable_type_resolves_its_own_view_permission()
    {
        foreach (var documentType in DocumentMechanisms.Printable)
        {
            var key = PrintDocumentPermissions.ViewPermissionFor(documentType);

            Assert.False(
                string.IsNullOrWhiteSpace(key),
                $"{documentType} is printable but resolves no View permission key.");

            Assert.Contains(
                key,
                PermissionKeyCatalog.AllKeys);
        }
    }

    /// <summary>Printing must never invent a wider permission than viewing the document itself --
    /// the whole reason PrintDocumentQuery declares no key of its own.</summary>
    [Fact]
    public void The_print_permission_map_refuses_every_non_document_type()
    {
        foreach (var documentType in DocumentMechanisms.NotApplicableReasons.Keys)
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => PrintDocumentPermissions.ViewPermissionFor(documentType));
        }
    }

    /// <summary>The live count, pinned. Confirm-live 2026-09-03 opened all eight line-item add
    /// forms: five carry the block, three do not. If a later phase widens this list it must have
    /// re-read the real screen, not reasoned from symmetry.</summary>
    [Fact]
    public void Terms_and_conditions_is_the_five_confirmed_types_and_not_the_three_confirmed_absent()
    {
        Assert.Equal(
            new[]
            {
                DocumentType.Quotation,
                DocumentType.SalesOrder,
                DocumentType.Invoice,
                DocumentType.CreditNote,
                DocumentType.PurchaseOrder,
            }.OrderBy(x => x).ToList(),
            DocumentMechanisms.TermsAndConditions.OrderBy(x => x).ToList());

        foreach (var absent in new[] { DocumentType.PurchaseBill, DocumentType.Expense, DocumentType.DebitNote })
        {
            Assert.DoesNotContain(absent, DocumentMechanisms.TermsAndConditions);
        }

        Assert.All(DocumentMechanisms.TermsAndConditions, x => Assert.Contains(x, DocumentMechanisms.Transactional));
    }

    /// <summary>The list is only worth anything if the aggregates behind it can actually store the
    /// text. Reflection rather than fifteen hand-written asserts, so adding a type to the list
    /// without adding the field fails here instead of at runtime.</summary>
    [Fact]
    public void Every_terms_type_has_a_Terms_property_and_a_SetTerms_mutator_on_its_aggregate()
    {
        var domainAssembly = typeof(DocumentType).Assembly;

        foreach (var documentType in DocumentMechanisms.TermsAndConditions)
        {
            var aggregate = domainAssembly.GetTypes().SingleOrDefault(t => t.Name == documentType.ToString() && t.IsClass);

            Assert.True(aggregate is not null, $"No Domain aggregate named {documentType}.");
            Assert.True(
                aggregate!.GetProperty("Terms")?.PropertyType == typeof(string),
                $"{documentType} carries Terms and Conditions but its aggregate has no string Terms property.");
            Assert.True(
                aggregate.GetMethod("SetTerms") is not null,
                $"{documentType} carries Terms and Conditions but its aggregate has no SetTerms mutator.");
        }
    }

    /// <summary>The mirror: nothing outside the list quietly grew the field. A stray Terms property
    /// on Purchase Bill would mean a form field nobody decided to add.</summary>
    [Fact]
    public void No_type_outside_the_terms_list_has_a_Terms_property()
    {
        var domainAssembly = typeof(DocumentType).Assembly;

        foreach (var documentType in DocumentMechanisms.Transactional.Except(DocumentMechanisms.TermsAndConditions))
        {
            var aggregate = domainAssembly.GetTypes().SingleOrDefault(t => t.Name == documentType.ToString() && t.IsClass);

            Assert.True(
                aggregate?.GetProperty("Terms") is null,
                $"{documentType} has a Terms property but is not in DocumentMechanisms.TermsAndConditions.");
        }
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

    // ---------------------------------------------------------------------------------------
    // Sweep 7 -- Send Email (Phase 30).
    // ---------------------------------------------------------------------------------------

    /// <summary>The six confirmed live, and the four confirmed absent. See
    /// docs/phase-30-status.md Step 1.2 for the probe, one real approved document per type.</summary>
    [Fact]
    public void Emailable_is_the_six_confirmed_types_and_not_the_four_confirmed_absent()
    {
        Assert.Equal(
            new[]
            {
                DocumentType.Quotation,
                DocumentType.SalesOrder,
                DocumentType.Invoice,
                DocumentType.CreditNote,
                DocumentType.Payment,
                DocumentType.PurchaseOrder,
            }.OrderBy(x => x).ToList(),
            DocumentMechanisms.Emailable.OrderBy(x => x).ToList());

        foreach (var absent in new[]
                 {
                     DocumentType.PurchaseBill,
                     DocumentType.DebitNote,
                     DocumentType.Expense,
                     DocumentType.JournalVoucher,
                 })
        {
            Assert.DoesNotContain(absent, DocumentMechanisms.Emailable);
        }

        Assert.All(DocumentMechanisms.Emailable, x => Assert.Contains(x, DocumentMechanisms.Transactional));
    }

    /// <summary>
    /// Emailing is strictly narrower than printing, and that asymmetry is the phase's own finding
    /// (the roadmap assumed they were the same set). Pinned, so a later phase widening one does not
    /// silently widen the other.
    /// </summary>
    [Fact]
    public void Every_emailable_type_is_printable_but_not_the_reverse()
    {
        Assert.All(DocumentMechanisms.Emailable, x => Assert.Contains(x, DocumentMechanisms.Printable));
        Assert.True(DocumentMechanisms.Emailable.Count < DocumentMechanisms.Printable.Count);
    }

    /// <summary>
    /// The rule the live pass actually established: Send Email exists exactly where an email
    /// template can be scoped. Asserted in <b>both</b> directions, so neither list can drift from
    /// the other -- which matters because the six/seven mismatch (one DocumentType.Payment, two
    /// payment contexts) makes a naive count comparison pass while the sets disagree.
    /// </summary>
    [Fact]
    public void Emailable_types_and_document_bearing_template_contexts_describe_the_same_set()
    {
        var fromContexts = Enum.GetValues<EmailTemplateContext>()
            .Select(EmailTemplateContexts.DocumentTypeFor)
            .Where(x => x is not null)
            .Select(x => x!.Value)
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        Assert.Equal(DocumentMechanisms.Emailable.OrderBy(x => x).ToList(), fromContexts);

        // And every emailable type maps forward to a context that maps back to it.
        foreach (var documentType in DocumentMechanisms.Emailable)
        {
            if (documentType == DocumentType.Payment)
            {
                Assert.Equal(
                    EmailTemplateContext.CustomerPayment,
                    EmailTemplateContexts.For(documentType, PaymentDirection.Received));
                Assert.Equal(
                    EmailTemplateContext.SupplierPayment,
                    EmailTemplateContexts.For(documentType, PaymentDirection.Paid));
                continue;
            }

            var context = EmailTemplateContexts.For(documentType);
            Assert.Equal(documentType, EmailTemplateContexts.DocumentTypeFor(context));
        }
    }

    /// <summary>A Payment's context depends on its direction, so asking without one is a wiring bug
    /// rather than a defaultable case -- pinned because silently defaulting to Customer Payment
    /// would mail a supplier a template that thanks them for paying us.</summary>
    [Fact]
    public void A_payment_context_cannot_be_resolved_without_a_direction()
    {
        Assert.Throws<ArgumentNullException>(() => EmailTemplateContexts.For(DocumentType.Payment));
    }

    [Fact]
    public void A_non_emailable_type_has_no_template_context()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EmailTemplateContexts.For(DocumentType.PurchaseBill));
    }

    /// <summary>
    /// The phase-26a/27a lesson restated on a fourth parent enum: mapped by name, never by ordinal.
    /// EmailParentType's members deliberately do not share an ordinal order with DocumentType -- it
    /// leads with Contact and omits nine document types -- so an ordinal cast would compile and
    /// silently attribute every email to the wrong document.
    /// </summary>
    [Fact]
    public void Every_emailable_type_round_trips_through_EmailParentType_by_name()
    {
        foreach (var documentType in DocumentMechanisms.Emailable)
        {
            var parentType = DocumentParentTypes.For<EmailParentType>(documentType);

            Assert.Equal(documentType.ToString(), parentType.ToString());
            Assert.Equal(documentType, DocumentParentTypes.TryToDocumentType(parentType));
        }

        // The ordinals genuinely differ, which is what makes the by-name rule load-bearing rather
        // than decorative: DocumentType.Invoice is 2 and EmailParentType.Invoice is 3, because this
        // enum leads with Contact and omits nine document types. An ordinal cast would compile,
        // appear to work for whichever pair happened to line up, and mis-attribute the rest.
        Assert.NotEqual((int)DocumentType.Invoice, (int)EmailParentType.Invoice);
        Assert.NotEqual((int)DocumentType.Payment, (int)EmailParentType.Payment);
        Assert.Null(DocumentParentTypes.TryFor<EmailParentType>(DocumentType.PurchaseBill));
    }

    /// <summary>Contact is the only non-document parent an email can hang off -- the Contact detail
    /// page's own Send Email action.</summary>
    [Fact]
    public void Contact_is_the_only_non_document_email_parent()
    {
        var nonDocuments = Enum.GetValues<EmailParentType>()
            .Where(x => DocumentParentTypes.TryToDocumentType(x) is null)
            .ToList();

        Assert.Equal([EmailParentType.Contact], nonDocuments);
    }

    /// <summary>Every context resolves a parent type, including the two that are about a Contact
    /// rather than a document.</summary>
    [Fact]
    public void Every_template_context_resolves_a_parent_type()
    {
        foreach (var context in Enum.GetValues<EmailTemplateContext>())
        {
            var parentType = EmailTemplateContexts.ParentTypeFor(context);

            if (EmailTemplateContexts.DocumentTypeFor(context) is null)
            {
                Assert.Equal(EmailParentType.Contact, parentType);
            }
            else
            {
                Assert.NotEqual(EmailParentType.Contact, parentType);
            }
        }
    }
}
