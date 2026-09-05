using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Conduit.Domain;
using Conduit.Features.Profiles;
using Conduit.Infrastructure;
using Conduit.Infrastructure.Errors;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Conduit.Features.Followers;

public class Add
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
            var usernameLower = message.Username.ToLowerInvariant();

            // ToLower() on the entity column is translated by EF Core into SQL LOWER(); .NET
            // culture rules do not apply server-side, so the culture analyzers are suppressed here.
#pragma warning disable CA1304, CA1311, CA1862
            var target = await context.Persons.FirstOrDefaultAsync(
                x => x.Username!.ToLower() == usernameLower,
                cancellationToken
            );
#pragma warning restore CA1304, CA1311

            if (target is null)
            {
                throw new RestException(HttpStatusCode.NotFound, "profile", Constants.NOT_FOUND);
            }

            var observer = await context.Persons.FirstOrDefaultAsync(
                x => x.Username == currentUserAccessor.GetCurrentUsername(),
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

            if (followedPeople == null)
            {
                followedPeople = new FollowedPeople
                {
                    Observer = observer,
                    ObserverId = observer.PersonId,
                    Target = target,
                    TargetId = target.PersonId,
                };
                await context.FollowedPeople.AddAsync(followedPeople, cancellationToken);
                await context.SaveChangesAsync(cancellationToken);
            }

            return await profileReader.ReadProfile(message.Username, cancellationToken);
        }
    }
}
