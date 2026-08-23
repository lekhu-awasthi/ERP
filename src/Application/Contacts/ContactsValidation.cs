using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Contacts;

/// <summary>Shared existence checks reused by every Contact-scoped Phase 18 command (Personnel,
/// Comment, Attachment) -- mirrors Workflow.WorkflowValidation's precedent.</summary>
internal static class ContactsValidation
{
    public static async Task EnsureContactExistsAsync(
        IAppDbContext db, Guid organizationId, Guid contactId, CancellationToken cancellationToken)
    {
        var exists = await db.Contacts.AnyAsync(
            x => x.Id == contactId && x.OrganizationId == organizationId, cancellationToken);

        if (!exists)
        {
            throw new NotFoundException("Contact not found.");
        }
    }
}
