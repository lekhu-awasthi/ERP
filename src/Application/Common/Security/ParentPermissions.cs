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
            : RecordOrThrow(
                parentType, PermissionKeys.ContactManage, PermissionKeys.DealManage, PermissionKeys.TaskManage);
    }

    /// <summary>The key required to read the files/comments on this parent.</summary>
    public static string ViewPermissionFor<TParentType>(TParentType parentType)
        where TParentType : struct, Enum
    {
        return DocumentParentTypes.TryToDocumentType(parentType) is { } documentType
            ? DocumentPermissions.ViewPermissionFor(documentType)
            : RecordOrThrow(
                parentType, PermissionKeys.ContactView, PermissionKeys.DealView, PermissionKeys.TaskView);
    }

    /// <summary>
    /// The non-document parents. Contact, Deal and WorkTask are records rather than documents, so
    /// each resolves to its own aggregate's key pair; every other member is a document and never
    /// reaches here. Resolved by member <i>name</i>, for the same reason
    /// <see cref="DocumentParentTypes"/> is: the parent enums do not and cannot share an ordinal
    /// order with <see cref="DocumentType"/>.
    ///
    /// <para><b>Phase 43 widened this from Contact alone</b> (39 carried item #1). Contact keeps its
    /// pre-split <c>Contact.View</c>/<c>Contact.Manage</c> pair; Deal uses <c>Crm.Deal.*</c> and
    /// WorkTask <c>Workflow.Task.*</c>, both of which are View/Manage pairs of the same shape, so a
    /// Member who may open a Deal may read the files on it and a Member who may not, may not.</para>
    ///
    /// <para><b>Tasks-as-a-tab still do not come through here</b>, and that is worth restating now
    /// that WorkTask is a parent name in two of the three enums: <c>CreateTask</c>/<c>UpdateTask</c>/
    /// <c>ListTasks</c> ride the blanket <c>Workflow.Task.*</c> pair for every parent (phase 13's
    /// design). What phase 43 adds is a <i>file or comment on</i> a task, which is this map's
    /// business, not a task on a task -- there is no such thing, and TaskParentType has no WorkTask
    /// member for that reason.</para>
    ///
    /// <para><c>Organization</c> is not reachable here: it is a <c>TaskParentType</c>-only parent,
    /// and there is a guard test saying so.</para>
    /// </summary>
    private static string RecordOrThrow<TParentType>(
        TParentType parentType, string contactKey, string dealKey, string taskKey)
        where TParentType : struct, Enum
    {
        return parentType.ToString() switch
        {
            nameof(DocumentType.Contact) => contactKey,
            nameof(DocumentType.Deal) => dealKey,
            nameof(DocumentType.WorkTask) => taskKey,
            _ => throw new ArgumentOutOfRangeException(
                nameof(parentType),
                parentType,
                $"{typeof(TParentType).Name}.{parentType} is neither a document nor a record anything "
                    + "can be filed against. (Organization is a task parent only.)"),
        };
    }
}
