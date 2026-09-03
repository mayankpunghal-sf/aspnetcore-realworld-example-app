using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Conduit.Infrastructure;
using Conduit.Infrastructure.Errors;
using Microsoft.EntityFrameworkCore;
// CA1304/CA1311/CA1862: string.ToLower() must stay inside EF Core expression trees so it translates to
// LOWER() on both SQL Server and PostgreSQL; string.Equals(StringComparison) is not translatable to SQL.
#pragma warning disable CA1304, CA1311, CA1862

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

        var person = await context
            .Persons.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Username!.ToLower() == username.ToLower(),
                cancellationToken
            );
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
                .FirstOrDefaultAsync(
                    x => x.Username!.ToLower() == currentUserName!.ToLower(),
                    cancellationToken
                );

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
