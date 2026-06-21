using System.Text.Json;
using Nerv.IIP.Business.Approval.Domain.AggregatesModel.ApprovalChainAggregate;
using Nerv.IIP.Business.Approval.Domain.DomainEvents;
using Nerv.IIP.Business.Approval.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Contracts.Approval;

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
        var decision = chain.ResolveStep(1, "user", "u-delegate", "approve", "ok", "user", "u-engineering");
        var step = chain.Steps.Single(x => x.StepNo == 1);
        var converter = new ApprovalStepResolvedIntegrationEventConverter();

        var integrationEvent = converter.Convert(new ApprovalStepResolvedDomainEvent(chain, step, decision));

        Assert.Equal(ApprovalIntegrationEventTypes.StepResolved, integrationEvent.EventType);
        Assert.Equal("user", integrationEvent.Payload.ActorType);
        Assert.Equal("u-delegate", integrationEvent.Payload.ActorRef);
        Assert.Equal("user", integrationEvent.Payload.OnBehalfOfActorType);
        Assert.Equal("u-engineering", integrationEvent.Payload.OnBehalfOfActorRef);
        Assert.Equal("approve", integrationEvent.Payload.Decision);
        Assert.Contains(":user:u-engineering", integrationEvent.IdempotencyKey, StringComparison.Ordinal);
    }

    [Fact]
    public void Step_resolved_event_idempotency_distinguishes_resubmission_rounds()
    {
        var chain = NewChain();
        var firstDecision = chain.ResolveStep(1, "user", "u-engineering", "return", "needs changes");
        chain.Resubmit("user", "u-requester", "reworked", DateTimeOffset.Parse("2026-06-21T09:00:00Z"));
        var secondDecision = chain.ResolveStep(1, "user", "u-engineering", "approve", "ok");
        var step = chain.Steps.Single(x => x.StepNo == 1);
        var converter = new ApprovalStepResolvedIntegrationEventConverter();

        var firstEvent = converter.Convert(new ApprovalStepResolvedDomainEvent(chain, step, firstDecision));
        var secondEvent = converter.Convert(new ApprovalStepResolvedDomainEvent(chain, step, secondDecision));

        Assert.NotEqual(firstEvent.IdempotencyKey, secondEvent.IdempotencyKey);
        Assert.Contains(":1:1:", firstEvent.IdempotencyKey, StringComparison.Ordinal);
        Assert.Contains(":2:1:", secondEvent.IdempotencyKey, StringComparison.Ordinal);
    }

    [Fact]
    public void Step_overdue_event_uses_required_event_type_and_due_metadata()
    {
        var chain = NewChain();
        var step = chain.Steps.Single(x => x.StepNo == 1);
        var markedAtUtc = step.DueAtUtc!.Value.AddMinutes(1);
        chain.MarkOverdueSteps(markedAtUtc);
        var converter = new ApprovalStepOverdueIntegrationEventConverter();

        var integrationEvent = converter.Convert(new ApprovalStepOverdueDomainEvent(chain, step, markedAtUtc));

        Assert.Equal(ApprovalIntegrationEventTypes.StepOverdue, integrationEvent.EventType);
        Assert.Equal("user", integrationEvent.Payload.ApproverType);
        Assert.Equal("u-engineering", integrationEvent.Payload.ApproverRef);
        Assert.Equal(step.DueAtUtc, integrationEvent.Payload.DueAtUtc);
        Assert.Equal(markedAtUtc, integrationEvent.Payload.MarkedAtUtc);
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
        Assert.Null(integrationEvent.Payload.OnBehalfOfActorRef);
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

    [Fact]
    public void Approval_returned_event_uses_required_event_type()
    {
        var chain = NewChain();
        var decision = chain.ResolveStep(1, "user", "u-engineering", "return", "needs changes");
        var converter = new ApprovalReturnedIntegrationEventConverter();

        var integrationEvent = converter.Convert(new ApprovalReturnedDomainEvent(chain, decision));

        Assert.Equal(ApprovalIntegrationEventTypes.ApprovalReturned, integrationEvent.EventType);
        Assert.Equal(ApprovalChainStatuses.Returned, integrationEvent.Payload.Result);
        Assert.Equal("u-engineering", integrationEvent.Payload.ActorRef);
    }

    [Fact]
    public void Approval_action_recorded_event_carries_actor_reason_and_recipients()
    {
        var chain = NewChain();
        chain.Transfer(1, "user", "u-engineering", "user", "u-backup", "user", "u-manager", "shift change");
        var domainEvent = chain.GetDomainEvents().OfType<ApprovalChainActionRecordedDomainEvent>().Single();
        var converter = new ApprovalChainActionRecordedIntegrationEventConverter();

        var integrationEvent = converter.Convert(domainEvent);

        Assert.Equal(ApprovalIntegrationEventTypes.ActionRecorded, integrationEvent.EventType);
        Assert.Equal(ApprovalDecisions.Transfer, integrationEvent.Payload.Action);
        Assert.Equal("user", integrationEvent.Payload.ActorType);
        Assert.Equal("u-manager", integrationEvent.Payload.ActorRef);
        Assert.Equal("shift change", integrationEvent.Payload.Reason);
        Assert.Contains("user:u-backup", integrationEvent.Payload.SuggestedRecipientRefs);
    }

    [Fact]
    public void Approval_action_recorded_idempotency_key_distinguishes_repeated_actions_in_the_same_round()
    {
        var chain = NewChain();
        chain.AddSigner(1, "user", "u-backup-1", "user", "u-manager", "extra approver");
        chain.AddSigner(1, "user", "u-backup-2", "user", "u-manager", "extra approver");
        var domainEvents = chain.GetDomainEvents().OfType<ApprovalChainActionRecordedDomainEvent>().ToArray();
        var converter = new ApprovalChainActionRecordedIntegrationEventConverter();

        var firstEvent = converter.Convert(domainEvents[0]);
        var secondEvent = converter.Convert(domainEvents[1]);

        Assert.NotEqual(firstEvent.IdempotencyKey, secondEvent.IdempotencyKey);
        Assert.Contains(domainEvents[0].Decision.Id.ToString(), firstEvent.IdempotencyKey, StringComparison.Ordinal);
        Assert.Contains(domainEvents[1].Decision.Id.ToString(), secondEvent.IdempotencyKey, StringComparison.Ordinal);
    }

    private static ApprovalChain NewChain()
    {
        return ApprovalChain.Start(ApprovalEndpointContractTests.NewTemplate(), ApprovalEndpointContractTests.NewDocument(), "system:eco");
    }
}
