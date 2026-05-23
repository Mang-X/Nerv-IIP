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
    public void Parallel_group_allows_same_step_number_to_resolve_before_next_step()
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
    public void Parallel_group_key_is_metadata_and_same_step_still_requires_all_approvers()
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

        Assert.All(chain.Steps.Where(x => x.StepNo == 1), x => Assert.Equal("finance-qc", x.ParallelGroupKey));
        Assert.Throws<InvalidOperationException>(() => chain.ResolveStep(2, "user", "u-manager", "approve", "ok"));
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
