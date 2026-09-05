using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Conduit.Domain;
using Conduit.Infrastructure;
using Conduit.Infrastructure.Errors;
using Conduit.Infrastructure.Security;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Conduit.Features.Users;

public class Create
{
    public record UserData(string? Username, string? Email, string? Password);

    public record Command(UserData User) : IRequest<UserEnvelope>;

    public class CommandValidator : AbstractValidator<Command>
    {
        public CommandValidator()
        {
            RuleFor(x => x.User.Username).NotEmpty().WithMessage(Constants.BLANK);
            RuleFor(x => x.User.Email).NotEmpty().WithMessage(Constants.BLANK);
            RuleFor(x => x.User.Password)
                .NotEmpty()
                .WithMessage(Constants.BLANK)
                .MinimumLength(8)
                .WithMessage(Constants.PASSWORD_TOO_SHORT);
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
            var username = message.User.Username!.ToLowerInvariant();
            var email = message.User.Email!.ToLowerInvariant();

            // ToLower() on the entity column is translated by EF Core into SQL LOWER(); .NET
            // culture rules do not apply server-side, so the culture analyzers are suppressed here.
#pragma warning disable CA1304, CA1311, CA1862
            if (
                await context
                    .Persons.Where(x => x.Username!.ToLower() == username)
                    .AnyAsync(cancellationToken)
            )
            {
                throw new RestException(HttpStatusCode.Conflict, "username", Constants.IN_USE);
            }

            if (
                await context
                    .Persons.Where(x => x.Email!.ToLower() == email)
                    .AnyAsync(cancellationToken)
            )
            {
                throw new RestException(HttpStatusCode.Conflict, "email", Constants.IN_USE);
            }
#pragma warning restore CA1304, CA1311

            var salt = Guid.NewGuid().ToByteArray();
            var person = new Person
            {
                Username = message.User.Username,
                Email = message.User.Email,
                Hash = await passwordHasher.Hash(
                    message.User.Password ?? throw new InvalidOperationException(),
                    salt
                ),
                Salt = salt,
            };

            await context.Persons.AddAsync(person, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);

            var user = mapper.PersonToUser(person);
            user.Token = jwtTokenGenerator.CreateToken(
                person.Username ?? throw new InvalidOperationException()
            );
            return new UserEnvelope(user);
        }
    }
}
