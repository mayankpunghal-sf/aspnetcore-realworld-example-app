using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Conduit.Features.Articles;
using Conduit.Infrastructure;
using Conduit.Infrastructure.Errors;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
// CA1304/CA1311/CA1862: string.ToLower() must stay inside EF Core expression trees so it translates to
// LOWER() on both SQL Server and PostgreSQL; string.Equals(StringComparison) is not translatable to SQL.
#pragma warning disable CA1304, CA1311, CA1862

namespace Conduit.Features.Favorites;

public class Delete
{
    public record Command(string Slug) : IRequest<ArticleEnvelope>;

    public class CommandValidator : AbstractValidator<Command>
    {
        public CommandValidator() => RuleFor(x => x.Slug).NotNull().NotEmpty();
    }

    public class QueryHandler(ConduitContext context, ICurrentUserAccessor currentUserAccessor)
        : IRequestHandler<Command, ArticleEnvelope>
    {
        public async ValueTask<ArticleEnvelope> Handle(
            Command message,
            CancellationToken cancellationToken
        )
        {
            var article =
                await context.Articles.FirstOrDefaultAsync(
                    x => x.Slug == message.Slug,
                    cancellationToken
                )
                ?? throw new RestException(HttpStatusCode.NotFound, "article", Constants.NOT_FOUND);

            var person = await context.Persons.FirstOrDefaultAsync(
                x => x.Username!.ToLower() == currentUserAccessor.GetCurrentUsername()!.ToLower(),
                cancellationToken
            );
            if (person is null)
            {
                throw new RestException(HttpStatusCode.NotFound, "article", Constants.NOT_FOUND);
            }

            var favorite = await context.ArticleFavorites.FirstOrDefaultAsync(
                x => x.ArticleId == article.ArticleId && x.PersonId == person.PersonId,
                cancellationToken
            );

            if (favorite != null)
            {
                context.ArticleFavorites.Remove(favorite);
                await context.SaveChangesAsync(cancellationToken);
            }

            article = await context
                .Articles.GetAllData()
                .FirstOrDefaultAsync(x => x.ArticleId == article.ArticleId, cancellationToken);
            if (article is null)
            {
                throw new RestException(HttpStatusCode.NotFound, "article", Constants.NOT_FOUND);
            }

            return new ArticleEnvelope(article);
        }
    }
}
