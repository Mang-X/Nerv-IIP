using Nerv.IIP.Iam.Domain.AggregatesModel.UserAggregate;
using Nerv.IIP.Iam.Infrastructure;
using Nerv.IIP.Iam.Infrastructure.Repositories;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Iam.Web.Application.Commands.Users;

public sealed record DisableUserCommand(string UserId) : ICommand;

public sealed class DisableUserCommandHandler(IServiceProvider services)
    : ICommandHandler<DisableUserCommand>
{
    public async Task Handle(DisableUserCommand request, CancellationToken cancellationToken)
    {
        if (services.GetService<IUserRepository>() is null)
        {
            services.GetRequiredService<InMemoryIamStore>().DisableUser(request.UserId);
            return;
        }

        var repository = services.GetRequiredService<IUserRepository>();
        var user = await repository.GetByIdAsync(new UserId(request.UserId), cancellationToken)
            ?? throw new KnownException($"User '{request.UserId}' was not found.");
        user.Disable();
    }
}
