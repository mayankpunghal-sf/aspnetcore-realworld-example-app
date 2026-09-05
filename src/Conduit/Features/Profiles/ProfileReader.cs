using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Conduit.Infrastructure;
using Conduit.Infrastructure.Errors;
using Microsoft.EntityFrameworkCore;

namespace Conduit.Features.Profiles;

public class ProfileReader(
    ConduitContext context,
    ICurrentUserAccessor currentUserAccessor,
    ConduitMapper mapper
) : IProfileReader
{
    public async Task<ProfileEnvelope> ReadProfile(
        string username,
        CancellationToken cancellationToken
    )
    {
        var currentUserName = currentUserAccessor.GetCurrentUsername();

        var usernameLower = username.ToLowerInvariant();

        // ToLower() on the entity column is translated by EF Core into SQL LOWER(); .NET
        // culture rules do not apply server-side, so the culture analyzers are suppressed here.
#pragma warning disable CA1304, CA1311, CA1862
        var person = await context
            .Persons.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Username!.ToLower() == usernameLower, cancellationToken);
#pragma warning restore CA1304, CA1311
        if (person is null)
        {
            throw new RestException(HttpStatusCode.NotFound, "profile", Constants.NOT_FOUND);
        }

        var profile = mapper.PersonToProfile(person);

        if (currentUserName != null)
        {
            var currentPerson = await context
                .Persons.Include(x => x.Following)
                .Include(x => x.Followers)
                .FirstOrDefaultAsync(x => x.Username == currentUserName, cancellationToken);

            if (currentPerson is null)
            {
                throw new RestException(HttpStatusCode.NotFound, "user", Constants.NOT_FOUND);
            }

            if (currentPerson.Followers.Any(x => x.TargetId == person.PersonId))
            {
                profile.IsFollowed = true;
            }
        }

        return new ProfileEnvelope(profile);
    }
}
