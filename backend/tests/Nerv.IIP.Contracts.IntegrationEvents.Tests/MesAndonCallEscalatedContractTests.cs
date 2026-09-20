using Nerv.IIP.Contracts.Mes;

namespace Nerv.IIP.Contracts.IntegrationEvents.Tests;

public sealed class MesAndonCallEscalatedContractTests
{
    [Fact]
    public void Andon_call_escalated_topic_is_built_from_its_canonical_template()
    {
        const string expectedTemplate =
            "nerv-iip.{deployment-profile}.business-mes.mes.andon-call-escalated.v1";
        const string expectedProductionTopic =
            "nerv-iip.production.business-mes.mes.andon-call-escalated.v1";

        Assert.Equal(expectedTemplate, AndonCallEscalatedIntegrationEvent.TopicTemplate);
        Assert.Equal(expectedProductionTopic, AndonCallEscalatedIntegrationEvent.Topic("Production"));
        Assert.Equal(
            AndonCallEscalatedIntegrationEvent.Topic("Production"),
            AndonCallEscalatedIntegrationEvent.TopicTemplate.Replace(
                "{deployment-profile}",
                "production",
                StringComparison.Ordinal));
    }
}
