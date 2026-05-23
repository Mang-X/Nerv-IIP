using System.Text.Json;
using Nerv.IIP.Business.Approval.Domain.AggregatesModel.ApprovalChainAggregate;
using Nerv.IIP.Business.Approval.Domain.DomainEvents;
using Nerv.IIP.Business.Approval.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Approval.Web.Application.IntegrationEvents;

namespace Nerv.IIP.Business.Approval.Web.Tests;

public sealed class ApprovalIntegrationEventTests
{
    [Fact]
    public void Approval_started_event_uses_stable_adr0011_envelope_shape()
    {
        var chain = NewChain();
        var converter = new ApprovalStartedIntegrationEventConverter();

        var integrationEvent = converter.Convert(new ApprovalStartedDomainEvent(chain));
        var json = JsonSerializer.Serialize(integrationEvent, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(ApprovalIntegrationEventTypes.ApprovalStarted, integrationEvent.EventType);
        Assert.Equal("business-approval", integrationEvent.SourceService);
        Assert.Equal("org-001", integrationEvent.OrganizationId);
        Assert.Equal("env-dev", integrationEvent.EnvironmentId);
        Assert.Equal("ECO-1001", integrationEvent.Payload.DocumentReference.DocumentId);
        Assert.Contains("\"eventType\":\"businessApproval.ApprovalStarted\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Step_resolved_event_uses_required_event_type_and_actor_reference()
    {
        var chain = NewChain();
        var decision = chain.ResolveStep(1, "user", "u-engineering", "approve", "ok");
        var step = chain.Steps.Single(x => x.StepNo == 1);
        var converter = new ApprovalStepResolvedIntegrationEventConverter();

        var integrationEvent = converter.Convert(new ApprovalStepResolvedDomainEvent(chain, step, decision));

        Assert.Equal(ApprovalIntegrationEventTypes.StepResolved, integrationEvent.EventType);
        Assert.Equal("user", integrationEvent.Payload.ActorType);
        Assert.Equal("u-engineering", integrationEvent.Payload.ActorRef);
        Assert.Equal("approve", integrationEvent.Payload.Decision);
    }

    [Fact]
    public void Approval_approved_event_uses_required_event_type()
    {
        var chain = NewChain();
        chain.ResolveStep(1, "user", "u-engineering", "approve", "ok");
        var decision = chain.ResolveStep(2, "user", "u-quality", "approve", "ok");
        var converter = new ApprovalApprovedIntegrationEventConverter();

        var integrationEvent = converter.Convert(new ApprovalApprovedDomainEvent(chain, decision));

        Assert.Equal(ApprovalIntegrationEventTypes.ApprovalApproved, integrationEvent.EventType);
        Assert.Equal(ApprovalChainStatuses.Approved, integrationEvent.Payload.Result);
        Assert.Equal("u-quality", integrationEvent.Payload.ActorRef);
    }

    [Fact]
    public void Approval_rejected_event_uses_required_event_type()
    {
        var chain = NewChain();
        var decision = chain.ResolveStep(1, "user", "u-engineering", "reject", "no");
        var converter = new ApprovalRejectedIntegrationEventConverter();

        var integrationEvent = converter.Convert(new ApprovalRejectedDomainEvent(chain, decision));

        Assert.Equal(ApprovalIntegrationEventTypes.ApprovalRejected, integrationEvent.EventType);
        Assert.Equal(ApprovalChainStatuses.Rejected, integrationEvent.Payload.Result);
        Assert.Equal("u-engineering", integrationEvent.Payload.ActorRef);
    }

    private static ApprovalChain NewChain()
    {
        return ApprovalChain.Start(ApprovalEndpointContractTests.NewTemplate(), ApprovalEndpointContractTests.NewDocument(), "system:eco");
    }
}
