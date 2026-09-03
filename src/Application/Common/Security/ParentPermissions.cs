using ErpApp.Domain.Common;

namespace ErpApp.Application.Common.Security;

/// <summary>
/// Phase 27a -- the permission keys for a file or a comment hung off a polymorphic parent, whichever
/// of the two enums (<c>AttachmentParentType</c>, <c>CommentParentType</c>) names it.
///
/// <para>Before this, <c>UploadAttachmentCommand</c> hardcoded <c>ContactManage</c> and
/// <c>ListAttachmentsQuery</c> hardcoded <c>ContactView</c>, because Contact was the only parent
/// there was. With documents as parents that is wrong in a way that matters: a Member who may edit
/// Invoices but holds no Contact grant could not attach a file to an invoice, and -- the direction
/// that actually leaks -- a Member holding only <c>ContactView</c> could read files attached to
/// documents they cannot open. The key has to come from the parent, so it does.</para>
///
/// <para><b>Tasks deliberately do not use this.</b> <c>CreateTask</c>/<c>UpdateTask</c>/
/// <c>ListTasks</c> ride the blanket <c>Workflow.Task.Manage</c>/<c>.View</c> pair for every parent,
/// which is Phase 13's own design: a task is a workflow object in its own right, gated by workflow
/// permissions, not a property of the thing it points at. Extending <c>TaskParentType</c> therefore
/// needed no permission change at all -- worth stating, because "we swept three parent enums and
/// only two needed keys" looks like an omission until you know why.</para>
///
/// <para>Contact keeps its own <c>Contact.View</c>/<c>Contact.Manage</c> pair -- Contacts predate the
/// View/Create/Edit/Approve split and have never had an Edit key. Everything else is a document and
/// resolves through <see cref="DocumentPermissions"/>. <c>Organization</c> is not reachable here: it
/// is a <c>TaskParentType</c>-only parent, and there is a guard test saying so.</para>
/// </summary>
public static class ParentPermissions
{
    /// <summary>The key required to attach, change or remove a file/comment on this parent.</summary>
    public static string EditPermissionFor<TParentType>(TParentType parentType)
        where TParentType : struct, Enum
    {
        return DocumentParentTypes.TryToDocumentType(parentType) is { } documentType
            ? DocumentPermissions.EditPermissionFor(documentType)
            : ContactOrThrow(parentType, PermissionKeys.ContactManage);
    }

    /// <summary>The key required to read the files/comments on this parent.</summary>
    public static string ViewPermissionFor<TParentType>(TParentType parentType)
        where TParentType : struct, Enum
    {
        return DocumentParentTypes.TryToDocumentType(parentType) is { } documentType
            ? DocumentPermissions.ViewPermissionFor(documentType)
            : ContactOrThrow(parentType, PermissionKeys.ContactView);
    }

    /// <summary>
    /// Contact is the only non-document parent a file or comment can have. Resolved by member name,
    /// for the same reason <see cref="DocumentParentTypes"/> is: the parent enums do not and cannot
    /// share an ordinal order with <see cref="DocumentType"/>. <c>DocumentMechanismSweepGuardTests</c>
    /// pins that Contact is the only non-document member of these two enums, so this cannot silently
    /// miss one.
    /// </summary>
    private static string ContactOrThrow<TParentType>(TParentType parentType, string contactKey)
        where TParentType : struct, Enum
    {
        return parentType.ToString() == nameof(DocumentType.Contact)
            ? contactKey
            : throw new ArgumentOutOfRangeException(
                nameof(parentType),
                parentType,
                $"{typeof(TParentType).Name}.{parentType} is neither a document nor a Contact, so nothing "
                    + "can be filed against it. (Organization is a task parent only.)");
    }
}
