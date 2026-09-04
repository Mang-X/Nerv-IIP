using DotNetCore.CAP;
using DotNetCore.CAP.Internal;
using Nerv.IIP.Contracts.Maintenance;

namespace Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;

public sealed class MesDeploymentProfileConsumerServiceSelector(
    IServiceProvider serviceProvider,
    string deploymentProfile)
    : ConsumerServiceSelector(serviceProvider)
{
    protected override IEnumerable<ConsumerExecutorDescriptor> FindConsumersFromInterfaceTypes(IServiceProvider provider) =>
        base.FindConsumersFromInterfaceTypes(provider).Select(ResolveDeploymentProfile);

    protected override IEnumerable<ConsumerExecutorDescriptor> FindConsumersFromControllerTypes() =>
        base.FindConsumersFromControllerTypes().Select(ResolveDeploymentProfile);

    private ConsumerExecutorDescriptor ResolveDeploymentProfile(ConsumerExecutorDescriptor descriptor)
    {
        var resolvedTopic = AssetUnavailableIntegrationEventTopics.ResolveSubscriptionTemplate(
            descriptor.Attribute.Name,
            deploymentProfile);
        if (string.Equals(resolvedTopic, descriptor.Attribute.Name, StringComparison.Ordinal))
        {
            return descriptor;
        }

        descriptor.Attribute = new CapSubscribeAttribute(resolvedTopic, descriptor.Attribute.IsPartial)
        {
            Group = descriptor.Attribute.Group,
            GroupConcurrent = descriptor.Attribute.GroupConcurrent,
        };
        return descriptor;
    }
}
