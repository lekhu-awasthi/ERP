namespace ErpApp.Domain.Common;

/// <summary>
/// Phase 27a -- bridges <see cref="DocumentType"/> to the three parent-type enums
/// (<c>TaskParentType</c>, <c>AttachmentParentType</c>, <c>CommentParentType</c>) <b>by member
/// name</b>.
///
/// <para>The mapping is <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)"/> and never an
/// ordinal cast. That is the phase-26a lesson stated as code: the enums do not share an ordinal
/// order and never can -- TaskParentType leads with Contact and Organization, AttachmentParentType
/// with Contact alone, DocumentType with Quotation -- so a cast would compile, appear to work for
/// whichever pair happened to line up, and silently mis-attribute every task and every file the
/// first time a member was inserted anywhere.</para>
///
/// <para>Name alignment is not left to hope either: <c>DocumentMechanismSweepGuardTests</c> asserts
/// that every transactional <see cref="DocumentType"/> round-trips through all three enums.</para>
/// </summary>
public static class DocumentParentTypes
{
    /// <summary>
    /// The parent-enum member corresponding to <paramref name="documentType"/>, or null when this
    /// document type has no counterpart (every non-transactional member -- Account, DataExport,
    /// the migrated register rows and so on).
    /// </summary>
    public static TParentType? TryFor<TParentType>(DocumentType documentType)
        where TParentType : struct, Enum
    {
        return Enum.TryParse<TParentType>(documentType.ToString(), ignoreCase: false, out var parsed)
            ? parsed
            : null;
    }

    /// <summary>
    /// The parent-enum member corresponding to <paramref name="documentType"/>. Throws for a
    /// document type with no counterpart -- callers reaching this have already been through
    /// <see cref="DocumentMechanisms.DetailTabs"/>, so a miss is a wiring bug, not user input.
    /// </summary>
    public static TParentType For<TParentType>(DocumentType documentType)
        where TParentType : struct, Enum
    {
        return TryFor<TParentType>(documentType)
            ?? throw new ArgumentOutOfRangeException(
                nameof(documentType),
                documentType,
                $"{documentType} has no {typeof(TParentType).Name} counterpart. Every transactional "
                    + "document type must have one with an identical member name -- see DocumentMechanisms.");
    }

    /// <summary>
    /// The <see cref="DocumentType"/> a parent-enum member names, or null when that member is not a
    /// document at all (TaskParentType.Contact / .Organization, CommentParentType.Contact).
    /// </summary>
    public static DocumentType? TryToDocumentType<TParentType>(TParentType parentType)
        where TParentType : struct, Enum
    {
        if (!Enum.TryParse<DocumentType>(parentType.ToString(), ignoreCase: false, out var parsed))
        {
            return null;
        }

        // Contact is a member of both vocabularies but means different things: a Contact record as a
        // task/comment parent, versus the Contact-code numbering pool. Only transactional types are
        // real documents here.
        return DocumentMechanisms.Transactional.Contains(parsed) ? parsed : null;
    }
}
