using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Conduit.Features.Profiles;
using Conduit.Infrastructure;
using Conduit.Infrastructure.Errors;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
// CA1304/CA1311/CA1862: string.ToLower() must stay inside EF Core expression trees so it translates to
// LOWER() on both SQL Server and PostgreSQL; string.Equals(StringComparison) is not translatable to SQL.
#pragma warning disable CA1304, CA1311, CA1862

namespace Conduit.Features.Followers;

public class Delete
{
    public record Command(string Username) : IRequest<ProfileEnvelope>;

    public class CommandValidator : AbstractValidator<Command>
    {
        public CommandValidator() => RuleFor(x => x.Username).NotNull().NotEmpty();
    }

    public class QueryHandler(
        ConduitContext context,
        ICurrentUserAccessor currentUserAccessor,
        IProfileReader profileReader
    ) : IRequestHandler<Command, ProfileEnvelope>
    {
        public async ValueTask<ProfileEnvelope> Handle(
            Command message,
            CancellationToken cancellationToken
        )
        {
            var target = await context.Persons.FirstOrDefaultAsync(
                x => x.Username!.ToLower() == message.Username.ToLower(),
                cancellationToken
            );

            if (target is null)
            {
                throw new RestException(HttpStatusCode.NotFound, "profile", Constants.NOT_FOUND);
            }

            var observer = await context.Persons.FirstOrDefaultAsync(
                x => x.Username!.ToLower() == currentUserAccessor.GetCurrentUsername()!.ToLower(),
                cancellationToken
            );

            if (observer is null)
            {
                throw new RestException(HttpStatusCode.NotFound, "user", Constants.NOT_FOUND);
            }

            var followedPeople = await context.FollowedPeople.FirstOrDefaultAsync(
                x => x.ObserverId == observer.PersonId && x.TargetId == target.PersonId,
                cancellationToken
            );

            if (followedPeople != null)
            {
                context.FollowedPeople.Remove(followedPeople);
                await context.SaveChangesAsync(cancellationToken);
            }

            return await profileReader.ReadProfile(message.Username, cancellationToken);
        }
    }
}
