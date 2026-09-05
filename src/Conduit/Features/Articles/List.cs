using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Conduit.Infrastructure;
using Conduit.Infrastructure.Errors;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Conduit.Features.Articles;

public class List
{
    public record Query(
        string? Tag,
        string? Author,
        string? FavoritedUsername,
        int? Limit,
        int? Offset,
        bool IsFeed = false
    ) : IRequest<ArticlesEnvelope>;

    public class QueryHandler(ConduitContext context, ICurrentUserAccessor currentUserAccessor)
        : IRequestHandler<Query, ArticlesEnvelope>
    {
        public async ValueTask<ArticlesEnvelope> Handle(
            Query message,
            CancellationToken cancellationToken
        )
        {
            var queryable = context.Articles.GetAllData();

            if (message.IsFeed && currentUserAccessor.GetCurrentUsername() != null)
            {
                // note: Person.Followers holds the FollowedPeople rows where this person is the
                // observer, i.e. the people this person follows
                var currentUser = await context
                    .Persons.Include(x => x.Followers)
                    .FirstOrDefaultAsync(
                        x => x.Username == currentUserAccessor.GetCurrentUsername(),
                        cancellationToken
                    );

                if (currentUser is null)
                {
                    throw new RestException(HttpStatusCode.NotFound, "user", Constants.NOT_FOUND);
                }
                queryable = queryable.Where(x =>
                    currentUser.Followers.Select(y => y.TargetId).Contains(x.Author!.PersonId)
                );
            }

            if (!string.IsNullOrWhiteSpace(message.Tag))
            {
                var tag = await context.ArticleTags.FirstOrDefaultAsync(
                    x => x.TagId == message.Tag,
                    cancellationToken
                );
                if (tag != null)
                {
                    queryable = queryable.Where(x =>
                        x.ArticleTags.Select(y => y.TagId).Contains(tag.TagId)
                    );
                }
                else
                {
                    return new ArticlesEnvelope();
                }
            }

            if (!string.IsNullOrWhiteSpace(message.Author))
            {
                // ToLower() on the entity column is translated by EF Core into SQL LOWER(); .NET
                // culture rules do not apply server-side, so the culture analyzers are suppressed.
#pragma warning disable CA1304, CA1311, CA1862
                var author = await context.Persons.FirstOrDefaultAsync(
                    x => x.Username!.ToLower() == message.Author.ToLowerInvariant(),
                    cancellationToken
                );
#pragma warning restore CA1304, CA1311
                if (author != null)
                {
                    queryable = queryable.Where(x => x.Author == author);
                }
                else
                {
                    return new ArticlesEnvelope();
                }
            }

            if (!string.IsNullOrWhiteSpace(message.FavoritedUsername))
            {
#pragma warning disable CA1304, CA1311, CA1862
                var author = await context.Persons.FirstOrDefaultAsync(
                    x => x.Username!.ToLower() == message.FavoritedUsername.ToLowerInvariant(),
                    cancellationToken
                );
#pragma warning restore CA1304, CA1311
                if (author != null)
                {
                    queryable = queryable.Where(x =>
                        x.ArticleFavorites.Any(y => y.PersonId == author.PersonId)
                    );
                }
                else
                {
                    return new ArticlesEnvelope();
                }
            }

            var articles = await queryable
                .OrderByDescending(x => x.CreatedAt)
                .ThenByDescending(x => x.ArticleId)
                .Skip(message.Offset ?? 0)
                .Take(message.Limit ?? 20)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            // the spec omits the article body in list responses (null values are not serialized)
            foreach (var article in articles)
            {
                article.Body = null;
            }

            // populate author.following for the current user
            var currentUsername = currentUserAccessor.GetCurrentUsername();
            if (currentUsername != null)
            {
                var followedIds = await context
                    .FollowedPeople.Where(x => x.Observer!.Username == currentUsername)
                    .Select(x => x.TargetId)
                    .ToListAsync(cancellationToken);
                foreach (var author in articles.Select(x => x.Author))
                {
                    author?.IsFollowedByCurrentUser = followedIds.Contains(author.PersonId);
                }
            }

            return new ArticlesEnvelope { Articles = articles, ArticlesCount = queryable.Count() };
        }
    }
}
