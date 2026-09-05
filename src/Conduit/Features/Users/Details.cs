using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Conduit.Infrastructure;
using Conduit.Infrastructure.Errors;
using Conduit.Infrastructure.Security;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Conduit.Features.Users;

public class Details
{
    public record Query(string Username) : IRequest<UserEnvelope>;

    public class QueryValidator : AbstractValidator<Query>
    {
        public QueryValidator() => RuleFor(x => x.Username).NotNull().NotEmpty();
    }

    public class QueryHandler(
        ConduitContext context,
        IJwtTokenGenerator jwtTokenGenerator,
        ConduitMapper mapper
    ) : IRequestHandler<Query, UserEnvelope>
    {
        public async ValueTask<UserEnvelope> Handle(
            Query message,
            CancellationToken cancellationToken
        )
        {
            var username = message.Username.ToLowerInvariant();

            // ToLower() on the entity column is translated by EF Core into SQL LOWER(); .NET
            // culture rules do not apply server-side, so the culture analyzers are suppressed here.
#pragma warning disable CA1304, CA1311, CA1862
            var person = await context
                .Persons.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Username!.ToLower() == username, cancellationToken);
#pragma warning restore CA1304, CA1311

            if (person == null)
            {
                throw new RestException(HttpStatusCode.NotFound, "user", Constants.NOT_FOUND);
            }

            var user = mapper.PersonToUser(person);
            user.Token = jwtTokenGenerator.CreateToken(
                person.Username ?? throw new InvalidOperationException()
            );
            return new UserEnvelope(user);
        }
    }
}
