using DotNetCore.CAP;
using DotNetCore.CAP.Internal;
using Nerv.IIP.Contracts.Mes;

namespace Nerv.IIP.Business.Erp.Web.Application.IntegrationEventHandlers;

/// <summary>
/// Resolves canonical MES topic templates against the deployment profile that hosts this ERP process.
/// </summary>
public sealed class DeploymentProfileConsumerServiceSelector(
    IServiceProvider serviceProvider,
    IHostEnvironment hostEnvironment)
    : ConsumerServiceSelector(serviceProvider)
{
    protected override IEnumerable<ConsumerExecutorDescriptor> FindConsumersFromInterfaceTypes(IServiceProvider provider) =>
        base.FindConsumersFromInterfaceTypes(provider).Select(ResolveDeploymentProfile);

    protected override IEnumerable<ConsumerExecutorDescriptor> FindConsumersFromControllerTypes() =>
        base.FindConsumersFromControllerTypes().Select(ResolveDeploymentProfile);

    private ConsumerExecutorDescriptor ResolveDeploymentProfile(ConsumerExecutorDescriptor descriptor)
    {
        var resolvedTopic = MesActualTimeIntegrationEventTopics.ResolveSubscriptionTemplate(
            descriptor.Attribute.Name,
            hostEnvironment.EnvironmentName);
        if (string.Equals(resolvedTopic, descriptor.Attribute.Name, StringComparison.Ordinal))
            return descriptor;

        descriptor.Attribute = new CapSubscribeAttribute(resolvedTopic, descriptor.Attribute.IsPartial)
        {
            Group = descriptor.Attribute.Group,
            GroupConcurrent = descriptor.Attribute.GroupConcurrent,
        };
        return descriptor;
    }
}
