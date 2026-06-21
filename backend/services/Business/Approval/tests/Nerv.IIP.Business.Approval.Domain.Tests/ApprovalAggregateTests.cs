using Nerv.IIP.Business.Approval.Domain.AggregatesModel.ApprovalChainAggregate;
using Nerv.IIP.Business.Approval.Domain.AggregatesModel.ApprovalTemplateAggregate;
using Nerv.IIP.Business.Approval.Domain.DomainEvents;

namespace Nerv.IIP.Business.Approval.Domain.Tests;

public sealed class ApprovalAggregateTests
{
    [Fact]
    public void Active_template_starts_chain_for_document_reference()
    {
        var template = NewTemplate();

        var chain = ApprovalChain.Start(template, NewDocument(), "system:eco");

        Assert.Equal("pending", chain.Status);
        Assert.Equal("eco", chain.DocumentReference.SourceService);
        Assert.Equal("ECO-1001", chain.DocumentReference.DocumentId);
        Assert.Equal(2, chain.Steps.Count);
        Assert.IsType<ApprovalStartedDomainEvent>(chain.GetDomainEvents().Single());
    }

    [Fact]
    public void Inactive_template_cannot_start_chain()
    {
        var template = ApprovalTemplate.Create(
            "org-001",
            "env-dev",
            "ECO-DEFAULT",
            "engineering-change-order",
            1,
            false,
            NewStepDefinitions());

        Assert.Throws<InvalidOperationException>(() => ApprovalChain.Start(template, NewDocument(), "system:eco"));
    }

    [Fact]
    public void Ordered_steps_must_resolve_in_sequence()
    {
        var chain = NewChain();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            chain.ResolveStep(2, "user", "u-quality", "approve", "ok"));

        Assert.Contains("sequence", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Duplicate_same_actor_same_decision_is_idempotent()
    {
        var chain = NewChain();
        var first = chain.ResolveStep(1, "user", "u-engineering", "approve", "ok");

        var second = chain.ResolveStep(1, "user", "u-engineering", "approve", "ok");

        Assert.Same(first, second);
        Assert.Single(chain.Decisions);
    }

    [Fact]
    public void Duplicate_same_actor_conflicting_decision_is_rejected()
    {
        var chain = NewChain();
        chain.ResolveStep(1, "user", "u-engineering", "approve", "ok");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            chain.ResolveStep(1, "user", "u-engineering", "reject", "no"));

        Assert.Contains("conflict", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejected_chain_is_terminal()
    {
        var chain = NewChain();
        chain.ResolveStep(1, "user", "u-engineering", "reject", "no");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            chain.ResolveStep(2, "user", "u-quality", "approve", "ok"));

        Assert.Equal("rejected", chain.Status);
        Assert.Contains("terminal", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(chain.GetDomainEvents(), x => x is ApprovalRejectedDomainEvent);
    }

    [Fact]
    public void Returned_chain_is_terminal_and_emits_returned_event()
    {
        var chain = NewChain();
        chain.ResolveStep(1, "user", "u-engineering", "return", "needs changes");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            chain.ResolveStep(2, "user", "u-quality", "approve", "ok"));

        Assert.Equal("returned", chain.Status);
        Assert.Contains("terminal", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(chain.GetDomainEvents(), x => x is ApprovalReturnedDomainEvent);
    }

    [Fact]
    public void Pending_chain_can_be_withdrawn_and_no_longer_resolved()
    {
        var chain = NewChain();

        chain.Withdraw("user", "u-requester", "duplicate request", DateTimeOffset.Parse("2026-06-21T08:00:00Z"));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            chain.ResolveStep(1, "user", "u-engineering", "approve", "ok"));
        Assert.Equal("withdrawn", chain.Status);
        Assert.Contains("terminal", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Returned_or_withdrawn_chain_can_be_resubmitted()
    {
        var chain = NewChain();
        chain.ResolveStep(1, "user", "u-engineering", "return", "needs changes");

        chain.Resubmit("user", "u-requester", "reworked", DateTimeOffset.Parse("2026-06-21T09:00:00Z"));

        Assert.Equal("pending", chain.Status);
        Assert.Null(chain.CompletedAtUtc);
        Assert.Equal(ApprovalStepStatuses.Pending, chain.Steps.Single(x => x.StepNo == 1).Status);
    }

    [Fact]
    public void Resubmitted_returned_step_can_be_decided_again_by_same_actor()
    {
        var chain = NewChain();
        chain.ResolveStep(1, "user", "u-engineering", "return", "needs changes");
        chain.Resubmit("user", "u-requester", "reworked", DateTimeOffset.UtcNow.AddMinutes(1));

        var decision = chain.ResolveStep(1, "user", "u-engineering", "approve", "ok now");

        Assert.Equal(ApprovalDecisions.Approve, decision.Decision);
        Assert.Equal(2, chain.Decisions.Count);
    }

    [Fact]
    public void Pending_chain_cannot_be_resubmitted_without_terminal_rework_state()
    {
        var chain = NewChain();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            chain.Resubmit("user", "u-requester", "retry", DateTimeOffset.Parse("2026-06-21T09:00:00Z")));

        Assert.Contains("returned or withdrawn", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Add_signer_requires_the_added_current_step_before_next_step_can_resolve()
    {
        var chain = NewChain();

        var addedStep = chain.AddSigner(1, "user", "u-finance", "user", "u-engineering", "amount threshold");
        chain.ResolveStep(1, "user", "u-engineering", "approve", "ok");
        var premature = Assert.Throws<InvalidOperationException>(() =>
            chain.ResolveStep(2, "user", "u-quality", "approve", "ok"));
        chain.ResolveStep(1, "user", "u-finance", "approve", "ok");
        chain.ResolveStep(2, "user", "u-quality", "approve", "ok");

        Assert.Equal(1, addedStep.StepNo);
        Assert.Contains("sequence", premature.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("approved", chain.Status);
    }

    [Fact]
    public void Transfer_reassigns_current_pending_step_and_blocks_previous_actor()
    {
        var chain = NewChain();

        chain.Transfer(1, "user", "u-engineering", "user", "u-backup", "user", "u-manager", "shift change");

        var previousActor = Assert.Throws<InvalidOperationException>(() =>
            chain.ResolveStep(1, "user", "u-engineering", "approve", "ok"));
        chain.ResolveStep(1, "user", "u-backup", "approve", "ok");

        Assert.Contains("assigned", previousActor.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(chain.Steps, x => x.StepNo == 1 && x.ApproverRef == "u-backup");
    }

    [Fact]
    public void Approved_chain_emits_approved_event_after_last_required_step()
    {
        var chain = NewChain();
        chain.ResolveStep(1, "user", "u-engineering", "approve", "ok");
        chain.ClearDomainEvents();

        chain.ResolveStep(2, "user", "u-quality", "approve", "ok");

        Assert.Equal("approved", chain.Status);
        Assert.Contains(chain.GetDomainEvents(), x => x is ApprovalApprovedDomainEvent);
    }

    [Fact]
    public void Parallel_group_with_all_policy_requires_every_approver_before_next_step()
    {
        var template = ApprovalTemplate.Create(
            "org-001",
            "env-dev",
            "COUNT-VARIANCE",
            "inventory-count-variance",
            1,
            true,
            [
                new ApprovalTemplateStepDefinition(1, "Finance review", "finance-qc", "user", "u-finance", 24),
                new ApprovalTemplateStepDefinition(1, "Quality review", "finance-qc", "user", "u-quality", 24),
                new ApprovalTemplateStepDefinition(2, "Manager review", null, "user", "u-manager", 24),
            ]);
        var chain = ApprovalChain.Start(template, NewDocument(), "system:inventory");

        chain.ResolveStep(1, "user", "u-finance", "approve", "ok");
        var premature = Assert.Throws<InvalidOperationException>(() =>
            chain.ResolveStep(2, "user", "u-manager", "approve", "ok"));
        chain.ResolveStep(1, "user", "u-quality", "approve", "ok");
        chain.ResolveStep(2, "user", "u-manager", "approve", "ok");

        Assert.Contains("sequence", premature.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("approved", chain.Status);
    }

    [Fact]
    public void Parallel_group_with_any_policy_allows_one_approver_to_unlock_next_step()
    {
        var template = ApprovalTemplate.Create(
            "org-001",
            "env-dev",
            "COUNT-VARIANCE",
            "inventory-count-variance",
            1,
            true,
            [
                new ApprovalTemplateStepDefinition(1, "Finance review", "finance-qc", "user", "u-finance", 24, "any"),
                new ApprovalTemplateStepDefinition(1, "Quality review", "finance-qc", "user", "u-quality", 24, "any"),
                new ApprovalTemplateStepDefinition(2, "Manager review", null, "user", "u-manager", 24),
            ]);
        var chain = ApprovalChain.Start(template, NewDocument(), "system:inventory");

        chain.ResolveStep(1, "user", "u-finance", "approve", "ok");
        chain.ResolveStep(2, "user", "u-manager", "approve", "ok");

        Assert.Equal("approved", chain.Status);
        Assert.Contains(chain.Steps.Where(x => x.StepNo == 1), x => x.Status == ApprovalStepStatuses.Skipped);
    }

    [Fact]
    public void Duplicate_step_number_requires_explicit_parallel_group()
    {
        Assert.Throws<ArgumentException>(() => ApprovalTemplate.Create(
            "org-001",
            "env-dev",
            "COUNT-VARIANCE",
            "inventory-count-variance",
            1,
            true,
            [
                new ApprovalTemplateStepDefinition(1, "Finance review", null, "user", "u-finance", 24),
                new ApprovalTemplateStepDefinition(1, "Quality review", null, "user", "u-quality", 24),
            ]));
    }

    [Fact]
    public void Conditional_steps_are_included_only_when_document_reference_matches()
    {
        var template = ApprovalTemplate.Create(
            "org-001",
            "env-dev",
            "DOC-ROUTING",
            "engineering-change-order",
            1,
            true,
            [
                new ApprovalTemplateStepDefinition(1, "Engineering review", null, "user", "u-engineering", 24, "all", "documentType=engineering-change-order"),
                new ApprovalTemplateStepDefinition(2, "Procurement review", null, "user", "u-procurement", 24, "all", "documentType=purchase-order"),
                new ApprovalTemplateStepDefinition(3, "Manager review", null, "user", "u-manager", 24),
            ]);

        var chain = ApprovalChain.Start(template, NewDocument(), "system:eco");

        Assert.DoesNotContain(chain.Steps, x => x.ApproverRef == "u-procurement");
        Assert.Contains(chain.Steps, x => x.ApproverRef == "u-engineering");
    }

    [Fact]
    public void Overdue_pending_steps_emit_once()
    {
        var chain = NewChain();
        var dueAt = chain.Steps.Min(x => x.DueAtUtc)!.Value;

        var first = chain.MarkOverdueSteps(dueAt.AddSeconds(1));
        var second = chain.MarkOverdueSteps(dueAt.AddHours(1));

        Assert.Equal(1, first);
        Assert.Equal(0, second);
        Assert.Contains(chain.GetDomainEvents(), x => x is ApprovalStepOverdueDomainEvent);
    }

    private static ApprovalChain NewChain()
    {
        return ApprovalChain.Start(NewTemplate(), NewDocument(), "system:eco");
    }

    private static ApprovalTemplate NewTemplate()
    {
        return ApprovalTemplate.Create(
            "org-001",
            "env-dev",
            "ECO-DEFAULT",
            "engineering-change-order",
            1,
            true,
            NewStepDefinitions());
    }

    private static ApprovalTemplateStepDefinition[] NewStepDefinitions()
    {
        return
        [
            new ApprovalTemplateStepDefinition(1, "Engineering review", null, "user", "u-engineering", 24),
            new ApprovalTemplateStepDefinition(2, "Quality review", null, "user", "u-quality", 24),
        ];
    }

    private static ApprovalDocumentReference NewDocument()
    {
        return new ApprovalDocumentReference("ECO", "engineering-change-order", "ECO-1001", "LINE-1");
    }
}
