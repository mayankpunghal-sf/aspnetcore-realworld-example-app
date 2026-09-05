using System;
using System.Linq;
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

public class Login
{
    public class UserData
    {
        public string? Email { get; init; }

        public string? Password { get; init; }
    }

    public record Command(UserData User) : IRequest<UserEnvelope>;

    public class CommandValidator : AbstractValidator<Command>
    {
        public CommandValidator()
        {
            RuleFor(x => x.User).NotNull();
            RuleFor(x => x.User.Email).NotEmpty().WithMessage(Constants.BLANK);
            RuleFor(x => x.User.Password).NotEmpty().WithMessage(Constants.BLANK);
        }
    }

    public class Handler(
        ConduitContext context,
        IPasswordHasher passwordHasher,
        IJwtTokenGenerator jwtTokenGenerator,
        ConduitMapper mapper
    ) : IRequestHandler<Command, UserEnvelope>
    {
        public async ValueTask<UserEnvelope> Handle(
            Command message,
            CancellationToken cancellationToken
        )
        {
            var email = message.User.Email!.ToLowerInvariant();

            // ToLower() on the entity column is translated by EF Core into SQL LOWER(); .NET
            // culture rules do not apply server-side, so the culture analyzers are suppressed here.
#pragma warning disable CA1304, CA1311, CA1862
            var person = await context
                .Persons.Where(x => x.Email!.ToLower() == email)
                .SingleOrDefaultAsync(cancellationToken);
#pragma warning restore CA1304, CA1311
            if (person == null)
            {
                throw new RestException(HttpStatusCode.Unauthorized, "credentials", "invalid");
            }

            var hash = await passwordHasher.Hash(
                message.User.Password ?? throw new InvalidOperationException(),
                person.Salt
            );

            if (!person.Hash.SequenceEqual(hash))
            {
                throw new RestException(HttpStatusCode.Unauthorized, "credentials", "invalid");
            }

            var user = mapper.PersonToUser(person);
            user.Token = jwtTokenGenerator.CreateToken(
                person.Username ?? throw new InvalidOperationException()
            );
            return new UserEnvelope(user);
        }
    }
}
