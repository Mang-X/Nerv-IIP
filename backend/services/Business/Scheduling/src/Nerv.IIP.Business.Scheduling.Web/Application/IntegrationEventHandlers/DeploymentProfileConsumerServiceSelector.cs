using DotNetCore.CAP;
using DotNetCore.CAP.Internal;
using Nerv.IIP.Contracts.Maintenance;

namespace Nerv.IIP.Business.Scheduling.Web.Application.IntegrationEventHandlers;

public sealed class DeploymentProfileConsumerServiceSelector(
    IServiceProvider serviceProvider,
    IHostEnvironment hostEnvironment) : ConsumerServiceSelector(serviceProvider)
{
    protected override IEnumerable<ConsumerExecutorDescriptor> FindConsumersFromInterfaceTypes(IServiceProvider provider) =>
        base.FindConsumersFromInterfaceTypes(provider).Select(Resolve);

    protected override IEnumerable<ConsumerExecutorDescriptor> FindConsumersFromControllerTypes() =>
        base.FindConsumersFromControllerTypes().Select(Resolve);

    private ConsumerExecutorDescriptor Resolve(ConsumerExecutorDescriptor descriptor)
    {
        var topic = AssetUnavailableIntegrationEventTopics.ResolveSubscriptionTemplate(
            descriptor.Attribute.Name, hostEnvironment.EnvironmentName);
        if (topic == descriptor.Attribute.Name)
            return descriptor;
        descriptor.Attribute = new CapSubscribeAttribute(topic, descriptor.Attribute.IsPartial)
        {
            Group = descriptor.Attribute.Group,
            GroupConcurrent = descriptor.Attribute.GroupConcurrent,
        };
        return descriptor;
    }
}
