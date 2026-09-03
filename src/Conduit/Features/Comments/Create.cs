using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Conduit.Domain;
using Conduit.Infrastructure;
using Conduit.Infrastructure.Errors;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
// CA1304/CA1311/CA1862: string.ToLower() must stay inside EF Core expression trees so it translates to
// LOWER() on both SQL Server and PostgreSQL; string.Equals(StringComparison) is not translatable to SQL.
#pragma warning disable CA1304, CA1311, CA1862

namespace Conduit.Features.Comments;

public class Create
{
    public record CommentData(string? Body);

    public record Command(Model Model, string Slug) : IRequest<CommentEnvelope>;

    public record Model(CommentData Comment);

    public class CommandValidator : AbstractValidator<Command>
    {
        public CommandValidator() =>
            RuleFor(x => x.Model.Comment.Body).NotEmpty().WithMessage(Constants.BLANK);
    }

    public class Handler(ConduitContext context, ICurrentUserAccessor currentUserAccessor)
        : IRequestHandler<Command, CommentEnvelope>
    {
        public async ValueTask<CommentEnvelope> Handle(
            Command message,
            CancellationToken cancellationToken
        )
        {
            var article = await context
                .Articles.Include(x => x.Comments)
                .FirstOrDefaultAsync(x => x.Slug == message.Slug, cancellationToken);

            if (article == null)
            {
                throw new RestException(HttpStatusCode.NotFound, "article", Constants.NOT_FOUND);
            }

            var author = await context.Persons.FirstAsync(
                x => x.Username!.ToLower() == currentUserAccessor.GetCurrentUsername()!.ToLower(),
                cancellationToken
            );

            var comment = new Comment
            {
                Author = author,
                Body = message.Model.Comment.Body ?? string.Empty,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };
            await context.Comments.AddAsync(comment, cancellationToken);

            article.Comments.Add(comment);

            await context.SaveChangesAsync(cancellationToken);

            return new CommentEnvelope(comment);
        }
    }
}
