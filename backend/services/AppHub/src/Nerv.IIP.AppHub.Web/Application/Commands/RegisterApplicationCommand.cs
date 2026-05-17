using Nerv.IIP.AppHub.Domain;
using Nerv.IIP.AppHub.Domain.AggregatesModel.ApplicationAggregate;
using Nerv.IIP.AppHub.Domain.AggregatesModel.ApplicationInstanceAggregate;
using Nerv.IIP.AppHub.Domain.AggregatesModel.ManagedNodeAggregate;
using Nerv.IIP.AppHub.Infrastructure;
using Nerv.IIP.AppHub.Infrastructure.Repositories;
using Nerv.IIP.Contracts.ConnectorProtocol;
using NetCorePal.Extensions.Primitives;
using AppHubApplication = Nerv.IIP.AppHub.Domain.AggregatesModel.ApplicationAggregate.Application;

namespace Nerv.IIP.AppHub.Web.Application.Commands;

public record RegisterApplicationCommand(ApplicationRegistration Registration) : ICommand<RegistrationResult>;

public class RegisterApplicationCommandHandler(IServiceProvider services)
    : ICommandHandler<RegisterApplicationCommand, RegistrationResult>
{
    public async Task<RegistrationResult> Handle(RegisterApplicationCommand request, CancellationToken cancellationToken)
    {
        if (services.GetService<ApplicationDbContext>() is null)
        {
            return services.GetRequiredService<IAppHubStateStore>().Register(request.Registration);
        }

        var registration = request.Registration;
        var context = registration.Context;
        var idempotencyRepository = services.GetRequiredService<IRegistrationIdempotencyRepository>();
        var existing = await idempotencyRepository.GetByKeyAsync(registration.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            return new RegistrationResult(existing.RegistrationId, existing.InstanceKey);
        }

        var applicationRepository = services.GetRequiredService<IApplicationRepository>();
        var application = await applicationRepository.GetByKeyAsync(context.OrganizationId, context.EnvironmentId, registration.ApplicationKey, cancellationToken);
        if (application is null)
        {
            application = new AppHubApplication(context.OrganizationId, context.EnvironmentId, registration.ApplicationKey, registration.ApplicationName, registration.Version);
            await applicationRepository.AddAsync(application, cancellationToken);
        }
        else
        {
            application.RenameAndTrackVersion(registration.ApplicationName, registration.Version);
        }

        var nodeRepository = services.GetRequiredService<IManagedNodeRepository>();
        var node = await nodeRepository.GetByKeyAsync(context.OrganizationId, context.EnvironmentId, registration.NodeKey, cancellationToken);
        if (node is null)
        {
            node = new ManagedNode(context.OrganizationId, context.EnvironmentId, registration.NodeKey, registration.NodeName, registration.DeploymentKind);
            await nodeRepository.AddAsync(node, cancellationToken);
        }
        else
        {
            node.UpdateProfile(registration.NodeName, registration.DeploymentKind);
        }

        var instanceRepository = services.GetRequiredService<IApplicationInstanceRepository>();
        var instance = await instanceRepository.GetByInstanceKeyAsync(registration.InstanceKey, cancellationToken);
        if (instance is null)
        {
            instance = new ApplicationInstance(
                context.OrganizationId,
                context.EnvironmentId,
                registration.ApplicationKey,
                registration.Version,
                registration.NodeKey,
                registration.InstanceKey,
                registration.InstanceName,
                registration.Metadata,
                registration.Capabilities);
            await instanceRepository.AddAsync(instance, cancellationToken);
        }
        else
        {
            instance.UpdateRegistration(
                context.OrganizationId,
                context.EnvironmentId,
                registration.ApplicationKey,
                registration.Version,
                registration.NodeKey,
                registration.InstanceName,
                registration.Metadata,
                registration.Capabilities);
        }

        var registrationId = $"reg-{Guid.CreateVersion7():N}";
        await idempotencyRepository.AddAsync(new RegistrationIdempotency(registration.IdempotencyKey, registrationId, registration.InstanceKey), cancellationToken);
        return new RegistrationResult(registrationId, registration.InstanceKey);
    }
}
