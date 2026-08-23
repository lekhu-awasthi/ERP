using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Contacts;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Contacts.Commands.AddComment;

public sealed class AddCommentCommandHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<AddCommentCommand, CommentResult>
{
    public async Task<CommentResult> Handle(AddCommentCommand request, CancellationToken cancellationToken)
    {
        await ContactsValidation.EnsureContactExistsAsync(db, request.OrganizationId, request.ContactId, cancellationToken);

        var comment = Comment.Create(request.OrganizationId, request.ContactId, request.Content, currentUser.UserId);

        db.Comments.Add(comment);
        await db.SaveChangesAsync(cancellationToken);

        var authorName = await db.Users
            .Where(x => x.Id == currentUser.UserId)
            .Select(x => x.FullName)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("User not found.");

        return new CommentResult(comment.Id, comment.ContactId, comment.Content, comment.AuthorUserId, authorName, comment.CreatedAt);
    }
}
