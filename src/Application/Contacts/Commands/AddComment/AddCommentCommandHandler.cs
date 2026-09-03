using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Workflow;
using ErpApp.Domain.Contacts;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Contacts.Commands.AddComment;

public sealed class AddCommentCommandHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<AddCommentCommand, CommentResult>
{
    public async Task<CommentResult> Handle(AddCommentCommand request, CancellationToken cancellationToken)
    {
        // Phase 27a: the same generic parent check WorkTask and Attachment use, so a comment cannot
        // be filed against a nonexistent document any more than against a nonexistent Contact.
        await WorkflowValidation.EnsureParentExistsAsync(
            db, request.OrganizationId, request.ParentType, request.ParentId, cancellationToken);

        var comment = Comment.Create(
            request.OrganizationId, request.ParentType, request.ParentId, request.Content, currentUser.UserId);

        db.Comments.Add(comment);
        await db.SaveChangesAsync(cancellationToken);

        var authorName = await db.Users
            .Where(x => x.Id == currentUser.UserId)
            .Select(x => x.FullName)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("User not found.");

        return new CommentResult(
            comment.Id, comment.ParentType, comment.ParentId, comment.Content, comment.AuthorUserId,
            authorName, comment.CreatedAt);
    }
}
