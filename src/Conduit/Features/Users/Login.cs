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
// CA1304/CA1311/CA1862: string.ToLower() must stay inside EF Core expression trees so it translates to
// LOWER() on both SQL Server and PostgreSQL; string.Equals(StringComparison) is not translatable to SQL.
#pragma warning disable CA1304, CA1311, CA1862

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
            var person = await context
                .Persons.Where(x => x.Email!.ToLower() == message.User.Email!.ToLower())
                .SingleOrDefaultAsync(cancellationToken);
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
